# LSP Integration Guide

## Overview

Thaum now includes a proper Language Server Protocol (LSP) client implementation that communicates with external language servers via JSON-RPC over stdio. This provides superior code intelligence compared to simple file parsing.

## Architecture

### Core Components

1. **ProperLspClientManager**: Main LSP client manager that handles multiple language servers
2. **LspServerInstance**: Manages individual language server processes and JSON-RPC communication
3. **LspWorkspaceWatcher**: File system monitoring for workspace changes

### Communication Flow

```
Thaum Application
    ↓ (ILspClientManager interface)
ProperLspClientManager
    ↓ (Process management)
LspServerInstance
    ↓ (JSON-RPC over stdio)
Language Server Process (omnisharp, pylsp, etc.)
```

## Supported Language Servers

| Language   | Server Command | Installation |
|------------|----------------|--------------|
| C#         | `omnisharp`    | Download from [OmniSharp releases](https://github.com/OmniSharp/omnisharp-roslyn/releases) |
| Python     | `pylsp`        | `pip install python-lsp-server` |
| TypeScript | `typescript-language-server` | `npm install -g typescript-language-server` |
| Rust       | `rust-analyzer` | `rustup component add rust-analyzer` |
| Go         | `gopls`        | `go install golang.org/x/tools/gopls@latest` |

## LSP Features Implemented

### Core LSP Methods

- **Initialize**: Establishes LSP connection with server capabilities
- **Workspace Symbols**: `workspace/symbol` - Find symbols across entire workspace
- **Document Symbols**: `textDocument/documentSymbol` - Get symbols from specific file
- **Go to Definition**: `textDocument/definition` - Navigate to symbol definitions
- **Find References**: `textDocument/references` - Find all references to a symbol
- **Shutdown/Exit**: Clean termination of language server processes

### JSON-RPC Communication

The implementation handles:
- Content-Length header parsing
- JSON message serialization/deserialization
- Request/response correlation via request IDs
- Notification messages
- Error handling and timeout management

## Configuration

Language servers are configured in `ProperLspClientManager.GetServerConfiguration()`:

```csharp
"csharp" => new LspServerConfiguration {
    ExecutablePath = "omnisharp",
    Arguments = new[] { "--languageserver" },
    WorkingDirectory = null,
    EnvironmentVariables = new Dictionary<string, string>()
}
```

## Testing the Implementation

### Install OmniSharp for C# Support

**Option 1: Use the provided installation script (Recommended)**
```bash
# Run the installation script from the Thaum repository
./install-omnisharp.sh
```

**Option 2: Manual installation**
```bash
# Download and install OmniSharp manually
mkdir -p ~/.local/bin
cd ~/.local/bin
wget https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v1.39.14/omnisharp-linux-x64.tar.gz
tar -xzf omnisharp-linux-x64.tar.gz
chmod +x OmniSharp

# Add to PATH if needed
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc

# Verify installation
OmniSharp --version
```

**Option 3: Package manager (if available)**
```bash
# Arch Linux (AUR)
yay -S omnisharp-roslyn

# Some distributions may have packages
# Check your package manager for omnisharp-roslyn
```

**Note**: The OmniSharp executable may be named `OmniSharp` or `omnisharp` depending on the installation method. Thaum will automatically detect the correct executable name.

### Test LSP Integration

```bash
# Test symbol listing with LSP
dotnet run -- ls test_project --lang csharp

# Test with different languages (if servers installed)
dotnet run -- ls /path/to/python/project --lang python
dotnet run -- ls /path/to/rust/project --lang rust
```

### Troubleshooting

1. **Server not found**: Ensure language server is installed and in PATH
2. **Connection timeout**: Check that server accepts `--languageserver` or `--stdio` arguments
3. **Symbol parsing issues**: Verify workspace has proper project files (.csproj, pyproject.toml, etc.)

## Implementation Details

### Process Management

- Each language server runs as a separate child process
- Stdio streams are used for JSON-RPC communication
- Clean shutdown with proper exit notifications
- Process timeout and error handling

### JSON-RPC Protocol

```csharp
// Example initialize request
{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
        "processId": 12345,
        "rootUri": "file:///path/to/workspace",
        "capabilities": {
            "textDocument": {
                "documentSymbol": {},
                "definition": {},
                "references": {}
            },
            "workspace": {
                "symbol": {}
            }
        }
    }
}
```

### Symbol Conversion

LSP symbols are converted to Thaum's internal `CodeSymbol` format:

```csharp
private static CodeSymbol ConvertSymbolInformation(SymbolInformation symbol) {
    return new CodeSymbol(
        Name: symbol.Name,
        Kind: ConvertSymbolKind(symbol.Kind),
        FilePath: symbol.Location.Uri.GetFileSystemPath(),
        StartPosition: new ThaumPosition(symbol.Location.Range.Start.Line, symbol.Location.Range.Start.Character),
        EndPosition: new ThaumPosition(symbol.Location.Range.End.Line, symbol.Location.Range.End.Character)
    );
}
```

## Future Enhancements

- **Incremental Updates**: Support for `textDocument/didChange` notifications
- **Diagnostic Support**: LSP diagnostics integration for error detection
- **Completion Support**: Code completion via LSP
- **Hover Information**: Symbol hover information
- **Signature Help**: Function signature assistance
- **Code Actions**: LSP-provided code actions and quick fixes

## Migration from SimpleLspClientManager

The `SimpleLspClientManager` (file-based parsing) has been replaced with `ProperLspClientManager` (LSP-based) in the main application. The interface remains the same (`ILspClientManager`), ensuring backward compatibility.

Benefits of the new implementation:

1. **Accurate Symbol Analysis**: Uses compiler-grade symbol information
2. **Cross-References**: Proper go-to-definition and find-references
3. **Project Context**: Language servers understand project structure
4. **Real-time Updates**: Language servers can provide incremental updates
5. **Extensibility**: Easy to add new languages by configuring their LSP servers