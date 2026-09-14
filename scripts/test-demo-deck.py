#!/usr/bin/env python3
"""Regression tests for the checked-in demo deck validator."""

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
VALIDATOR = ROOT / "scripts" / "validate-demo-deck.py"


class DemoDeckValidatorTests(unittest.TestCase):
    def run_validator(self, deck: str, manifest=None, routes=None, *extra: str):
        manifest = manifest or {"routes": ["demo/story", "demo/recipes/:recipeSlug"]}
        routes = routes or "export const routes = [{ path: 'demo/story' }, { path: 'demo/recipes/:recipeSlug' }, { path: 'demo/:feature' }];"
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            deck_path = root / "deck.md"
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
        result = self.run_validator(
            "<!-- slide: title -->\n# Deck\n\n---\n\n<!-- demo: repeatable-stage -->\n## Repeatable stage\n"
            "**ArcadeDB:** Graph\n\n**Activate demo:** [Open](http://localhost:4200/demo/story?persona=maya)\n\n"
            "**Action:** Reveal status.\n\n**Look for:** Ready.\n",
            None, None, "--json",
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(json.loads(result.stdout), [{
            "id": "repeatable-stage", "title": "Repeatable stage",
            "url": "http://localhost:4200/demo/story?persona=maya",
            "action": "Reveal status.", "lookFor": "Ready.",
        }])

    def test_rejects_missing_feature_field(self):
        result = self.run_validator(
            "# Deck\n\n---\n\n<!-- demo: missing-action -->\n## Missing action\n"
            "**ArcadeDB:** Graph\n\n**Activate demo:** [Open](http://localhost:4200/demo/story)\n\n"
            "**Look for:** Ready.\n"
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("missing Action", result.stderr)

    def test_rejects_each_blank_feature_field(self):
        fields = {
            "ArcadeDB": "Graph",
            "Activate demo": "[Open](http://localhost:4200/demo/story)",
            "Action": "Go.",
            "Look for": "Ready.",
        }
        for blank_name in fields:
            with self.subTest(field=blank_name):
                lines = ["<!-- demo: blank-field -->", "## Blank field"]
                lines.extend(f"**{name}:** {'' if name == blank_name else value}" for name, value in fields.items())
                result = self.run_validator("\n".join(lines) + "\n")
                self.assertNotEqual(result.returncode, 0)
                self.assertIn(f"missing {blank_name}", result.stderr)

    def test_rejects_duplicate_ids_and_malformed_query(self):
        slide = ("<!-- demo: duplicate -->\n## {title}\n**ArcadeDB:** Graph\n"
                 "**Activate demo:** [Open](http://localhost:4200/demo/story?persona=maya&persona=priya)\n"
                 "**Action:** Go.\n**Look for:** Ready.\n")
        result = self.run_validator(f"# Deck\n\n---\n\n{slide.format(title='One')}\n---\n\n{slide.format(title='Two')}")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("duplicate activation id", result.stderr)
        self.assertIn("duplicate query parameter", result.stderr)

    def test_rejects_route_only_matched_by_fallback(self):
        result = self.run_validator(
            "# Deck\n\n---\n\n<!-- demo: fake -->\n## Fake\n**ArcadeDB:** Graph\n"
            "**Activate demo:** [Open](http://localhost:4200/demo/not-real)\n"
            "**Action:** Go.\n**Look for:** Ready.\n",
            {"routes": ["demo/story", "demo/not-real"]},
            "export const routes = [{ path: 'demo/story' }, { path: 'demo/:feature' }];",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("not a concrete Angular route", result.stderr)

    def test_rejects_manifest_drift_from_angular_routes(self):
        result = self.run_validator(
            "# Deck\n\n---\n\n<!-- demo: stage -->\n## Stage\n**ArcadeDB:** Graph\n"
            "**Activate demo:** [Open](http://localhost:4200/demo/story)\n"
            "**Action:** Go.\n**Look for:** Ready.\n",
            {"routes": ["demo/story", "demo/ghost"]},
            "export const routes = [{ path: 'demo/story' }];",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("manifest route is not concrete", result.stderr)

    def test_rejects_concrete_angular_route_missing_from_manifest(self):
        result = self.run_validator(
            "# Deck\n\n---\n\n<!-- demo: stage -->\n## Stage\n**ArcadeDB:** Graph\n"
            "**Activate demo:** [Open](http://localhost:4200/demo/story)\n"
            "**Action:** Go.\n**Look for:** Ready.\n",
            {"routes": ["demo/story"]},
            "export const routes = [{ path: 'demo/story' }, { path: 'demo/new-screen' }];",
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("concrete Angular route is missing from manifest", result.stderr)

    def test_rejects_malformed_separator(self):
        result = self.run_validator(
            "<!-- slide: title -->\n# Deck\n\n --- \n\n<!-- demo: stage -->\n## Stage\n"
            "**ArcadeDB:** Graph\n**Activate demo:** [Open](http://localhost:4200/demo/story)\n"
            "**Action:** Go.\n**Look for:** Ready.\n"
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("malformed slide separator", result.stderr)

    def test_rejects_invalid_port_without_traceback(self):
        result = self.run_validator(
            "<!-- demo: bad-url -->\n## Bad URL\n**ArcadeDB:** Graph\n"
            "**Activate demo:** [Open](http://localhost:not-a-port/demo/story)\n"
            "**Action:** Go.\n**Look for:** Ready.\n"
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("malformed demo URL", result.stderr)
        self.assertNotIn("Traceback", result.stderr)


if __name__ == "__main__":
    unittest.main()
