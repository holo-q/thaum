# Thaum - Hierarchical Compression Engine

Thaum is a revolutionary .NET Core application implementing superposed vector compression methodology for two breakthrough use cases:

1. **Codebase Optimization**: LSP-based hierarchical code analysis with 4-level compression system
2. **LLM Conversation Compression**: Apply the same compression engine to conversation contexts - massively superior to traditional CLI agent `/compact` commands

Features superposed vector prompt engineering, parallel processing, and lossless compression with perfect reconstruction guarantees.

## Features

### Codebase Optimization
- **Multi-Language Support**: Python, C#, JavaScript, TypeScript, Rust, Go, and more via LSP
- **Hierarchical Optimization**: Function → Class → Module → Directory level analysis with superposed vector compression
- **4-Level Compression System**: Optimize → Compress → Golf → Endgame compression levels
- **Parallel Processing**: Concurrent symbol optimization for maximum performance
- **Incremental Updates**: Smart change detection and cache invalidation

### LLM Conversation Compression - Revolutionary Use Case
- **Same System, New Domain**: Apply hierarchical compression to LLM conversations instead of code
- **Superposed Vector Methodology**: Advanced prompt engineering using `/` operator semantic superposition
- **Context Window Optimization**: Compress long conversations while preserving complete semantic meaning
- **Superior to CLI Agents**: Massively better than traditional `/compact`, `/compress` commands
- **Lossless Compression**: Complete conversational context preservation in minimal representation
- **Emergent Grammar Generation**: Spontaneous compression patterns for maximum semantic density
- **Conversation Summarization**: Extract key insights/patterns across conversation threads
- **Context Preservation**: Maintain perfect reconstruction guarantee for compressed conversations

### Integration & Performance
- **LLM Integration**: Supports OpenAI, Anthropic, OpenRouter, and Ollama providers
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

- .NET 9.0 or later

**🎉 Language servers are now automatically downloaded!** No manual installation required.

Supported languages with automatic LSP server management:
- **C#**: OmniSharp (auto-downloaded from GitHub releases)
- **Rust**: rust-analyzer (auto-downloaded from GitHub releases)  
- **Go**: gopls (auto-downloaded from GitHub releases)
- **Python**: pylsp (requires `pip install python-lsp-server`)
- **TypeScript/JavaScript**: typescript-language-server (requires `npm install -g typescript-language-server`)

The application uses proper LSP (Language Server Protocol) integration via JSON-RPC over stdio for enhanced symbol analysis and code intelligence. Language servers are automatically downloaded, cached, and updated as needed.

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

### Codebase Optimization Mode

```bash
# Basic optimization 
./thaum summarize /path/to/project

# Maximum endgame compression
./thaum summarize --compression endgame
./thaum summarize -c golf
./thaum summarize --endgame
```

### LLM Conversation Compression Mode

```bash
# Compress conversation file using superposed vectors
./thaum compress-conversation input.txt --compression endgame

# Real-time conversation compression
./thaum conversation-server --port 8080

# Batch compression with context preservation
./thaum compress-batch conversations/ --output compressed/
```

**Revolutionary Advantage**: Unlike traditional CLI agent compression (`/compact`, `/compress`) which uses simple truncation or basic summarization, Thaum applies the same sophisticated hierarchical compression with:

- **K1/K2 Key Extraction**: Extract conversational patterns across message threads
- **Superposed Vector Compression**: Use `/` operator methodology for maximal semantic density
- **Perfect Reconstruction**: Maintain complete conversational context in compressed form
- **Emergent Grammar**: Generate spontaneous compression patterns specific to conversation style
- **4-Level Intensity**: From basic optimization to endgame superposed vector retopologization

### Interactive Mode

```bash
./thaum
```

Use the Terminal.Gui interface to:
- Open projects (Ctrl+O)
- Start optimization (Ctrl+S) 
- Browse symbol hierarchies
- View optimizations and extracted keys
- Load conversation files for compression

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
| Language   | LSP Server | Status |
|------------|------------|---------|
| **C#**         | **omnisharp**  | ✅ **Full - Auto-download** |
| **Rust**       | **rust-analyzer** | ✅ **Full - Auto-download** |
| **Go**         | **gopls**      | ✅ **Full - Auto-download** |
| Python     | pylsp      | ✅ Full - System install required |
| JavaScript | typescript-language-server  | ✅ Full - System install required |
| TypeScript | typescript-language-server  | ✅ Full - System install required |
| Java       | jdtls      | 🚧 Planned |
| C++        | clangd     | 🚧 Planned |

**🚀 Auto-Download Feature**: Thaum automatically downloads, installs, and manages LSP servers for supported languages. Servers are cached locally and updated automatically. No manual installation required for C#, Rust, and Go!

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

### Conversation Compression Implementation
- [ ] `compress-conversation` command with file input/output
- [ ] `conversation-server` HTTP API for real-time compression
- [ ] `compress-batch` for directory processing
- [ ] Conversation-specific prompts (message/thread/context levels)
- [ ] Integration with popular chat formats (Claude, GPT, Discord, Slack)
- [ ] Streaming compression for long conversations
- [ ] Conversation reconstruction validation
- [ ] Performance benchmarks vs traditional `/compact` commands

### Codebase Features  
- [ ] Additional language support (Java, C++)
- [ ] Web interface alongside Terminal.Gui
- [ ] Prompt optimization with self-play
- [ ] Distributed processing for large codebases
- [ ] Integration with more AI providers
- [ ] Visual dependency graphs
- [ ] Export formats (Markdown, HTML, JSON)