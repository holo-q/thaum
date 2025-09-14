# Repository Guidelines

## Project Structure & Module Organization
- `Thaum.App` — CLI/TUI host; entrypoint `Program.cs` and command wiring in partial `CLI` (e.g., `CLI_ls.cs`, `CLI_optimize.cs`).
- `Thaum.Core` — core models, services (crawler, LLM, compression), utilities (`Tracer`, `GLB`).
- `Thaum.Tests` — xUnit tests with FluentAssertions and NSubstitute.
- `TreeSitter` — grammar integration and native adapters.
- `reference/` — external subprojects used by the TUI (Ratatui.cs reload libs).
- `cache/` — runtime artifacts (sessions, evals). Not for commits.

## Build, Test, and Development Commands
- Build: `dotnet build Thaum.sln` — compile all projects.
- Tests: `dotnet test Thaum.sln` — run xUnit test suite.
- Run CLI: `dotnet run --project Thaum.App -- --help`
  - Example: `dotnet run --project Thaum.App -- ls --path . --lang auto`
- Publish (example): `dotnet publish Thaum.App -c Release -r linux-x64` — self-contained binary if desired.

## Coding Style & Naming Conventions
- C# 12, `net9.0`, 4-space indentation, `Nullable` enabled, implicit usings on.
- Embrace semantic minimalism (see `NAMESPACE-ENGINEERING.md`): prefer short, precise names (`CLI`, `trace`, `colorer`).
- Keep CLI command handlers in `CLI` partials; avoid monolithic files.
- Use `Logging.For<T>()` for loggers and `Tracer` helpers (`trace`, `tracein/traceout`).
- Avoid ceremony: favor clear data types, `var` for obvious locals, and small, composable methods.

## Testing Guidelines
- Frameworks: xUnit + FluentAssertions + NSubstitute.
- Naming: test classes mirror target type; methods use behavior style, e.g., `MethodName_Should_DoThing_When_Condition`.
- Run: `dotnet test` (add `-v n` for normal verbosity). Keep unit tests fast and deterministic.

## Commit & Pull Request Guidelines
- Commits: imperative, concise subject; scope small. Examples: `cli: add ls --split`, `core: fix crawler null slice`.
- PRs: include summary, motivation, and screenshots/logs if UX/CLI output changes. Link issues and note breaking changes.
- Keep diffs focused; update docs if behavior or commands change.

## Security & Configuration Tips
- Configuration via `.env` files; loader merges hierarchically. Do not commit secrets.
- LLM provider keys/models come from env (`GLB.AppConfig`); verify before running compression commands.
- Artifacts are written under `cache/` (`cache/sessions`, `cache/evals`); safe to delete locally.
