# Rehearse recipes, notes, and provenance

Start Aspire using the [README](../README.md) and restore the story seed before the walkthrough. The [Reveal.js slide source](../src/coffee-community-slides/public/slides.html) contains the four Phase 4 checkpoints.

## Explore and publish a recipe

Open `/demo/recipes/blueberry-v60`. The seeded recipe starts at revision 2. Inspect the nested pour steps, equipment, grind, temperature, and commentary, then compare the earlier revision.

Change the grind setting and publish a revision. Publication creates a new `RecipeRevision` document and changes the recipe's current-revision link in one transaction. Previous revisions remain available for comparison. Conflicting publication attempts are rejected so a stale editor cannot replace a newer revision.

Use the recipe creation form to start a separate recipe with its own stable slug and first revision.

## Attach a memory

Notes are plain text and link to a person, brew, roast batch, recipe, or game session. Select a demo persona, choose the subject, enter a memory, and choose private or public visibility.

Private notes appear only for their owner's selected demo persona; public notes can appear for other personas. Switching personas clears displayed note data and retained query details before loading the next view. Demo personas model ownership for this local demonstration; they are not production authentication.

## Follow the coffee's history

Open `/demo/coffee/ethiopia-blueberry-bloom`. Select connected nodes to inspect the origin lot, roaster, vendor, brew, brewer, recipe, revision, and tasting reaction.

The authored chain includes Ethiopia Guji Lot 17, Great Lakes Roasters, Great Lakes Coffee Table, Priya's Blueberry Bloom V60, and Maya's reaction. The brew points to revision 2 even after the recipe advances to revision 3. Graph vertices keep flexible properties, while recipe revisions and roast profiles store nested document content.

## Verify the document workflow

With Aspire running:

```bash
python3 scripts/verify-community-documents.py \
  --api-url http://localhost:4200 --reset
```

This replaces the demo data with the story seed, then creates recipes, revisions, and notes. It checks revision immutability, concurrent publication, invalid/conflicting requests, owner and subject filtering, and the historical brew revision.

Restore the story before presenting again:

```bash
curl --fail-with-body -X POST 'http://localhost:4200/api/demo/reset?profile=story'
```

Search ranking and embedding updates are Phase 5 work.
