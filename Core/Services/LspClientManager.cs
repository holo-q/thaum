using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspSymbolKind = OmniSharp.Extensions.LanguageServer.Protocol.Models.SymbolKind;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using System.Diagnostics;
using System.Text.Json;
using Thaum.Core.Models;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

public class LspClientManager : ILspClientManager
{
    private readonly ILogger<LspClientManager> _logger;
    private readonly Dictionary<string, LanguageServerConnection> _connections = new();
    private readonly Dictionary<string, Process> _serverProcesses = new();

    public LspClientManager(ILogger<LspClientManager> logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartLanguageServerAsync(string language, string workspacePath)
    {
        if (_connections.ContainsKey(language))
        {
            _logger.LogInformation("Language server for {Language} already running", language);
            return true;
        }

        try
        {
            var serverCommand = GetLanguageServerCommand(language);
            if (serverCommand == null)
            {
                _logger.LogError("No language server command found for {Language}", language);
                return false;
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = serverCommand.Command,
                    Arguments = string.Join(" ", serverCommand.Arguments),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workspacePath
                }
            };

            process.Start();
            _serverProcesses[language] = process;

            var client = LanguageClient.PreInit(options =>
            {
                options
                    .WithInput(process.StandardOutput.BaseStream)
                    .WithOutput(process.StandardInput.BaseStream)
                    .WithLoggerFactory(LoggerFactory.Create(builder => builder.AddConsole()));
            });

            await client.Initialize(CancellationToken.None);
            
            var connection = new LanguageServerConnection(client, process);
            _connections[language] = connection;

            _logger.LogInformation("Language server for {Language} started successfully", language);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start language server for {Language}", language);
            return false;
        }
    }

    public async Task<bool> StopLanguageServerAsync(string language)
    {
        if (!_connections.TryGetValue(language, out var connection))
        {
            return false;
        }

        try
        {
            await connection.Client.Shutdown(CancellationToken.None);
            connection.Client.Exit();
            
            if (_serverProcesses.TryGetValue(language, out var process))
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                process.Dispose();
                _serverProcesses.Remove(language);
            }

            connection.Dispose();
            _connections.Remove(language);
            
            _logger.LogInformation("Language server for {Language} stopped", language);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping language server for {Language}", language);
            return false;
        }
    }

    public async Task<List<CodeSymbol>> GetWorkspaceSymbolsAsync(string language, string workspacePath)
    {
        if (!_connections.TryGetValue(language, out var connection))
        {
            throw new InvalidOperationException($"Language server for {language} is not running");
        }

        try
        {
            var symbols = await connection.Client.TextDocument.SendRequest(
                new WorkspaceSymbolParams { Query = "" },
                CancellationToken.None);

            return symbols?.Select(ConvertToCodeSymbol).ToList() ?? new List<CodeSymbol>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workspace symbols for {Language}", language);
            return new List<CodeSymbol>();
        }
    }

    public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string language, string filePath)
    {
        if (!_connections.TryGetValue(language, out var connection))
        {
            throw new InvalidOperationException($"Language server for {language} is not running");
        }

        try
        {
            var documentUri = DocumentUri.FromFileSystemPath(filePath);
            var symbols = await connection.Client.TextDocument.SendRequest(
                new DocumentSymbolParams
                {
                    TextDocument = new TextDocumentIdentifier(documentUri)
                },
                CancellationToken.None);

            return symbols?.Select(ConvertToCodeSymbol).ToList() ?? new List<CodeSymbol>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get document symbols for {FilePath}", filePath);
            return new List<CodeSymbol>();
        }
    }

    public async Task<string?> GetSymbolDefinitionAsync(string language, string filePath, ThaumPosition position)
    {
        if (!_connections.TryGetValue(language, out var connection))
        {
            return null;
        }

        try
        {
            var documentUri = DocumentUri.FromFileSystemPath(filePath);
            var definition = await connection.Client.TextDocument.SendRequest(
                new DefinitionParams
                {
                    TextDocument = new TextDocumentIdentifier(documentUri),
                    Position = new LspPosition(position.Line, position.Character)
                },
                CancellationToken.None);

            return definition?.FirstOrDefault()?.Uri.GetFileSystemPath();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get symbol definition");
            return null;
        }
    }

    public async Task<List<string>> GetSymbolReferencesAsync(string language, string filePath, ThaumPosition position)
    {
        if (!_connections.TryGetValue(language, out var connection))
        {
            return new List<string>();
        }

        try
        {
            var documentUri = DocumentUri.FromFileSystemPath(filePath);
            var references = await connection.Client.TextDocument.SendRequest(
                new ReferenceParams
                {
                    TextDocument = new TextDocumentIdentifier(documentUri),
                    Position = new LspPosition(position.Line, position.Character),
                    Context = new ReferenceContext { IncludeDeclaration = true }
                },
                CancellationToken.None);

            return references?.Select(r => r.Uri.GetFileSystemPath()).ToList() ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get symbol references");
            return new List<string>();
        }
    }

    public bool IsLanguageServerRunning(string language)
    {
        return _connections.ContainsKey(language);
    }

    public async IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges(string language, string workspacePath)
    {
        if (!_connections.TryGetValue(language, out var connection))
        {
            yield break;
        }

        // Implementation for workspace change watching would go here
        // This is a placeholder for the async enumerable pattern
        await Task.CompletedTask;
        yield break;
    }

    private static CodeSymbol ConvertToCodeSymbol(SymbolInformation symbol)
    {
        return new CodeSymbol(
            Name: symbol.Name,
            Kind: ConvertSymbolKind(symbol.Kind),
            FilePath: symbol.Location.Uri.GetFileSystemPath(),
            StartPosition: new ThaumPosition(symbol.Location.Range.Start.Line, symbol.Location.Range.Start.Character),
            EndPosition: new ThaumPosition(symbol.Location.Range.End.Line, symbol.Location.Range.End.Character)
        );
    }

    private static CodeSymbol ConvertToCodeSymbol(DocumentSymbol symbol)
    {
        var children = symbol.Children?.Select(ConvertToCodeSymbol).ToList();
        
        return new CodeSymbol(
            Name: symbol.Name,
            Kind: ConvertSymbolKind(symbol.Kind),
            FilePath: "", // Will be set by caller
            StartPosition: new ThaumPosition(symbol.Range.Start.Line, symbol.Range.Start.Character),
            EndPosition: new ThaumPosition(symbol.Range.End.Line, symbol.Range.End.Character),
            Children: children
        );
    }

    private static Thaum.Core.Models.SymbolKind ConvertSymbolKind(LspSymbolKind kind)
    {
        return kind switch
        {
            LspSymbolKind.Function => Thaum.Core.Models.SymbolKind.Function,
            LspSymbolKind.Method => Thaum.Core.Models.SymbolKind.Method,
            LspSymbolKind.Class => Thaum.Core.Models.SymbolKind.Class,
            LspSymbolKind.Interface => Thaum.Core.Models.SymbolKind.Interface,
            LspSymbolKind.Module => Thaum.Core.Models.SymbolKind.Module,
            LspSymbolKind.Namespace => Thaum.Core.Models.SymbolKind.Namespace,
            LspSymbolKind.Property => Thaum.Core.Models.SymbolKind.Property,
            LspSymbolKind.Field => Thaum.Core.Models.SymbolKind.Field,
            LspSymbolKind.Variable => Thaum.Core.Models.SymbolKind.Variable,
            _ => Thaum.Core.Models.SymbolKind.Function
        };
    }

    private static LanguageServerCommand? GetLanguageServerCommand(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "python" => new LanguageServerCommand("python", ["-m", "pylsp"]),
            "csharp" => new LanguageServerCommand("dotnet", ["exec", "csharp-ls"]),
            "javascript" => new LanguageServerCommand("typescript-language-server", ["--stdio"]),
            "typescript" => new LanguageServerCommand("typescript-language-server", ["--stdio"]),
            "rust" => new LanguageServerCommand("rust-analyzer", []),
            "go" => new LanguageServerCommand("gopls", []),
            _ => null
        };
    }

    public void Dispose()
    {
        foreach (var (language, _) in _connections.ToList())
        {
            StopLanguageServerAsync(language).Wait();
        }
    }
}

internal record LanguageServerCommand(string Command, string[] Arguments);

internal record LanguageServerConnection(object Client, Process Process) : IDisposable
{
    public void Dispose()
    {
        if (Client is IDisposable disposableClient)
        {
            disposableClient.Dispose();
        }
        Process?.Dispose();
    }
}