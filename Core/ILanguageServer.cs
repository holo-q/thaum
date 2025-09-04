using Thaum.Core.Models;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

public interface ILanguageServer : IDisposable {
	Task<bool>                     StartLanguageServerAsync(string language, string workspacePath);
	Task<bool>                     StopLanguageServerAsync(string  language);
	Task<List<CodeSymbol>>         GetWorkspaceSymbolsAsync(string language, string workspacePath);
	Task<List<CodeSymbol>>         GetDocumentSymbolsAsync(string  language, string filePath);
	Task<string?>                  GetSymbolDefinitionAsync(string language, string filePath, ThaumPosition position);
	Task<List<string>>             GetSymbolReferencesAsync(string language, string filePath, ThaumPosition position);
	bool                           IsLanguageServerRunning(string  language);
	IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges(string    language, string workspacePath);
}