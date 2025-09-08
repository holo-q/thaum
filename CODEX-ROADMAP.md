# Codex Roadmap

High‑leverage, one‑shot upgrades that expand capability, improve iteration speed, and drive toward maximal hierarchical compression power across code and conversations.

## Core Engine
- **AST Extraction++:** Add Roslyn fallback for C# and parity checks vs TreeSitter (spans/signatures).
- **Range Fidelity:** Normalize 0/1-based positions, clamp, and add unit tests for span correctness.
- **Incremental Index:** Persist file→symbol hashes; only reparse changed files; support `--changed-since`.
- **Language Map:** Add Java/C++/Swift grammars with dynamic load and graceful degradation.
- **Hybrid Crawl:** Combine LSP (types/refs) with TreeSitter (speed); deterministic graph merge.

## Compression Pipeline
- **Prompt Profiles:** Distinct Optimize/Compress/Golf/Endgame profiles tied to specific prompts/seeds.
- **Triad Outputs:** Standardize TOPOLOGY/MORPHISM/POLICY/KEYS; store JSON+text per symbol.
- **Rehydration:** Expand-from-triad prompts to reconstruct code/spec; compare via AST+signatures.
- **Key Injection:** Auto‑reuse K1/K2 bottom→up (function→class→module); measure fidelity lift.
- **Streaming Fusion:** Parallel rollouts; MinHash dedupe before fusion; stream merge.

## Evaluation & Fidelity
- **AST Gate:** Symbol set equality, signature diff, control‑flow skeleton similarity score.
- **Reconstruction Score:** ROUGE/BERTScore/AST overlap with per‑level thresholds.
- **Golden Sets:** Curated multi‑language corpus with expected behaviors and tricky patterns.
- **Regression Runs:** Nightly eval; fail on fidelity regression > epsilon; emit reports.
- **Diff Viewer:** CLI side‑by‑side original vs rehydrated with syntax highlighting.

## Memory & Keys
- **Key Store:** Persist K1/K2 as text + embeddings (SQLite/duckdb); index by repo/namespace/symbol.
- **Key Retrieval:** Nearest‑neighbor + tag filters for context injection; A/B strategies.
- **Key Compaction:** Periodic merge/rewrite to stabilize vocabulary; log survivor keys.
- **Cross‑Domain Keys:** Share keys between code and conversation compression; measure gains.

## LLM Providers & Routing
- **Provider Matrix:** Configure OpenAI/Anthropic/Kimi/Local with model aliases.
- **Cost Router:** Route Optimize→cheaper, Endgame→best; support `--force-model`.
- **Fallback Chain:** Retry with down‑level profile/model on errors; strict timeouts.
- **Batching:** Concurrent dispatch with per‑provider QPS and jitter.

## CLI/TUI UX
- **Try Dry‑Run:** `--dry-run` prints constructed prompt (no network); great offline.
- **Live Watch:** `try --interactive --watch` reloads on symbol or prompt change; in‑TUI diff.
- **Quick Targets:** `try --pick` fuzzy symbol picker; names/kinds filtering.
- **Output Capture:** Persist prompts/outputs under `cache/sessions/<ts>/`.
- **Colorized Logs:** Clear frames for TOPOLOGY/MORPHISM/POLICY/KEYS and fusion steps.

## Performance & Caching
- **Parallel DOP:** Respect `THAUM_TREESITTER_DOP`; add `--dop N`; pilot auto‑tune.
- **I/O Pooling:** Async bounded file reads; memory map large files where helpful.
- **Artifact Cache:** Cache triads by (file hash, level, prompt, model); `--force-refresh` escape.
- **Warm Index:** Background pre‑scan with live counts and ETA.
- **Skip Heavy:** Default incremental builds; `--rebuild` for full; avoid rebuilding `Terminal.Gui` unless changed.

## Observability & Analytics
- **Metrics:** Time per stage, tokens saved/used, compression ratio, fidelity scores (CSV/JSON).
- **Trace IDs:** Correlate prompt→output→fusion→rehydration for reproducibility.
- **Log Facets:** Operator view (concise) and researcher view (full streams).
- **Failure Taxonomy:** Classify prompt overflow/parse fail/timeouts; targeted fixes.

## Prompts & Methodology
- **Seed Library:** Versioned prompts with schema and changelog; prompt formatting tests.
- **Param Injection:** Tunable temps/token budgets/seeds per level.
- **Self‑Check:** Model validates triad completeness/consistency; re‑roll partials.
- **Anti‑Collapse:** Paraphrase/negative guidance to avoid degenerate compressions.
- **Prompt Lint:** Validate placeholders/forbidden structure; CI gate.

## Extensibility
- **Plugin Hooks:** Pre/post compression hooks (linters, domain adapters).
- **Tool API:** Expose compression as MCP tools with typed schemas.
- **Workflows:** YAML mini‑DSL for pipelines (crawl→compress→fuse→eval).

## Safety & Guardrails
- **Content Filters:** Redact secrets in prompts/outputs; hash replacement policy.
- **Token Budgeting:** Enforce stage caps; actionable errors on exceed.
- **Model Specifier:** Document model implications per level; warn on underpowered choices.

## Developer Velocity
- **Watch Mode:** `./run.sh watch` auto‑rebuilds & re‑runs last command.
- **Fast Tests:** Span extraction, prompt formatting, AST gates.
- **One‑Liners:** `./run.sh eval --path . --models kimi-2,gpt-4o --level endgame` reports.
- **Doc Snippets:** “Try a function,” “Tune compression,” “Add a model,” “Read fidelity report.”

## Offline/Edge
- **Local Model:** Ollama fallback with tuned prompts; clear fidelity expectations.
- **Cache‑Only Mode:** `--offline` uses dry‑run + cached outputs.

## Immediate Start (1–2 Sessions)
- **Dry‑Run:** Add `--dry-run` to `try` and persist artifacts per session.
- **AST Gate:** Implement symbol/fidelity checks and basic reconstruction test.
- **Endgame Wiring:** Bind Endgame to `compress_function_v5` with structured triad capture.
- **Evaluator CLI:** `eval-compression` to batch a folder and emit a report.
- **Keys:** Persist K1/K2 (text+embedding) and simple retrieval for second‑pass compression.

