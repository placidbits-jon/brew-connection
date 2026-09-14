#!/usr/bin/env python3
"""Validate the Obsidian demo deck and export its browser-smoke contract."""

import argparse
import json
import re
import sys
from pathlib import Path
from urllib.parse import parse_qsl, urlsplit


FIELD_NAMES = ("ArcadeDB", "Activate demo", "Action", "Look for")
FALLBACK_ROUTES = {"demo/:feature", "demo/:feature/:slug", "**"}
MARKER_RE = re.compile(r"^<!--\s*(demo|slide):\s*([a-z0-9]+(?:-[a-z0-9]+)*)\s*-->$", re.MULTILINE)
PATH_RE = re.compile(r"\bpath:\s*['\"]([^'\"]+)['\"]")


def route_matches(template: str, path: str) -> bool:
    expected = template.strip("/").split("/")
    actual = path.strip("/").split("/")
    return len(expected) == len(actual) and all(
        part.startswith(":") or part == value for part, value in zip(expected, actual)
    )


def field(slide: str, name: str) -> str | None:
    match = re.search(rf"^\*\*{re.escape(name)}:\*\*[ \t]*([^\r\n]*?)[ \t]*$", slide, re.MULTILINE)
    value = match.group(1).strip() if match else ""
    return value or None


def validate(deck_path: Path, manifest_path: Path, routes_path: Path):
    errors: list[str] = []
    deck = deck_path.read_text(encoding="utf-8")
    malformed = [str(number) for number, line in enumerate(deck.splitlines(), 1)
                 if line.strip() == "---" and line != "---"]
    if malformed:
        errors.append(f"malformed slide separator on line(s) {', '.join(malformed)}")
    slides = re.split(r"^---$", deck, flags=re.MULTILINE)
    if any(not slide.strip() for slide in slides):
        errors.append("empty slide caused by a leading, trailing, or repeated separator")

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest_routes = manifest["routes"]
        if not isinstance(manifest_routes, list) or not all(isinstance(item, str) for item in manifest_routes):
            raise ValueError("routes must be an array of strings")
    except (OSError, json.JSONDecodeError, KeyError, ValueError) as exc:
        return [], [f"invalid route manifest: {exc}"]

    angular_routes = set(PATH_RE.findall(routes_path.read_text(encoding="utf-8")))
    concrete_angular_routes = {
        route for route in angular_routes
        if route.startswith("demo/") and route not in FALLBACK_ROUTES
    }
    for route in manifest_routes:
        if route in FALLBACK_ROUTES or route not in concrete_angular_routes:
            errors.append(f"manifest route is not concrete in Angular routes: {route}")
    if len(manifest_routes) != len(set(manifest_routes)):
        errors.append("duplicate route in manifest")
    for route in sorted(concrete_angular_routes - set(manifest_routes)):
        errors.append(f"concrete Angular route is missing from manifest: {route}")

    features = []
    ids: set[str] = set()
    for index, slide in enumerate(slides, 1):
        markers = MARKER_RE.findall(slide)
        if len(markers) != 1:
            errors.append(f"slide {index} must have exactly one demo or slide marker")
            continue
        kind, activation_id = markers[0]
        if activation_id in ids:
            errors.append(f"duplicate activation id: {activation_id}")
        ids.add(activation_id)
        if kind == "slide":
            if field(slide, "Activate demo"):
                errors.append(f"nonfeature slide {activation_id} contains Activate demo")
            continue

        title_match = re.search(r"^##\s+(.+?)\s*$", slide, re.MULTILINE)
        if not title_match:
            errors.append(f"feature {activation_id} is missing an H2 title")
            continue
        values = {name: field(slide, name) for name in FIELD_NAMES}
        for name, value in values.items():
            if not value:
                errors.append(f"feature {activation_id} is missing {name}")
        activation = values["Activate demo"]
        if not activation:
            continue
        link = re.fullmatch(r"\[[^\]]+\]\(([^)]+)\)", activation)
        if not link:
            errors.append(f"feature {activation_id} has malformed Activate demo link")
            continue
        url = link.group(1)
        try:
            parsed = urlsplit(url)
            valid_origin = parsed.scheme == "http" and parsed.hostname == "localhost" and parsed.port == 4200
        except ValueError:
            parsed = None
            valid_origin = False
        if (not parsed or not valid_origin or parsed.fragment or parsed.username or parsed.password
                or any(character.isspace() for character in url)
                or re.search(r"%(?![0-9A-Fa-f]{2})", url)):
            errors.append(f"feature {activation_id} has malformed demo URL: {url}")
            continue
        try:
            query = parse_qsl(parsed.query, keep_blank_values=True, strict_parsing=True)
        except ValueError as exc:
            errors.append(f"feature {activation_id} has malformed query: {exc}")
            query = []
        keys = [key for key, _ in query]
        if len(keys) != len(set(keys)):
            errors.append(f"feature {activation_id} has duplicate query parameter")
        if any(not key or not value for key, value in query):
            errors.append(f"feature {activation_id} has an empty query key or value")
        relative_path = parsed.path.lstrip("/")
        if not any(route_matches(route, relative_path) for route in manifest_routes):
            errors.append(f"feature {activation_id} URL is absent from route manifest: {parsed.path}")
        if not any(route_matches(route, relative_path) for route in concrete_angular_routes):
            errors.append(f"feature {activation_id} URL is not a concrete Angular route: {parsed.path}")
        if all(values.values()):
            features.append({
                "id": activation_id,
                "title": title_match.group(1),
                "url": url,
                "action": values["Action"],
                "lookFor": values["Look for"],
            })
    return features, errors


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--deck", type=Path, default=root / "docs/arcadedb-coffee-demo.md")
    parser.add_argument("--manifest", type=Path, default=root / "src/coffee-community-web/src/app/demo-route-manifest.json")
    parser.add_argument("--routes", type=Path, default=root / "src/coffee-community-web/src/app/app.routes.ts")
    parser.add_argument("--json", action="store_true", help="write the feature contract as JSON")
    args = parser.parse_args()
    features, errors = validate(args.deck, args.manifest, args.routes)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    if args.json:
        print(json.dumps(features, indent=2, ensure_ascii=False))
    else:
        slide_count = len(re.split(r"^---$", args.deck.read_text(encoding="utf-8"), flags=re.MULTILINE))
        print(f"Validated {len(features)} feature slides across {slide_count} deck slides.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
