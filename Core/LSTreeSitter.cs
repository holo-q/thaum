using Microsoft.Extensions.Logging;
using System.Text;
using System.IO;
using System.Collections.Concurrent;
using Thaum.Core.Models;
using Thaum.Core.Services;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

/// <summary>
/// LSP Client Manager that uses TreeSitter for robust parsing of multiple languages
/// Provides accurate AST-based symbol extraction for lossless compression optimization
/// </summary>
public class LSTreeSitter : ILanguageServer, IDisposable {
	private readonly ILogger<LSTreeSitter>                        _logger;
	private readonly Dictionary<string, TreeSitterLanguageConfig> _languageConfigs;
	private readonly Dictionary<string, bool>                     _activeLanguages;
	private readonly Dictionary<string, FileSystemWatcher>        _fileWatchers;

	private readonly ILoggerFactory _loggerFactory;

	public LSTreeSitter(ILoggerFactory loggerFactory) {
		_loggerFactory   = loggerFactory;
		_logger          = _loggerFactory.CreateLogger<LSTreeSitter>();
		_languageConfigs = InitializeLanguageConfigs();
		_activeLanguages = new Dictionary<string, bool>();
		_fileWatchers    = new Dictionary<string, FileSystemWatcher>();
	}

	public async Task<bool> StartLanguageServerAsync(string language, string workspacePath) {
		_logger.LogInformation("Starting TreeSitter parser for {Language} at {WorkspacePath}", language, workspacePath);

		string langKey = language.ToLowerInvariant();
		if (!_languageConfigs.ContainsKey(langKey)) {
			_logger.LogWarning("No TreeSitter configuration found for language: {Language}", language);
			return false;
		}

		try {
			_activeLanguages[langKey] = true;
			_logger.LogDebug("Successfully started TreeSitter parser for {Language}", language);
			return true;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error starting TreeSitter parser for {Language}", language);
			return false;
		}
	}

	public async Task<bool> StopLanguageServerAsync(string language) {
		_logger.LogInformation("Stopping TreeSitter parser for {Language}", language);

		string langKey = language.ToLowerInvariant();
		if (_activeLanguages.ContainsKey(langKey)) {
			_activeLanguages.Remove(langKey);
		}

		return true;
	}

	public async Task<List<CodeSymbol>> GetWorkspaceSymbolsAsync(string language, string workspacePath) {
		List<CodeSymbol> symbols = new List<CodeSymbol>();

		try {
			// Find all source files for the language using language-agnostic approach
			List<string> sourceFiles = Directory.GetFiles(workspacePath, "*.*", SearchOption.AllDirectories)
				.Where(f => IsSourceFileForLanguage(f, language))
				.ToList();

			_logger.LogDebug("Found {Count} {Language} files to parse", sourceFiles.Count, language);

			foreach (string filePath in sourceFiles) {
				try {
					List<CodeSymbol> fileSymbols = await ExtractSymbolsFromFile(filePath, language);
					symbols.AddRange(fileSymbols);
				} catch (Exception ex) {
					_logger.LogWarning(ex, "Failed to parse file: {FilePath}", filePath);
				}
			}

			_logger.LogDebug("Extracted {Count} symbols from {Language} files", symbols.Count, language);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error scanning workspace for {Language} symbols", language);
		}

		return symbols;
	}

	public async Task<List<CodeSymbol>> GetDocumentSymbolsAsync(string language, string filePath) {
		if (!File.Exists(filePath)) {
			return new List<CodeSymbol>();
		}

		return await ExtractSymbolsFromFile(filePath, language);
	}

	public async Task<string?> GetSymbolDefinitionAsync(string language, string filePath, ThaumPosition position) {
		// Enhanced TreeSitter-based symbol definition lookup
		if (!_languageConfigs.TryGetValue(language.ToLowerInvariant(), out TreeSitterLanguageConfig? config)) {
			return null;
		}

		try {
			List<CodeSymbol> symbols = await GetDocumentSymbolsAsync(language, filePath);
			CodeSymbol? symbol = symbols.FirstOrDefault(s =>
				position.Line >= s.StartPosition.Line &&
				position.Line <= s.EndPosition.Line);

			return symbol?.FilePath;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting symbol definition for {FilePath}", filePath);
			return filePath;
		}
	}

	public async Task<List<string>> GetSymbolReferencesAsync(string language, string filePath, ThaumPosition position) {
		// Enhanced TreeSitter-based symbol reference lookup
		if (!_languageConfigs.TryGetValue(language.ToLowerInvariant(), out TreeSitterLanguageConfig? config)) {
			return new List<string> { filePath };
		}

		try {
			List<CodeSymbol> symbols = await GetDocumentSymbolsAsync(language, filePath);
			CodeSymbol? symbol = symbols.FirstOrDefault(s =>
				position.Line >= s.StartPosition.Line &&
				position.Line <= s.EndPosition.Line);

			return symbol != null ? new List<string> { symbol.FilePath } : new List<string> { filePath };
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting symbol references for {FilePath}", filePath);
			return new List<string> { filePath };
		}
	}

	public bool IsLanguageServerRunning(string language) {
		return _activeLanguages.ContainsKey(language.ToLowerInvariant());
	}

	public async IAsyncEnumerable<SymbolChange> WatchWorkspaceChanges(string language, string workspacePath) {
		FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(workspacePath) {
			NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
			IncludeSubdirectories = true
		};

		// Create a simple async enumerable implementation using events
		BlockingCollection<SymbolChange> changeQueue = new BlockingCollection<SymbolChange>();

		fileSystemWatcher.Changed += async (sender, e) => {
			if (IsSourceFileForLanguage(e.FullPath, language)) {
				List<CodeSymbol> symbols = await ExtractSymbolsFromFile(e.FullPath, language);
				foreach (CodeSymbol symbol in symbols) {
					changeQueue.Add(new SymbolChange(e.FullPath, ChangeType.Modified, symbol));
				}
			}
		};

		fileSystemWatcher.Created += async (sender, e) => {
			if (IsSourceFileForLanguage(e.FullPath, language)) {
				List<CodeSymbol> symbols = await ExtractSymbolsFromFile(e.FullPath, language);
				foreach (CodeSymbol symbol in symbols) {
					changeQueue.Add(new SymbolChange(e.FullPath, ChangeType.Added, symbol));
				}
			}
		};

		fileSystemWatcher.Deleted += (sender, e) => {
			if (IsSourceFileForLanguage(e.FullPath, language)) {
				changeQueue.Add(new SymbolChange(e.FullPath, ChangeType.Deleted, null));
			}
		};

		fileSystemWatcher.Renamed += async (sender, e) => {
			if (IsSourceFileForLanguage(e.FullPath, language)) {
				List<CodeSymbol> symbols = await ExtractSymbolsFromFile(e.FullPath, language);
				foreach (CodeSymbol symbol in symbols) {
					changeQueue.Add(new SymbolChange(e.FullPath, ChangeType.Renamed, symbol));
				}
			}
		};

		fileSystemWatcher.EnableRaisingEvents = true;

		try {
			foreach (SymbolChange change in changeQueue.GetConsumingEnumerable()) {
				yield return change;
			}
		} finally {
			fileSystemWatcher.Dispose();
			changeQueue.Dispose();
		}
	}

	public void Dispose() {
		// Clean up file watchers
		foreach (FileSystemWatcher watcher in _fileWatchers.Values) {
			watcher.Dispose();
		}
		_fileWatchers.Clear();
		_activeLanguages.Clear();
	}


	private async Task<List<CodeSymbol>> ExtractSymbolsFromFile(string filePath, string language) {
		List<CodeSymbol> symbols = new List<CodeSymbol>();

		try {
			string content = await File.ReadAllTextAsync(filePath);
			
			// Get the TreeSitter language configuration
			if (_languageConfigs.TryGetValue(language, out var config)) {
				using var parser = new TreeSitterParser(config.Language, _loggerFactory.CreateLogger<TreeSitterParser>());
				symbols = parser.Parse(content, filePath);
				_logger.LogDebug("Extracted {Count} symbols from {FilePath} using TreeSitter", symbols.Count, filePath);
			} else {
				_logger.LogWarning("No TreeSitter configuration found for language: {Language}", language);
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error parsing file with TreeSitter: {FilePath}", filePath);
		}

		return symbols;
	}

	private static bool IsSourceFileForLanguage(string filePath, string language) {
		string extension = Path.GetExtension(filePath).ToLowerInvariant();

		return language.ToLowerInvariant() switch {
			"python"     => extension == ".py",
			"c-sharp"    => extension == ".cs",
			"javascript" => extension is ".js" or ".jsx",
			"typescript" => extension is ".ts" or ".tsx",
			"rust"       => extension == ".rs",
			"go"         => extension == ".go",
			_            => false
		};
	}

	private Dictionary<string, TreeSitterLanguageConfig> InitializeLanguageConfigs() {
		return new Dictionary<string, TreeSitterLanguageConfig> {
			["c-sharp"] = new TreeSitterLanguageConfig {
				Language = "c_sharp"
			},
			["python"] = new TreeSitterLanguageConfig {
				Language = "python"
			},
			["javascript"] = new TreeSitterLanguageConfig {
				Language = "javascript"
			},
			["typescript"] = new TreeSitterLanguageConfig {
				Language = "typescript"
			},
			["rust"] = new TreeSitterLanguageConfig {
				Language = "rust"
			},
			["go"] = new TreeSitterLanguageConfig {
				Language = "go"
			}
		};
	}

	public record TreeSitterLanguageConfig {
		public required string Language { get; init; }
	}
}