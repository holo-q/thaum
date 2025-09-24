# Repository Guidelines

This document is a concise contributor guide for the Thaum repository.

## Project Structure & Module Organization
- `Thaum.App` — CLI/TUI host; entrypoint `Program.cs`, commands in partials (`CLI_*.cs`).
- `Thaum.Core` — core models/services (crawler, LLM, compression), utilities (`Tracer`, `GLB`).
- `Ratatui.Reload` / `Ratatui.Reload.Abstractions` — Ratatui.cs live‑reload host and interfaces.
- `Thaum.TUI` — sample plugin implementing `IReloadableApp` for hot reload.
- `Thaum.Tests` — xUnit tests with FluentAssertions and NSubstitute.
- `TreeSitter/` — grammar integration and native adapters; `reference/` — external subprojects.
- `cache/` — runtime artifacts (sessions, evals). Do not commit.
* `docs/ratatui-ffi` — for reference only, do not change.
* `Ratatui.cs` — the actual Ratatui.cs bindings used for our TUI.

## Build, Test, and Development Commands
- Build all: `dotnet build Thaum.sln`
- Test all: `dotnet test Thaum.sln` (add `-v n` for normal verbosity)
- Run CLI: `dotnet run --project Thaum.App -- --help`
  - Example: `dotnet run --project Thaum.App -- ls --path . --lang auto`
- TUI hot‑reload host: `dotnet run --project Thaum.App -- tui-watch [-p <proj>|--plugin <proj>] [--manual --hint --key r]`
  - Default plugin: `Thaum.TUI/Thaum.TUI.csproj`.

## Coding Style & Naming Conventions
- C# 12, `net9.0`, tab indentation, `Nullable` enabled, implicit usings on.
- Prefer semantic minimalism: short, precise names (`CLI`, `trace`, `colorer`).
- Keep CLI handlers in `CLI` partials; avoid monolithic files.
- Use `Logging.For<T>()` and `Tracer` helpers (`trace`, `tracein/traceout`).
- Curly braces on same line

### Symbolic & Topological Naming
Use naming patterns where the visual topology of code mirrors its semantic topology:

**Type-prefix notation** (Hungarian-style):
- `rButton` over `buttonRect`, `wSelected` over `selectedWidth`, `nItems` over `itemCount`
- Prefixes act as visual type signatures, creating columnar alignment and immediate grouping
- All variables of the same type/category start with the same symbol, forming visual clusters

**Paired/symmetric concepts** use visually symmetric names:
- `l/r` (left/right), `src/dst` (source/destination), `in/out`, `up/dn`, `old/new`
- Short, symmetric notation emphasizes the relationship's symmetry
- Creates visual balance when variables appear together in code

**Visual clustering principle**:
```csharp
var rButton = GetButtonRect();
var rLabel = GetLabelRect();
var rFrame = GetFrameRect();
// All 'r*' variables visually group, making rectangle operations obvious
```

**Rationale**: Code should be "euclidean" — spatial relationships in the text directly correspond to logical relationships in the program. The visual pattern reveals the semantic pattern.

## Testing Guidelines
- Frameworks: xUnit + FluentAssertions + NSubstitute.
- Naming: test classes mirror target type; methods use behavior style, e.g., `Method_Should_DoThing_When_Condition`.
- Run: `dotnet test`; keep tests fast, deterministic, and focused.

## Commit & Pull Request Guidelines
- Commits: small, focused, imperative subject. Examples:
  - `cli: add ls --split`
  - `core: fix crawler null slice`
- PRs: include summary, motivation, and relevant logs/screenshots for CLI/TUI output. Link issues and note breaking changes.

## Security & Configuration Tips
- Configuration via `.env` files (hierarchical loader). Never commit secrets.
- LLM keys/models via env (`GLB.AppConfig`); verify before running compression commands.
- Artifacts are written under `cache/` (`cache/sessions`, `cache/evals`); safe to delete locally.

# Additional Work Directives

IMPORTANT:

- Curly braces on same line
