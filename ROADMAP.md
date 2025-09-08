# ROADMAP — Beeline To First Measurements

Focus: Quantify the current compression pipeline (lossless-first). Defer prompt optimization and advanced transforms until we have baseline metrics.

## Goals
- Produce reliable, repeatable measurements for representation-preserving compression (R0).
- Validate triad fidelity with objective gates (no human-in-the-loop).
- Generate per-symbol and aggregate reports (CSV/JSON) for quick iteration.

## In-Scope (Now)
- Triads: TOPOLOGY/MORPHISM/POLICY/MANIFEST, persisted per symbol.
- Gates (minimum):
  - Signature match (name/params/return) via TreeSitter (C# first; fallback heuristics for others).
  - AST-backed counts: await/calls/branches (C# first via TreeSitter; fallback regex).
  - Simple CFG overlap proxy (block/edge counts or ratios).
- Batch evaluation:
  - `eval-compression` CLI to run across a folder and emit CSV/JSON report.
  - Metrics: triad completeness, gates pass/fail, compression ratio (tokens saved per preserved fact), latency (if online), variance across runs.
- Artifacts:
  - Persist prompt/response/triad and per-symbol FidelityReport under `cache/sessions/<ts>/`.

## Out-of-Scope (Defer)
- Prompt optimization (PromptLab, variants v6+).
- Refactor/normalization/semantic optimizations (R1–R3).
- Full provider router and local model wiring (keep simple for now).

## Datasets
- A: Internal codebase slices (C# first).
- B: 50–200 function corpus per language (add JS/TS, Python, Go, Rust incrementally).

## Baselines
- Current prompt (compress_function_v5) as baseline.
- Optional: minimal-constraint variant as lower-bound comparator (later).

## Success Criteria (Initial Targets)
- ≥ 95% triad completeness across evaluated functions.
- ≥ 90% pass rate on signature + AST-backed counts gates (C#).
- Report generation time suitable for iterative dev (minutes, not hours).

## Risks & Mitigations
- Throttling / latency: prefer `--dry` and cached outputs; run online selectively.
- Model variance: fix seeds where supported; cache outputs by (hash, model).
- Language differences: ship C# first, add profiles incrementally.

## Immediate Tasks
- [ ] Add `eval-compression` CLI (directory batch → CSV/JSON report).
- [ ] Strengthen signature gate (compare extracted vs CodeSymbol where available).
- [ ] Add simple CFG overlap proxy (block/edge counts from TreeSitter, C# first).
- [ ] Persist per-symbol FidelityReport alongside triad artifacts.
- [ ] Add language profile scaffolding; map TreeSitter nodes per language.

Once these are green and we have a baseline report, revisit PromptLab and optimization passes with data in hand.
