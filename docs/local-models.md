# Local models

Phase 5 uses real local EmbeddingGemma inference for every stored and query vector. The former token-hash compatibility provider has been removed. Both the keyword and embedding indexes consume the same canonical public text; private tasting notes never enter that text.

Install [Ollama](https://ollama.com/download) so `ollama` is on `PATH`, then start the existing application:

```sh
aspire start --non-interactive
aspire wait embedding --non-interactive --timeout 600
aspire wait api --non-interactive --timeout 600
```

The AppHost starts `ollama serve` as the `local-models` executable resource on an Aspire-allocated loopback port. It sets `OLLAMA_MODELS` to this checkout's ignored `.aspire/models` directory and disables cloud features. It does not use or modify an existing system Ollama daemon. Aspire owns the process lifecycle; `aspire stop --non-interactive` stops the local services. Models remain cached for the next start.

The embedding service downloads the approximately 622 MB `embeddinggemma:300m` model on first startup, validates its manifest digest, and runs an actual 768-dimensional inference probe before reporting healthy. It then downloads the optional approximately 523 MB `qwen3:0.6b` query interpreter. Retrieval becomes ready before this second download; interpreter failures do not block keyword, semantic, hybrid, or graph recommendation retrieval. First startup requires access to Ollama's model registry. Subsequent starts use the cached models offline.

| Purpose | Model | Required manifest digest |
| --- | --- | --- |
| Embeddings | `embeddinggemma:300m` | `85462619ee721b466c5927d109d4cb765861907d5417b9109caebc4e614679f1` |
| Optional query interpretation | `qwen3:0.6b` | `7df6b6e09427a769808717c0a93cadc4ae99ed4eb8bf5ca557c90846becea435` |

A changed upstream tag does not silently change search semantics: readiness fails when the digest differs. Review and update the pin intentionally, then rebuild all indexed embeddings. Normal starts verify the installed digest without downloading an already matching model. `/api/models` on the embedding resource shows each model's readiness and initialization error. `/health` additionally checks that the embedding runtime is reachable and the pinned model remains installed. After fixing startup failures, use `aspire resource embedding restart --non-interactive` to retry initialization.

The embedding service supports:

- `POST /api/embed` with `{ "text": "bright fruity floral coffee", "purpose": "query" }` returns `embedding`, `dimensions`, `provider`, `model`, and `digest`.
- `POST /api/embed/batch` with `{ "texts": ["canonical public text"], "purpose": "document" }` accepts up to 64 texts and returns `embeddings` with the same metadata.
- `POST /api/interpret` with `{ "text": "I want a bright fruity floral coffee" }` returns only a bounded `query`, `model`, and `provider`.

Embedding input follows EmbeddingGemma's retrieval task prefixes: `task: search result | query: ...` for queries and `title: Coffee tasting | text: ...` for indexed documents. The service checks dimensionality, finite values, and normalization. It rejects oversized requests and context overflow rather than silently truncating canonical text. Models are kept warm to avoid repeated cold loads during seed batches.

The interpreter uses constrained JSON output, disabled thinking, temperature zero, a fixed seed, and a 128-token output budget. It rewrites a search query only; the API still validates the query and obtains every result and explanation from deterministic database retrieval. Generated text cannot supply bean IDs, recommendation scores, graph evidence, or SQL. Model rewriting is optional and cannot be treated as a factual answer.

The real model does not guarantee a hand-authored ranking for every keyword. For example, the single word `blueberry` can favor Blueberry Label Dark Roast. The richer query `bright fruity floral coffee` distinguishes Summer Orchard's berry nectar and blossom description from that dark, smoky keyword match. Keep golden checks tied to measured model behavior and the pinned model.

Run the read-only model verification after `aspire wait embedding`. Use the embedding URL shown by `aspire describe`; include `--interpret` after validating deterministic retrieval to check the optional helper too:

```sh
python3 scripts/verify-local-models.py --embedding-url http://localhost:5012 --interpret
```

The script verifies the model pin, finite normalized 768-dimensional vectors, repeated inference, batch output, the semantic contrast above, and rejected invalid requests. The optional helper check verifies a real generated query with a 300-character bound; it deliberately does not assert exact model wording. Cold interpreter inference may take longer than 30 seconds on CPU.

References: [EmbeddingGemma](https://ollama.com/library/embeddinggemma:300m), [Ollama embedding API](https://docs.ollama.com/api/embed), [EmbeddingGemma task prompts](https://ai.google.dev/gemma/docs/embeddinggemma), [Qwen3 0.6b](https://ollama.com/library/qwen3:0.6b), [Ollama generation API](https://docs.ollama.com/api/generate).
