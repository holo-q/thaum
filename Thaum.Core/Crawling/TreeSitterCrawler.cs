using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Thaum.Core.Models;
using Thaum.Utils;
using TreeSitter;

namespace Thaum.Core.Services;

public static class TreeSitterQueries {
	public static readonly string UniversalQuery = @"
(namespace_declaration name: (_) @namespace.name) @namespace.body
(class_declaration name: (identifier) @class.name) @class.body
(method_declaration name: (identifier) @method.name) @method.body
(constructor_declaration name: (identifier) @constructor.name) @constructor.body
(property_declaration name: (identifier) @property.name) @property.body
(interface_declaration name: (identifier) @interface.name) @interface.body
(field_declaration (variable_declaration (variable_declarator name: (identifier) @field.name))) @field.body
(enum_declaration name: (identifier) @enum.name) @enum.body
(enum_member_declaration name: (identifier) @enum_member.name) @enum_member.body
";
}

/// <summary>
/// TreeSitter-based code crawler implementing high-performance AST parsing across multiple languages
/// where TreeSitter provides incremental parsing/concrete syntax trees/robust error recovery allowing
/// accurate symbol extraction even from malformed code where the crawler maintains language configs
/// for C#/Python/JavaScript/TypeScript/Go/Rust enabling polyglot analysis through unified interface
/// </summary>
public class TreeSitterCrawler : Crawler {
	private readonly ILogger<TreeSitterCrawler>                   _logger;
	private readonly Dictionary<string, TreeSitterLanguageConfig> _languageConfigs;

	public TreeSitterCrawler() {
		_logger          = Logging.For<TreeSitterCrawler>();
		_languageConfigs = InitializeLanguageConfigs();
	}

	public override async Task<CodeMap> CrawlDir(string dirpath, CodeMap? codeMap = null) {
		// Auto-detect language based on files in directory
		string language = LangUtil.DetectLanguageFromDirectory(dirpath);
		return await CrawlDir(language, dirpath, codeMap);
	}

	public override async Task<CodeMap> CrawlFile(string filepath, CodeMap? codeMap = null) {
		// Auto-detect language based on file extension
		string language = LangUtil.DetectLanguageFromFile(filepath);
		codeMap ??= CodeMap.Create();
		var symbols = await ExtractSymbolsFromFile(filepath, language);
		codeMap.AddSymbols(symbols);
		return codeMap;
	}

	public override async Task<CodeSymbol?> GetDefinitionFor(string name, CodeLoc location) {
		throw new InvalidOperationException("The TreeSitter crawler does not support definitions. LSPs are required for this.");
	}

	public override async Task<List<CodeSymbol>> GetReferencesFor(string name, CodeLoc location) {
		throw new InvalidOperationException("The TreeSitter crawler does not support reference crawling. LSPs are required for this.");
	}

	public override Task<string?> GetCode(CodeSymbol targetSymbol) => throw new NotImplementedException();

	/// <summary>
	/// Legacy compatibility - crawls directory and returns symbols as list
	/// </summary>
	public async Task<List<CodeSymbol>> CrawlDir(string lang, string dirpath) {
		var codeMap = await CrawlDir(lang, dirpath, null);
		return codeMap.ToList();
	}

	public async Task<CodeMap> CrawlDir(string lang, string dirpath, CodeMap? codeMap = null) {
		codeMap ??= CodeMap.Create();

		try {
			// Find all source files for the language using language-agnostic approach
			List<string> sourceFiles = Directory.GetFiles(dirpath, "*.*", SearchOption.AllDirectories)
				.Where(f => LangUtil.IsSourceFileForLanguage(f, lang))
				.Where(f => !ProjectExclusions.ShouldExclude(f, dirpath, lang))
				.ToList();

			_logger.LogDebug("Found {Count} {Language} files to parse", sourceFiles.Count, lang);

			foreach (string filePath in sourceFiles) {
				try {
					List<CodeSymbol> fileSymbols = await ExtractSymbolsFromFile(filePath, lang);
					codeMap.AddSymbols(fileSymbols);
				} catch (Exception ex) {
					_logger.LogWarning(ex, "Failed to parse file: {FilePath}", filePath);
				}
			}

			_logger.LogDebug("Extracted {Count} symbols from {Language} files", codeMap.Count, lang);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error scanning workspace for {Language} symbols", lang);
		}

		return codeMap;
	}

	public async Task<List<CodeSymbol>> CrawlFile(string lang, string filePath) {
		if (!File.Exists(filePath)) {
			return [];
		}

		return await ExtractSymbolsFromFile(filePath, lang);
	}

	public async Task<string?> GetDefinitionAt(string lang, string filePath, CodeLoc codeLoc) {
		// Enhanced TreeSitter-based symbol definition lookup
		if (!_languageConfigs.TryGetValue(lang.ToLowerInvariant(), out TreeSitterLanguageConfig? config)) {
			return null;
		}

		try {
			List<CodeSymbol> symbols = await CrawlFile(lang, filePath);
			CodeSymbol? symbol = symbols.FirstOrDefault(s =>
				codeLoc.Line >= s.StartCodeLoc.Line &&
				codeLoc.Line <= s.EndCodeLoc.Line);

			return symbol?.FilePath;
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting symbol definition for {FilePath}", filePath);
			return filePath;
		}
	}

	public async Task<List<string>> GetReferencesAt(string lang, string filePath, CodeLoc codeLoc) {
		// Enhanced TreeSitter-based symbol reference lookup
		if (!_languageConfigs.TryGetValue(lang.ToLowerInvariant(), out TreeSitterLanguageConfig? config)) {
			return [filePath];
		}

		try {
			List<CodeSymbol> symbols = await CrawlFile(lang, filePath);
			CodeSymbol? symbol = symbols.FirstOrDefault(s =>
				codeLoc.Line >= s.StartCodeLoc.Line &&
				codeLoc.Line <= s.EndCodeLoc.Line);

			return symbol != null ? [symbol.FilePath] : [filePath];
		} catch (Exception ex) {
			_logger.LogError(ex, "Error getting symbol references for {FilePath}", filePath);
			return [filePath];
		}
	}

	// public async IAsyncEnumerable<CodeChange> WatchDir(string lang, string workspacePath) {
	// 	FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(workspacePath) {
	// 		NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
	// 		IncludeSubdirectories = true
	// 	};
	//
	// 	// Create a simple async enumerable implementation using events
	// 	BlockingCollection<CodeChange> changeQueue = new BlockingCollection<CodeChange>();
	//
	// 	fileSystemWatcher.Changed += async (sender, e) => {
	// 		if (LanguageUtil.IsSourceFileForLanguage(e.FullPath, lang)) {
	// 			List<CodeSymbol> symbols = await ExtractSymbolsFromFile(e.FullPath, lang);
	// 			foreach (CodeSymbol symbol in symbols) {
	// 				changeQueue.Add(new CodeChange(e.FullPath, ChangeType.Modified, symbol));
	// 			}
	// 		}
	// 	};
	//
	// 	fileSystemWatcher.Created += async (sender, e) => {
	// 		if (LanguageUtil.IsSourceFileForLanguage(e.FullPath, lang)) {
	// 			List<CodeSymbol> symbols = await ExtractSymbolsFromFile(e.FullPath, lang);
	// 			foreach (CodeSymbol symbol in symbols) {
	// 				changeQueue.Add(new CodeChange(e.FullPath, ChangeType.Added, symbol));
	// 			}
	// 		}
	// 	};
	//
	// 	fileSystemWatcher.Deleted += (sender, e) => {
	// 		if (LanguageUtil.IsSourceFileForLanguage(e.FullPath, lang)) {
	// 			changeQueue.Add(new CodeChange(e.FullPath, ChangeType.Deleted, null));
	// 		}
	// 	};
	//
	// 	fileSystemWatcher.Renamed += async (sender, e) => {
	// 		if (LanguageUtil.IsSourceFileForLanguage(e.FullPath, lang)) {
	// 			List<CodeSymbol> symbols = await ExtractSymbolsFromFile(e.FullPath, lang);
	// 			foreach (CodeSymbol symbol in symbols) {
	// 				changeQueue.Add(new CodeChange(e.FullPath, ChangeType.Renamed, symbol));
	// 			}
	// 		}
	// 	};
	//
	// 	fileSystemWatcher.EnableRaisingEvents = true;
	//
	// 	try {
	// 		foreach (CodeChange change in changeQueue.GetConsumingEnumerable()) {
	// 			yield return change;
	// 		}
	// 	} finally {
	// 		fileSystemWatcher.Dispose();
	// 		changeQueue.Dispose();
	// 	}
	// }

	private async Task<List<CodeSymbol>> ExtractSymbolsFromFile(string filePath, string language) {
		List<CodeSymbol> symbols = [];

		try {
			string content = await File.ReadAllTextAsync(filePath);

			// Get the TreeSitter language configuration
			if (_languageConfigs.TryGetValue(language, out var config)) {
				using var parser = new Parser(config.Language);
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

	public class Parser : IDisposable {
		private readonly TreeSitter.Parser _parser;
		private readonly Language          _language;
		private readonly ILogger<Parser>   _logger;

		public Parser(string language) {
			_logger   = Logging.For<Parser>();
			_language = new Language(language);
			_parser   = new TreeSitter.Parser(_language);
		}

		public List<CodeSymbol> Parse(string sourceCode, string filePath) {
			var       symbols = new List<CodeSymbol>();
			using var tree    = _parser.Parse(sourceCode);
			var       query   = new Query(_language, TreeSitterQueries.UniversalQuery);
			var       matches = query.Execute(tree.RootNode).Matches.ToList();

			foreach (var match in matches) {
				Node? nameNode = null;
				Node? bodyNode = null;

				foreach (var capture in match.Captures) {
					if (capture.Name.EndsWith(".name")) {
						nameNode = capture.Node;
					} else if (capture.Name.EndsWith(".body")) {
						bodyNode = capture.Node;
					}
				}

				if (nameNode != null && bodyNode != null) {
					var captureName = match.Captures.First(c => c.Name.EndsWith(".name")).Name;
					var symbolKind  = GetSymbolKind(captureName);

					symbols.Add(new CodeSymbol(
						Name: nameNode.Text,
						Kind: symbolKind,
						FilePath: filePath,
						StartCodeLoc: new CodeLoc((int)nameNode.StartPosition.Row, (int)nameNode.StartPosition.Column),
						EndCodeLoc: new CodeLoc((int)bodyNode.EndPosition.Row, (int)bodyNode.EndPosition.Column)
					));
				}
			}

			return symbols;
		}

		private SymbolKind GetSymbolKind(string captureName) {
			if (captureName.StartsWith("namespace")) {
				return SymbolKind.Namespace;
			} else if (captureName.StartsWith("function")) {
				return SymbolKind.Function;
			} else if (captureName.StartsWith("method")) {
				return SymbolKind.Method;
			} else if (captureName.StartsWith("constructor")) {
				return SymbolKind.Constructor;
			} else if (captureName.StartsWith("property")) {
				return SymbolKind.Property;
			} else if (captureName.StartsWith("field")) {
				return SymbolKind.Field;
			} else if (captureName.StartsWith("interface")) {
				return SymbolKind.Interface;
			} else if (captureName.StartsWith("class")) {
				return SymbolKind.Class;
			} else if (captureName.StartsWith("enum_member")) {
				return SymbolKind.EnumMember;
			} else if (captureName.StartsWith("enum")) {
				return SymbolKind.Enum;
			} else {
				return SymbolKind.Variable;
			}
		}

		public void Dispose() {
			_parser.Dispose();
		}
	}
}