# Rehearse the community graph

Start the application using the [README](../README.md), then reset to the story profile from the story screen. The [Reveal.js slide source](../src/coffee-community-slides/public/slides.html) contains the five Phase 3 checkpoints in presentation order.

## Walk through Maya's passport

1. Open `/demo/meet/maya-chen`. Scan `badge-0003` to meet Attendee 0003. Repeat the scan to demonstrate that the relationship is not duplicated.
2. Open `/demo/passport/maya-chen`. Log a tasting or love the default `blueberry-bloom-v60` brew. The passport brings together meetings, cups, reconnect intent, and games.
3. Open `/demo/games/maya-chen`. Follow Maya's Wingspan, Magic the Gathering, Bid Whist, and Pokemon TCG sessions. Each `PLAYED_IN` edge links the player's cup and tasting note; follow an opponent's cup into bean-to-cup provenance, then inspect the player/session/coffee query.
4. Open `/demo/network/maya-chen?target=luis-ortega`. Inspect rematch candidates: Luis beat Maya 2–1 in Magic the Gathering.
5. Find the shortest path to Luis: Maya Chen → Priya Nair → Luis Ortega. The badge scan in step 1 does not create a shortcut to Luis.
6. Inspect reconnect targets. Priya is Maya's authored target for a conversation about the blueberry recipe.

Use **Inspect queries** beside a section to see only the SQL or Cypher and parameters used for that section. After a write, a separate inspector beside its action retains the saved operation's statements. Rematch candidates and reconnect targets start collapsed; their **Show**/**Hide** controls reveal and hide those results. Shortest paths use a breadth-first traversal in the API over the actual `MET` edges read with SQL; the query label identifies that computation. Meetings are treated as connections in both directions.

## Try a new game

From the passport, start the default `demo-wingspan` session against Priya, then record the result. Repeating the same session or result is safe. A different participant list or conflicting result for an existing session is rejected; start a new session to record another game.

## Run verification

With Aspire running:

```bash
python3 scripts/verify-community-graph.py \
  --api-url http://localhost:4200 --reset
```

The `--reset` option replaces current data with the story seed first. The test writes meetings, tastings, reactions, reconnect intent, and a game result. Concurrent repeated actions must produce one logical relationship or result. The checks also cover invalid participants, conflicting results, missing records, and disconnected paths.

Restore the presentation seed after testing:

```bash
curl --fail-with-body -X POST 'http://localhost:4200/api/demo/reset?profile=story'
```

Demo personas are a local presentation mechanism, not production authentication. Phase 4 implements the recipe and provenance screens; Phase 5 implements ranked discovery.
