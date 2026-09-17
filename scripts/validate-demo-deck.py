#!/usr/bin/env python3
"""Validate the Reveal.js demo deck and export its browser-smoke contract."""

import argparse
import html
import json
import re
import sys
from pathlib import Path
from urllib.parse import parse_qsl, urlsplit


NOTE_FIELD_NAMES = ("Activate demo", "Action", "Look for")
FIELD_NAMES = ("ArcadeDB", *NOTE_FIELD_NAMES)
FALLBACK_ROUTES = {"demo/:feature", "demo/:feature/:slug", "**"}
SECTION_RE = re.compile(r"<section\b(?P<attrs>[^>]*)>(?P<body>.*?)</section>", re.IGNORECASE | re.DOTALL)
ASIDE_RE = re.compile(r"<aside\b(?P<attrs>[^>]*)>(?P<body>.*?)</aside>", re.IGNORECASE | re.DOTALL)
ATTRIBUTE_RE = re.compile(r"([\w-]+)\s*=\s*(['\"])(.*?)\2", re.DOTALL)
PATH_RE = re.compile(r"\bpath:\s*['\"]([^'\"]+)['\"]")
TAG_RE = re.compile(r"<[^>]+>")


def route_matches(template: str, path: str) -> bool:
    expected = template.strip("/").split("/")
    actual = path.strip("/").split("/")
    return len(expected) == len(actual) and all(
        part.startswith(":") or part == value for part, value in zip(expected, actual)
    )


def attributes(source: str) -> dict[str, str]:
    return {name: html.unescape(value) for name, _, value in ATTRIBUTE_RE.findall(source)}


def plain_text(source: str) -> str:
    return " ".join(html.unescape(TAG_RE.sub(" ", source)).split())


def field(slide: str, name: str) -> str | None:
    match = re.search(
        rf"<p\b[^>]*>\s*<strong\b[^>]*>\s*{re.escape(name)}:\s*</strong>(.*?)</p>",
        slide,
        re.IGNORECASE | re.DOTALL,
    )
    value = plain_text(match.group(1)) if match else ""
    return value or None


def validate(deck_path: Path, manifest_path: Path, routes_path: Path):
    errors: list[str] = []
    deck = deck_path.read_text(encoding="utf-8")
    slides = list(SECTION_RE.finditer(deck))
    if not slides:
        errors.append("deck does not contain any Reveal.js section elements")
    if deck.lower().count("<section") != len(slides) or deck.lower().count("</section>") != len(slides):
        errors.append("deck contains an unclosed or nested section element")

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
    for index, slide_match in enumerate(slides, 1):
        slide_attrs = attributes(slide_match.group("attrs"))
        slide = slide_match.group("body")
        kind = slide_attrs.get("data-slide-kind")
        activation_id = slide_attrs.get("data-slide-id", "")
        if kind not in {"demo", "slide"}:
            errors.append(f"slide {index} must have data-slide-kind=demo or slide")
            continue
        if not re.fullmatch(r"[a-z0-9]+(?:-[a-z0-9]+)*", activation_id):
            errors.append(f"slide {index} has an invalid or missing data-slide-id")
            continue
        if activation_id in ids:
            errors.append(f"duplicate activation id: {activation_id}")
        ids.add(activation_id)
        if kind == "slide":
            if field(slide, "Activate demo"):
                errors.append(f"nonfeature slide {activation_id} contains Activate demo")
            continue

        note_matches = [
            match for match in ASIDE_RE.finditer(slide)
            if "notes" in attributes(match.group("attrs")).get("class", "").split()
        ]
        if len(note_matches) != 1:
            errors.append(f"feature {activation_id} must have exactly one presenter notes block")
            notes = ""
        else:
            notes = note_matches[0].group("body")
        visible_slide = ASIDE_RE.sub("", slide)

        title_match = re.search(r"<h2\b[^>]*>(.*?)</h2>", visible_slide, re.IGNORECASE | re.DOTALL)
        title = plain_text(title_match.group(1)) if title_match else ""
        if not title:
            errors.append(f"feature {activation_id} is missing an H2 title")
            continue
        values = {
            "ArcadeDB": field(visible_slide, "ArcadeDB"),
            **{name: field(notes, name) for name in NOTE_FIELD_NAMES},
        }
        if not values["ArcadeDB"]:
            errors.append(f"feature {activation_id} is missing ArcadeDB")
        for name in NOTE_FIELD_NAMES:
            value = values[name]
            if not value:
                errors.append(f"feature {activation_id} presenter notes are missing {name}")
            if field(visible_slide, name):
                errors.append(f"feature {activation_id} exposes presenter-only {name} on the slide")

        activation = re.search(
            r"<p\b[^>]*>\s*<strong\b[^>]*>\s*Activate demo:\s*</strong>\s*"
            r"<a\b(?P<attrs>[^>]*)>.*?</a>\s*</p>",
            notes,
            re.IGNORECASE | re.DOTALL,
        )
        if not activation:
            continue
        link_attrs = attributes(activation.group("attrs"))
        url = link_attrs.get("href", "")
        demo_path = link_attrs.get("data-demo-path", "")
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
        if demo_path != parsed.path + (f"?{parsed.query}" if parsed.query else ""):
            errors.append(f"feature {activation_id} data-demo-path does not match href")
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
                "title": title,
                "url": url,
                "action": values["Action"],
                "lookFor": values["Look for"],
            })
    return features, errors


def main() -> int:
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--deck",
        type=Path,
        default=root / "src/coffee-community-slides/public/slides.html",
    )
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
        slide_count = len(SECTION_RE.findall(args.deck.read_text(encoding="utf-8")))
        print(f"Validated {len(features)} feature slides across {slide_count} Reveal.js slides.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
