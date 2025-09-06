# Thaum Project Overview

## Purpose
Thaum is a hierarchical compression engine for two breakthrough use cases:
1. **Codebase Optimization**: LSP-based hierarchical code analysis with 4-level compression system
2. **LLM Conversation Compression**: Advanced prompt engineering with superposed vector methodology

## Tech Stack
- **Language**: C# / .NET 9.0
- **UI**: Terminal.Gui v1.15.0 for TUI interface
- **LSP**: OmniSharp.Extensions.LanguageProtocol for multi-language support
- **HTTP**: HttpClient for LLM integration
- **Database**: SQLite via Microsoft.Data.Sqlite
- **Configuration**: Microsoft.Extensions.Configuration with JSON support
- **Logging**: Serilog with console, file, and Seq sinks
- **Testing**: xUnit with NSubstitute and FluentAssertions
- **CLI Parsing**: System.CommandLine (beta)

## Project Structure
- **CLI/**: Command-line interface implementations (partial class pattern)
- **Core/**: Core business logic (Compressor, HttpLLM, etc.)
- **TreeSitter/**: Tree-sitter integration for code parsing
- **TUI/**: Terminal.Gui interface
- **MCP/**: Model Context Protocol server
- **Tests/**: Unit tests
- **prompts/**: LLM prompt templates

## Key Features
- Multi-language LSP support (C#, Rust, Go, Python, TypeScript, JavaScript)
- Hierarchical symbol compression with K1/K2 key extraction
- 4-level compression system (Optimize → Compress → Golf → Endgame)
- Native AOT compilation support
- MCP server integration for AI tools
- Incremental processing with SQLite caching