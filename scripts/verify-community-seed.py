#!/usr/bin/env python3
"""Integration assertions against the local API/ArcadeDB; --reset checks each profile twice."""

import argparse
import base64
import json
import os
import urllib.request

p = argparse.ArgumentParser()
p.add_argument("--db-url", required=True)
p.add_argument("--api-url")
p.add_argument("--reset", action="store_true")
p.add_argument("--profile", choices=["story", "scale"], default="story")
a = p.parse_args()
auth = base64.b64encode(
    ("root:" + os.getenv("ARCADEDB_PASSWORD", "CoffeeDemo_Local_2026!")).encode()
).decode()


def post(url, payload, db=False):
    req = urllib.request.Request(
        url,
        json.dumps(payload).encode(),
        {
            "Content-Type": "application/json",
            **({"Authorization": "Basic " + auth} if db else {}),
        },
    )
    with urllib.request.urlopen(req, timeout=1800) as r:
        return json.load(r)


def query(sql):
    return post(
        a.db_url + "/api/v1/query/coffee_demo",
        {"language": "sql", "command": sql, "limit": 30000},
        True,
    )["result"]


def verify():
    types = {x["name"] for x in query("SELECT FROM schema:types")}
    required = "Person Organization Event VenueArea VendorTable CoffeeLot RoastBatch Recipe Brew Game GameSession ATTENDED MET WANTS_TO_RECONNECT MEMBER_OF ROASTED SELLS FROM_LOT BREWED USED_BATCH USED_RECIPE TASTED LOVED LIKED_RECIPE PLAYED_IN BEAT_IN_GAME RecipeRevision Note RoastProfile EventConfiguration BadgeLookup SearchEmbedding BrewTelemetry EventActivity".split()
    assert set(required) <= types, f"Missing types: {set(required) - types}"
    expected = {
        "Person": 40,
        "VendorTable": 8,
        "RoastBatch": 25,
        "Recipe": 20,
        "Brew": 100,
        "SearchEmbedding": 45,
    }
    if a.profile == "scale":
        expected.update(Person=2040, RoastBatch=2025, SearchEmbedding=20045)
    counts = {t: query(f"SELECT count(*) AS n FROM {t}")[0]["n"] for t in expected}
    assert counts == expected, (counts, expected)
    for t in expected:
        rows = query(f"SELECT slug, count(*) AS n FROM {t} GROUP BY slug LIMIT 30000")
        assert all((r["n"] == 1 for r in rows)), t
    assert (
        query("SELECT name FROM Person WHERE slug='maya-chen'")[0]["name"]
        == "Maya Chen"
    )
    assert (
        query("SELECT expand(out('MET')) FROM Person WHERE slug='maya-chen'")[0]["name"]
        == "Priya Nair"
    )
    assert {
        row["name"]
        for row in query("SELECT expand(in('BEAT_IN_GAME')) FROM Person WHERE slug='maya-chen'")
    } == {"Luis Ortega", "Priya Nair"}
    assert query(
        "SELECT currentRevision.steps AS steps FROM Recipe WHERE slug='blueberry-v60'"
    )[0]["steps"]
    assert (
        query(
            "SELECT target.slug AS slug FROM BadgeLookup WHERE slug='short-blueberry'"
        )[0]["slug"]
        == "ethiopia-blueberry-bloom"
    )
    assert (
        query(
            "SELECT expand(out('USED_BATCH')) FROM Brew WHERE slug='blueberry-bloom-v60'"
        )[0]["slug"]
        == "ethiopia-blueberry-bloom"
    )
    assert (
        query(
            "SELECT expand(out('FROM_LOT')) FROM RoastBatch WHERE slug='ethiopia-blueberry-bloom'"
        )[0]["slug"]
        == "lot-0000"
    )
    assert (
        query(
            "SELECT expand(in('ROASTED')) FROM RoastBatch WHERE slug='ethiopia-blueberry-bloom'"
        )[0]["name"]
        == "Great Lakes Roasters"
    )
    assert (
        query(
            "SELECT revision.slug AS slug FROM USED_RECIPE WHERE @out.slug='blueberry-bloom-v60'"
        )[0]["slug"]
        == "blueberry-v60-v2"
    )
    games = {row["name"] for row in query("SELECT name FROM Game")}
    assert games == {"Wingspan", "Magic the Gathering", "Monopoly", "Bid Whist", "Pokemon TCG"}
    assert len(query("SELECT FROM GameSession")) == 5
    assert all(row["score"] > row["opponentScore"] for row in query("SELECT score, opponentScore FROM BEAT_IN_GAME"))
    game_drinks = query("SELECT drink.slug AS brewSlug, drinkNote FROM PLAYED_IN")
    assert len(game_drinks) == 10 and all(row.get("brewSlug") and row.get("drinkNote") for row in game_drinks)
    assert (
        query(
            "SELECT expand(out('LOVED').out('USED_BATCH')) FROM Person WHERE slug='priya-nair'"
        )[0]["slug"]
        == "roast-batch-0003"
    )
    index_types = {x["indexType"] for x in query("SELECT FROM schema:indexes")}
    assert any(("VECTOR" in x for x in index_types)), index_types
    config = query("SELECT FROM EventConfiguration WHERE slug='brew-connection-2026'")[
        0
    ]
    assert config["seedComplete"] and config["seedProfile"] == a.profile, config
    assert config["telemetrySamples"] == (2000000 if a.profile == "scale" else 6000)
    assert {
        "HASH",
        "LSM_TREE",
        "FULL_TEXT",
        "LSM_VECTOR",
        "GEOSPATIAL",
    } <= index_types, index_types
    telemetry = post(
        a.db_url + "/api/v1/ts/coffee_demo/query",
        {
            "type": "BrewTelemetry",
            "from": 1789401600000,
            "to": 1789501600000,
            "aggregation": {
                "bucketInterval": 100000000,
                "requests": [
                    {"field": "water_grams", "type": "COUNT", "alias": "samples"}
                ],
            },
        },
        True,
    )
    actual = sum((b["values"][0] for b in telemetry["buckets"]))
    assert actual == config["telemetrySamples"], actual
    spike = query(
        "SELECT flow_rate FROM BrewTelemetry WHERE brew_id='blueberry-bloom-v60' AND flow_rate > 10"
    )
    assert len(spike) == 1 and spike[0]["flow_rate"] == 12, spike
    keyword = query(
        "SELECT slug FROM RoastBatch WHERE SEARCH_INDEX('RoastBatch[searchText]', 'blueberry') = true"
    )
    assert {r["slug"] for r in keyword} == {
        "ethiopia-blueberry-bloom",
        "roast-batch-0001",
    }, keyword
    orchard = query("SELECT searchText FROM RoastBatch WHERE slug='roast-batch-0002'")[
        0
    ]["searchText"]
    assert "blueberry" not in orchard.lower() and "berry nectar" in orchard.lower(), (
        orchard
    )
    for statement, marker in [
        ("SELECT FROM Person WHERE slug='maya-chen'", "INDEX"),
        (
            "SELECT FROM RoastBatch WHERE SEARCH_INDEX('RoastBatch[searchText]', 'blueberry') = true",
            "BM25",
        ),
    ]:
        plan = post(
            a.db_url + "/api/v1/query/coffee_demo",
            {"language": "sql", "command": "EXPLAIN " + statement},
            True,
        )
        assert marker in json.dumps(plan), plan
    vectors = query(
        "SELECT vector.neighbors('SearchEmbedding[embedding]', embedding, 1) AS neighbors FROM SearchEmbedding WHERE slug='embedding-00000'"
    )
    assert (
        vectors[0]["neighbors"][0]["record"]["subjectSlug"]
        == "ethiopia-blueberry-bloom"
    ), vectors
    geo = query(
        "SELECT slug, geo.distance(coords, geo.geomFromText('POINT(-83.0458 42.3314)'), 'm') AS meters FROM VendorTable ORDER BY meters"
    )
    assert geo[0]["slug"] == "vendor-0" and geo[0]["meters"] == 0, geo
    fingerprint = {
        t: [r["slug"] for r in query(f"SELECT slug FROM {t} ORDER BY slug LIMIT 30000")]
        for t in expected
    }
    print("PASS", a.profile, counts, "actual telemetry samples", actual, flush=True)
    return fingerprint


previous = None
for i in range(2 if a.reset else 1):
    if a.reset:
        assert a.api_url, "--api-url required with --reset"
        post(a.api_url + "/api/demo/reset?profile=" + a.profile, {})
    current = verify()
    if previous is not None:
        assert current == previous, "Slugs changed between resets"
    previous = current
