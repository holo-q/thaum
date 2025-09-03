# LSP Server Auto-Download System

## Overview

Thaum includes an automatic LSP server download and management system that eliminates the need for manual installation of language servers. This provides a seamless out-of-the-box experience similar to VS Code.

## Features

### 🚀 Automatic Download
- **Zero Configuration**: Language servers are downloaded automatically on first use
- **Cross-Platform**: Supports Windows, Linux, and macOS with automatic architecture detection
- **Progress Reporting**: Real-time download progress with visual feedback

### 🔄 Version Management
- **Latest Versions**: Automatically fetches the latest stable releases
- **Update Detection**: Checks for newer versions and updates automatically
- **Cache Management**: Intelligent caching with automatic cleanup of old versions

### 📁 Local Caching
- **Cache Location**: `~/.local/share/Thaum/lsp-servers/` (Linux/macOS) or `%LOCALAPPDATA%\Thaum\lsp-servers\` (Windows)
- **Space Efficient**: Only downloads when needed, removes old versions
- **Offline Ready**: Works offline once servers are cached

## Supported Servers

### Auto-Download Enabled

| Language | Server | Source |
|----------|---------|---------|
| **C#** | OmniSharp | [GitHub Releases](https://github.com/OmniSharp/omnisharp-roslyn/releases) |
| **Rust** | rust-analyzer | [GitHub Releases](https://github.com/rust-lang/rust-analyzer/releases) |
| **Go** | gopls | [GitHub Releases](https://github.com/golang/tools/releases) |

### System Installation Required

| Language | Server | Installation Command |
|----------|---------|---------------------|
| **Python** | pylsp | `pip install python-lsp-server` |
| **TypeScript/JavaScript** | typescript-language-server | `npm install -g typescript-language-server` |

## How It Works

### 1. First Use
```bash
# First time running with C#
dotnet run -- ls test_project --lang csharp

# Output:
# 🔽 OmniSharp: 000% - Starting download...
# 🔽 OmniSharp: 025% - Downloading... (4623 KB)
# 🔽 OmniSharp: 050% - Downloading... (9247 KB)  
# 🔽 OmniSharp: 075% - Downloading... (13863 KB)
# 🔽 OmniSharp: 100% - Downloading... (18483 KB)
# ✅ OmniSharp: Download complete!
# Scanning test_project for csharp symbols...
```

### 2. Subsequent Uses
```bash
# Second time - uses cached version
dotnet run -- ls test_project --lang csharp

# Output:
# Scanning test_project for csharp symbols...
# Found 15 symbols total
```

### 3. Automatic Updates
The system checks for updates periodically and downloads newer versions when available.

## Architecture

### Download Manager (`LspServerManager`)
- **Server Discovery**: Queries GitHub APIs for latest releases
- **Platform Detection**: Automatically detects OS and architecture
- **Download & Extract**: Handles zip/tar.gz archives with progress tracking
- **Permission Setting**: Sets executable permissions on Unix systems
- **Version Tracking**: Maintains version metadata for update detection

### Progress Reporting (`ILspDownloadProgress`)
- **Console Reporter**: Shows progress bars and status messages
- **Extensible**: Can be replaced with GUI progress dialogs
- **Thread-Safe**: Handles concurrent downloads properly

### Integration (`ProperLspClientManager`)
- **Transparent**: Auto-download happens seamlessly during server startup
- **Fallback**: Falls back to system PATH if download fails
- **Caching**: Reuses downloaded servers across sessions

## Configuration

### Server Information
Language servers are configured in `LspServerManager.GetServerInfo()`:

```csharp
"csharp" => new LspServerInfo
{
    Name = "OmniSharp",
    Version = "1.39.14",  // or "latest"
    ExecutableName = "OmniSharp.exe",  // platform-specific
    Arguments = new[] { "--languageserver" },
    DownloadUrlPattern = "https://github.com/OmniSharp/omnisharp-roslyn/releases/download/v{version}/omnisharp-{os}-{arch}{ext}",
    VersionUrl = "https://api.github.com/repos/OmniSharp/omnisharp-roslyn/releases/latest"
}
```

### URL Patterns
Download URLs support these placeholders:
- `{version}`: Release version (e.g., "1.39.14")
- `{os}`: Platform identifier ("win", "linux", "osx")  
- `{arch}`: Architecture ("x64", "arm64", "x86")
- `{ext}`: Archive extension (".zip", ".tar.gz")

## Benefits

### For Users
- **No Setup Required**: Works out of the box
- **Always Updated**: Latest language server features
- **Cross-Platform**: Same experience on all platforms
- **Offline Capable**: Works without internet after initial download

### For Developers
- **Consistent Environment**: Same LSP versions across team
- **CI/CD Friendly**: No manual setup in build environments
- **Version Control**: Reproducible builds with specific LSP versions
- **Easy Debugging**: Clear logs and progress reporting

## Troubleshooting

### Download Failures
- **Network Issues**: Check internet connectivity
- **Proxy Settings**: Configure HTTP proxy if needed
- **GitHub Rate Limits**: Try again later if rate limited
- **Disk Space**: Ensure sufficient space in cache directory

### Cache Issues
```bash
# Clear cache manually if needed
rm -rf ~/.local/share/Thaum/lsp-servers/

# Or on Windows
rmdir /s %LOCALAPPDATA%\Thaum\lsp-servers\
```

### Permissions
On Unix systems, ensure the cache directory is writable:
```bash
chmod 755 ~/.local/share/Thaum/lsp-servers/
```

## Future Enhancements

- **GUI Progress**: Rich progress dialogs for desktop applications
- **Parallel Downloads**: Download multiple servers simultaneously  
- **Custom Sources**: Support for private/corporate language server repositories
- **Delta Updates**: Incremental updates to reduce bandwidth
- **Verification**: Checksum verification for downloaded binaries
- **Rollback**: Ability to rollback to previous versions

## Migration from Manual Installation

If you previously installed language servers manually:
1. The auto-download system will use cached versions first
2. Manual installations in PATH are used as fallback
3. Auto-downloaded servers take precedence for consistency
4. Old manual installations can be safely removed

The system provides the best of both worlds: automatic management with manual override capability.