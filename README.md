# Thaum - LSP-Based Codebase Summarization Tool

Thaum is a .NET Core application that uses Language Server Protocol (LSP) integration to perform hierarchical codebase summarization with LLM-powered analysis. It features a Terminal.Gui interface and supports multiple programming languages through LSP servers.

## Features

- **Multi-Language Support**: Python, C#, JavaScript, TypeScript, Rust, Go, and more via LSP
- **Hierarchical Summarization**: Function → Class → Module → Directory level analysis
- **LLM Integration**: Supports OpenAI, Anthropic, and Ollama providers
- **Incremental Updates**: Smart change detection and cache invalidation
- **MCP Integration**: Model Context Protocol server for AI tool integration
- **Native AOT**: Compiles to single-file native executables
- **Cross-Platform**: Windows, Linux, macOS support

## Architecture

### Core Components

1. **LSP Integration**: Uses OmniSharp.Extensions.LanguageProtocol for multi-language support
2. **Summarization Engine**: Implements hierarchical summarization with key extraction
3. **LLM Provider**: Abstract provider supporting multiple AI services
4. **Caching System**: SQLite-based caching for performance
5. **Change Detection**: File system watching with dependency tracking
6. **Terminal UI**: Rich console interface using Terminal.Gui v2
7. **MCP Server**: Exposes tools via Model Context Protocol

### Summarization Process

1. **Symbol Extraction**: Extract functions, classes, modules via LSP
2. **Function Summarization**: Summarize deepest scope symbols first
3. **Key Extraction**: Extract common patterns (K1) from function summaries
4. **Class Summarization**: Summarize classes using K1 context
5. **Key Refinement**: Extract K2 from class summaries
6. **Re-summarization**: Update all summaries with K1+K2 context
7. **Hierarchy Building**: Construct nested symbol hierarchy

## Installation

### Prerequisites

- .NET 8.0 or later
- Language servers for target languages:
  - Python: `pip install python-lsp-server`
  - Rust: `rustup component add rust-analyzer`
  - Go: `go install golang.org/x/tools/gopls@latest`
  - TypeScript: `npm install -g typescript-language-server`

### Build from Source

```bash
git clone https://github.com/your-repo/thaum.git
cd thaum
dotnet restore
dotnet build
```

### Native AOT Compilation

```bash
dotnet publish -c Release -r linux-x64 --self-contained
# Creates single-file executable in bin/Release/net9.0/linux-x64/publish/
```

## Usage

### Interactive Mode

```bash
./thaum
```

Use the Terminal.Gui interface to:
- Open projects (Ctrl+O)
- Start summarization (Ctrl+S)
- Browse symbol hierarchies
- View summaries and extracted keys

### Configuration

Configure via `appsettings.json`:

```json
{
  "LLM": {
    "Provider": "openai|anthropic|ollama",
    "BaseUrl": "https://api.openai.com/v1",
    "ApiKey": "your-api-key",
    "DefaultModel": "gpt-4"
  },
  "Cache": {
    "Directory": "/path/to/cache"
  }
}
```

Or via environment variables:
```bash
export LLM__Provider=openai
export LLM__ApiKey=your-api-key
```

### MCP Integration

Start the MCP server for Claude Code integration:

```bash
./thaum --mcp-server
```

Register in Claude Code's MCP configuration:
```json
{
  "mcpServers": {
    "thaum": {
      "command": "/path/to/thaum",
      "args": ["--mcp-server"]
    }
  }
}
```

## MCP Tools

Thaum exposes these tools via Model Context Protocol:

### `summarize_codebase`
Analyze and summarize a codebase using hierarchical approach.

**Parameters:**
- `project_path` (string, required): Project root directory
- `language` (string, required): Programming language
- `force_refresh` (boolean): Force refresh cached summaries
- `max_depth` (number): Maximum hierarchy depth
- `llm_model` (string): LLM model override

### `search_symbols`
Search for symbols in a summarized codebase.

**Parameters:**
- `project_path` (string, required): Project root directory  
- `query` (string, required): Search query
- `symbol_kinds` (array): Filter by symbol types
- `max_results` (number): Result limit

### `get_symbol_summary`
Get detailed summary of a specific symbol.

**Parameters:**
- `project_path` (string, required): Project root directory
- `symbol_name` (string, required): Symbol name
- `file_path` (string, required): File containing symbol

### `get_extracted_keys`
Get the extracted summarization keys for a project.

**Parameters:**
- `project_path` (string, required): Project root directory

### `get_hierarchy` 
Get the complete symbol hierarchy for a project.

**Parameters:**
- `project_path` (string, required): Project root directory

### `invalidate_cache`
Invalidate cached summaries for a project.

**Parameters:**
- `project_path` (string, required): Project root directory
- `pattern` (string): Cache pattern to invalidate

## Key Bindings

- `F1` - Show help
- `Ctrl+Q` - Quit application  
- `Ctrl+O` - Open project
- `Ctrl+S` - Start summarization
- `Ctrl+R` - Refresh symbols
- `Tab` - Navigate between panels
- `Enter` - Select item/Execute action
- `Escape` - Go back/Cancel

## Language Support

| Language   | LSP Server | Status |
|------------|------------|---------|
| Python     | pylsp      | ✅ Full |
| C#         | csharp-ls  | ✅ Full |
| JavaScript | ts-server  | ✅ Full |
| TypeScript | ts-server  | ✅ Full |
| Rust       | rust-analyzer | ✅ Full |
| Go         | gopls      | ✅ Full |
| Java       | jdtls      | 🚧 Planned |
| C++        | clangd     | 🚧 Planned |

## Performance

- **Native AOT**: Fast startup, low memory usage
- **Incremental Processing**: Only re-summarize changed symbols  
- **SQLite Caching**: Persistent caching with expiration
- **Parallel Processing**: Concurrent summarization of symbols
- **Smart Invalidation**: Dependency-aware cache invalidation

## Contributing

1. Fork the repository
2. Create a feature branch
3. Implement changes with tests
4. Submit a pull request

## License

MIT License - see LICENSE file for details.

## Roadmap

- [ ] Additional language support (Java, C++)
- [ ] Web interface alongside Terminal.Gui
- [ ] Prompt optimization with self-play
- [ ] Distributed processing for large codebases
- [ ] Integration with more AI providers
- [ ] Visual dependency graphs
- [ ] Export formats (Markdown, HTML, JSON)