# GDPR Assistant — RAG over EU data-protection law

A **RAG (Retrieval-Augmented Generation)** project that answers questions about
the **GDPR** (EU General Data Protection Regulation, Regulation (EU) 2016/679),
**citing the articles** it relied on. Ask in plain language (e.g. *"do I need
consent to process personal data?"*) and get a grounded answer plus the relevant
articles. Built as a portfolio piece to demonstrate end-to-end RAG engineering on
a realistic, internationally-relevant domain.

## Stack

| Layer | Technology |
|---|---|
| Backend | C# / ASP.NET Core (.NET 10) |
| Vector store | Postgres + pgvector |
| DB access | Npgsql + Pgvector (.NET) |
| Embeddings | Ollama `bge-m3` (1024 dims, **multilingual**, local) |
| Generation (LLM) | Ollama `qwen2.5:3b` (local) — interface ready for **Claude** |
| Frontend | React + Vite + TypeScript + Tailwind |
| Infra | Docker Compose |

> **100% local and free** — no API key required. Everything runs on your machine
> via Ollama. Models can be swapped in `appsettings.json`. The embedding model is
> multilingual, so it also handles questions in other languages.

## Architecture

```
Browser → web (React/Nginx :8080) → api (ASP.NET Core :8000) ─┬─ Postgres+pgvector (:5432)
                                                              └─ Ollama (:11434)
                                                                 ├─ bge-m3 (embeddings, multilingual)
                                                                 └─ qwen2.5:3b (generation)
```

## Dataset (GDPR)

The corpus is the GDPR article text published on
[gdpr-info.eu](https://gdpr-info.eu). A reproducible seeder fetches all 99
articles, and writes one `.md` per article (each a self-contained, citable chunk)
under `docs-samples/gdpr/`:

```bash
node tools/seed-gdpr.mjs   # populates docs-samples/gdpr/ (99 articles)
# then index them:  POST http://localhost:8000/ingest
```

Each law article is a natural retrieval unit, so the answer can cite an exact
article (e.g. *Art. 17 — Right to erasure*).

> **Note on the data:** the generated `docs-samples/gdpr/` files are **not committed**
> (they are git-ignored) — run the seeder to recreate them locally. The GDPR text is
> sourced from [gdpr-info.eu](https://gdpr-info.eu) and belongs to its respective
> rights holders; it is used here only as a sample corpus for a non-commercial demo.

## Running it

Prerequisites: Docker Desktop.

1. Bring everything up:
   ```bash
   docker compose up --build
   ```
   > On the **first** run, Ollama downloads the models (~3 GB). This can take a
   > few minutes; the API only starts once the models are ready.
2. Open:
   - Frontend: http://localhost:8080
   - API health: http://localhost:8000/health
   - Postgres: `localhost:5432` (db/user/password: `docsrag`)

## API endpoints

| Method | Route | Purpose |
|---|---|---|
| GET  | `/health` | API status |
| GET  | `/health/db` | Postgres connectivity + chunk count |
| POST | `/ingest` | re-index the documents folder |
| POST | `/ingest/upload` | index an uploaded file |
| POST | `/ask` | full RAG: structured JSON answer + cited articles |
| POST | `/ask/stream` | full RAG, streamed token-by-token over SSE |
| POST | `/search` | retrieval only (debug, no LLM) |
| GET  | `/eval` | evaluation harness: retrieval metrics (+ optional faithfulness) |

`/ask` returns a structured JSON answer (`{ answer, cited_refs }`) which the
backend parses (with a fallback) and the frontend renders, showing the consulted
articles as cards.

`/ask/stream` (used by the UI) streams over **Server-Sent Events**: it sends the
retrieved articles first (a `citations` event, ~0.3s — the cards appear instantly),
then the answer as plain prose, one `token` event at a time, then `done`. This
hides the latency of a slow local model — the first content shows in well under a
second instead of after the full ~45s generation.

## Evaluation (`/eval`)

RAG is only as good as you can measure. `/eval` runs a small hand-labelled
**golden set** of GDPR questions (each paired with the article that *should* be
retrieved — see `EvalDataset.cs`) and reports:

| Metric | Meaning |
|---|---|
| **hit-rate** | fraction of questions whose expected article appeared in the top-k |
| **MRR** | mean reciprocal rank — rewards ranking the right article higher |
| **avg latency** | retrieval time per question (ms) |
| **faithfulness** | *(optional)* LLM-as-judge: is the generated answer grounded in the retrieved articles? |

```bash
# fast, retrieval only:
curl "http://localhost:8000/eval"
# with the LLM-as-judge (slow on CPU, ~1-2 min/question):
curl "http://localhost:8000/eval?judge=true&limit=3"
```

Sample run (top-k = 4, local stack): **hit-rate 1.0**, **MRR 1.0** — every golden
question retrieves its target article at rank 1 — at a few hundred ms per query;
the judged subset scored **faithfulness 1.0**. The metrics make regressions
visible: change the embedding model, chunk size, or top-k and the numbers move, so
you can tell an "improvement" actually improved things instead of guessing.

## Tests

Pure-logic unit tests (no DB, no Ollama — they run in CI) for chunking and the
retrieval metrics:

```bash
cd backend && dotnet test   # 17 tests
```

## Switching LLM provider (Strategy Pattern)

Generation is abstracted behind `IGenerationService`, with two implementations:
`OllamaGenerationService` (local, default) and `AnthropicGenerationService`
(Claude, production). The choice is configuration-only — **no other code changes**:

```bash
# Development (default): local Qwen, free
Rag__Provider=ollama

# Production: Claude
Rag__Provider=anthropic
ANTHROPIC_API_KEY=sk-ant-...
```

> Anthropic does not offer embeddings — in production, keep `bge-m3` local or
> switch to Voyage/OpenAI (changes the vector dimension → requires re-indexing).

## Engineering notes & trade-offs

- **Same-language retrieval is strong:** GDPR text and English questions match
  well (similarity ~0.70); the multilingual embedding model also supports asking
  in other languages.
- **Per-article chunking:** each law article is a natural, citable unit — the
  cleanest possible chunking strategy.
- **Retrieval vs. generation:** retrieval is cheap and local; high-quality,
  fully instruction-following generation is where a stronger model (Claude) shines.
- **Decoupling via an interface:** swapping Ollama ↔ Claude is configuration only.
- **Streaming hides latency:** with a slow local model, streaming tokens (SSE +
  `IAsyncEnumerable`) and showing the citations first makes the app feel instant —
  perceived latency drops from ~45s to <1s, with no faster model.
- **Measure, don't guess:** a tiny golden set + `/eval` turns "looks fine" into
  numbers (hit-rate, MRR, faithfulness) you can watch when you tweak chunking,
  top-k, or the model.
- **Interfaces for testability:** `IEmbeddingService` / `IGenerationService` let the
  pure logic (chunking, metrics) be unit-tested without a DB or a model running.

> ⚠️ This is general information generated by an AI over the law text, **not legal
> advice**.

## Future improvements (out of scope for v1)

- Hybrid search (vector + keyword) and re-ranking (more robust retrieval on
  near-duplicate articles, e.g. GDPR Art. 13 vs Art. 14)
- Authentication / multi-tenant, cloud deployment

## License

[MIT](LICENSE) © Gustavo Pereira. The GDPR article text is the property of its
source ([gdpr-info.eu](https://gdpr-info.eu)) and is not covered by this license.
