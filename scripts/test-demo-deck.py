#!/usr/bin/env python3
"""Regression tests for the checked-in Reveal.js deck validator."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = ROOT / "scripts" / "validate-demo-deck.py"


def slide(slide_id: str = "title", body: str = "<h1>Deck</h1>") -> str:
    return f'<section data-slide-kind="slide" data-slide-id="{slide_id}">{body}</section>'


def feature(
    feature_id: str = "stage",
    title: str = "Stage",
    url: str = "http://localhost:4200/demo/story",
    arcade_db: str = "Graph",
    action: str = "Go.",
    look_for: str = "Ready.",
    demo_path: str | None = None,
) -> str:
    demo_path = demo_path if demo_path is not None else url.removeprefix("http://localhost:4200")
    return (
        f'<section data-slide-kind="demo" data-slide-id="{feature_id}">'
        f"<h2>{title}</h2>"
        f"<p><strong>ArcadeDB:</strong> {arcade_db}</p>"
        '<aside class="notes">'
        f'<p><strong>Activate demo:</strong> <a href="{url}" data-demo-path="{demo_path}">Open</a></p>'
        f"<p><strong>Action:</strong> {action}</p>"
        f"<p><strong>Look for:</strong> {look_for}</p>"
        "</aside>"
        "</section>"
    )


class DemoDeckValidatorTests(unittest.TestCase):
    def run_validator(self, deck: str, manifest=None, routes=None, *extra: str):
        manifest = manifest or {"routes": ["demo/story", "demo/recipes/:recipeSlug"]}
        routes = routes or "export const routes = [{ path: 'demo/story' }, { path: 'demo/recipes/:recipeSlug' }, { path: 'demo/:feature' }];"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            deck_path = root / "slides.html"
            manifest_path = root / "manifest.json"
            routes_path = root / "app.routes.ts"
            deck_path.write_text(deck, encoding="utf-8")
            manifest_path.write_text(json.dumps(manifest), encoding="utf-8")
            routes_path.write_text(routes, encoding="utf-8")
            return subprocess.run(
                [sys.executable, str(VALIDATOR), "--deck", str(deck_path),
                 "--manifest", str(manifest_path), "--routes", str(routes_path), *extra],
                text=True, capture_output=True, check=False,
            )

    def test_exports_browser_contract_as_json(self):
        deck = slide() + feature(
            "repeatable-stage",
            "Repeatable stage",
            "http://localhost:4200/demo/story?persona=maya",
            action="Reveal status.",
        )
        result = self.run_validator(deck, None, None, "--json")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), [{
            "id": "repeatable-stage", "title": "Repeatable stage",
            "url": "http://localhost:4200/demo/story?persona=maya",
            "action": "Reveal status.", "lookFor": "Ready.",
        }])

    def test_rejects_missing_feature_field(self):
        deck = feature().replace("<p><strong>Action:</strong> Go.</p>", "")
        result = self.run_validator(deck)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("presenter notes are missing Action", result.stderr)

    def test_rejects_each_blank_feature_field(self):
        decks = {
            "ArcadeDB": feature(arcade_db=""),
            "Activate demo": feature().replace(">Open</a>", "></a>"),
            "Action": feature(action=""),
            "Look for": feature(look_for=""),
        }
        for name, deck in decks.items():
            with self.subTest(field=name):
                result = self.run_validator(deck)
                self.assertNotEqual(result.returncode, 0)
                expected = (
                    "is missing ArcadeDB"
                    if name == "ArcadeDB"
                    else f"presenter notes are missing {name}"
                )
                self.assertIn(expected, result.stderr)

    def test_rejects_presenter_fields_outside_notes(self):
        deck = feature().replace('<aside class="notes">', "").replace("</aside>", "")
        result = self.run_validator(deck)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must have exactly one presenter notes block", result.stderr)
        self.assertIn("exposes presenter-only Action", result.stderr)

    def test_rejects_duplicate_ids_and_malformed_query(self):
        url = "http://localhost:4200/demo/story?persona=maya&amp;persona=priya"
        duplicate = feature("duplicate", "One", url)
        result = self.run_validator(duplicate + feature("duplicate", "Two", url))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("duplicate activation id", result.stderr)
        self.assertIn("duplicate query parameter", result.stderr)

    def test_rejects_route_only_matched_by_fallback(self):
        result = self.run_validator(
            feature(url="http://localhost:4200/demo/not-real"),
            {"routes": ["demo/story", "demo/not-real"]},
            "export const routes = [{ path: 'demo/story' }, { path: 'demo/:feature' }];",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("not a concrete Angular route", result.stderr)

    def test_rejects_manifest_drift_from_angular_routes(self):
        result = self.run_validator(
            feature(),
            {"routes": ["demo/story", "demo/ghost"]},
            "export const routes = [{ path: 'demo/story' }];",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("manifest route is not concrete", result.stderr)

    def test_rejects_concrete_angular_route_missing_from_manifest(self):
        result = self.run_validator(
            feature(),
            {"routes": ["demo/story"]},
            "export const routes = [{ path: 'demo/story' }, { path: 'demo/new-screen' }];",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("concrete Angular route is missing from manifest", result.stderr)

    def test_rejects_unclosed_section(self):
        result = self.run_validator(feature().removesuffix("</section>"))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("unclosed or nested section", result.stderr)

    def test_rejects_mismatched_demo_path(self):
        result = self.run_validator(feature(demo_path="/demo/recipes/wrong"))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("data-demo-path does not match href", result.stderr)

    def test_rejects_invalid_port_without_traceback(self):
        result = self.run_validator(feature(url="http://localhost:not-a-port/demo/story"))
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("malformed demo URL", result.stderr)
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
