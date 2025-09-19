using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Thaum.Core.Utils;
using TreeSitter;

namespace Thaum.Core.Crawling;

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
	private readonly int                                          _maxDegreeOfParallelism;

	public TreeSitterCrawler() {
		_logger          = Logging.For<TreeSitterCrawler>();
		_languageConfigs = InitializeLanguageConfigs();
		_maxDegreeOfParallelism = GetMaxDegreeOfParallelism();
	}

	public override async Task<CodeMap> CrawlDir(string dirpath, CodeMap? codeMap = null) {
		// Scan all supported source files present (multi-language repos)
		codeMap ??= CodeMap.Create();

		try {
			List<string> files = Directory.GetFiles(dirpath, "*.*", SearchOption.AllDirectories)
				.Where(LangUtil.IsSourceFile)
				.ToList();

			IEnumerable<IGrouping<string, string>> byLang = files
				.Select(f => new { file = f, lang = LangUtil.DetectLanguageFromFile(f).ToLowerInvariant() })
				.GroupBy(t => t.lang, t => t.file);

			foreach (IGrouping<string, string> group in byLang) {
				string lang = group.Key;
				if (_languageConfigs.ContainsKey(lang)) {
					_logger.LogDebug("Scanning {Count} {Language} files...", group.Count(), lang);
					await CrawlDir(lang, dirpath, codeMap);
				}
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error scanning workspace for symbols at {Dir}", dirpath);
		}

		return codeMap;
	}

	public override async Task<CodeMap> CrawlFile(string filepath, CodeMap? codeMap = null) {
		// Auto-detect language based on file extension
		string language = LangUtil.DetectLanguageFromFile(filepath);
		codeMap ??= CodeMap.Create();
		List<CodeSymbol> symbols = await ExtractSymbolsFromFile(filepath, language);
		codeMap.AddSymbols(symbols);
		return codeMap;
	}

	public override async Task<CodeSymbol?> GetDefinitionFor(string name, CodeLoc location) {
		throw new InvalidOperationException("The TreeSitter crawler does not support definitions. LSPs are required for this.");
	}

	public override async Task<List<CodeSymbol>> GetReferencesFor(string name, CodeLoc location) {
		throw new InvalidOperationException("The TreeSitter crawler does not support reference crawling. LSPs are required for this.");
	}

	public override async Task<string?> GetCode(CodeSymbol targetSymbol) {
		try {
			if (string.IsNullOrEmpty(targetSymbol.FilePath) || !File.Exists(targetSymbol.FilePath)) {
				return null;
			}

			string[] lines = await File.ReadAllLinesAsync(targetSymbol.FilePath);
			int startLine = Math.Clamp(targetSymbol.StartCodeLoc.Line, 0, Math.Max(0, lines.Length - 1));
			int endLine   = Math.Clamp(targetSymbol.EndCodeLoc.Line,   0, Math.Max(0, lines.Length - 1));

			if (endLine < startLine) (startLine, endLine) = (endLine, startLine);

			int startCol = Math.Max(0, targetSymbol.StartCodeLoc.Character);
			int endCol   = Math.Max(0, targetSymbol.EndCodeLoc.Character);

			StringBuilder sb = new System.Text.StringBuilder();
			for (int i = startLine; i <= endLine; i++) {
				string line = lines[i];
				if (i == startLine && i == endLine) {
					int from = Math.Min(startCol, line.Length);
					int to   = Math.Min(endCol,   line.Length);
					if (to > from) sb.Append(line.AsSpan(from, to - from));
					else sb.Append(line[from..]);
				} else if (i == startLine) {
					int from = Math.Min(startCol, line.Length);
					sb.AppendLine(line[from..]);
					continue;
				} else if (i == endLine) {
					int to = Math.Min(endCol, line.Length);
					sb.Append(line[..to]);
				} else {
					sb.AppendLine(line);
				}
			}

			return sb.ToString();
		} catch (Exception ex) {
			_logger.LogError(ex, "Error extracting code for symbol {Name} in {File}", targetSymbol.Name, targetSymbol.FilePath);
			return null;
		}
	}

	/// <summary>
	/// Legacy compatibility - crawls directory and returns symbols as list
	/// </summary>
	public async Task<List<CodeSymbol>> CrawlDir(string lang, string dirpath) {
		CodeMap codeMap = await CrawlDir(lang, dirpath, null);
		return codeMap.ToList();
	}

	public async Task<CodeMap> CrawlDir(string lang, string dirpath, CodeMap? codeMap = null) {
		codeMap ??= CodeMap.Create();

		try {
			List<string> sourceFiles = Directory.GetFiles(dirpath, "*.*", SearchOption.AllDirectories)
				.Where(f => LangUtil.IsSourceFileForLanguage(f, lang))
				.Where(f => !ProjectExclusions.ShouldExclude(f, dirpath, lang))
				.ToList();

			_logger.LogDebug("Found {Count} {Language} files to parse", sourceFiles.Count, lang);

			ConcurrentDictionary<string, List<CodeSymbol>> results = new ConcurrentDictionary<string, List<CodeSymbol>>();
			ParallelOptions                     options = new ParallelOptions { MaxDegreeOfParallelism = _maxDegreeOfParallelism };

			await Parallel.ForEachAsync(sourceFiles, options, async (filePath, ct) => {
				try {
					List<CodeSymbol> fileSymbols = await ExtractSymbolsFromFile(filePath, lang);
					results[filePath] = fileSymbols;
				} catch (Exception ex) {
					_logger.LogWarning(ex, "Failed to parse file: {FilePath}", filePath);
				}
			});

			foreach (string filePath in sourceFiles) {
				if (results.TryGetValue(filePath, out List<CodeSymbol>? fileSymbols)) {
					codeMap.AddSymbols(fileSymbols);
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

    private static readonly HashSet<string> s_missingGrammars = new(StringComparer.OrdinalIgnoreCase);

    private async Task<List<CodeSymbol>> ExtractSymbolsFromFile(string filePath, string language) {
        List<CodeSymbol> symbols = [];

        try {
            string content = await File.ReadAllTextAsync(filePath);

            // Get the TreeSitter language configuration
            if (_languageConfigs.TryGetValue(language, out TreeSitterLanguageConfig? config)) {
                if (s_missingGrammars.Contains(config.Language)) {
                    // Grammar previously detected as missing; skip quietly
                    return symbols;
                }

                try {
                    using Parser parser = new Parser(config.Language);
                    symbols = parser.Parse(content, filePath);
                    _logger.LogDebug("Extracted {Count} symbols from {FilePath} using TreeSitter", symbols.Count, filePath);
                } catch (DllNotFoundException dllEx) {
                    // Native parser library for this language is not available. Remember and downgrade noise.
                    s_missingGrammars.Add(config.Language);
                    _logger.LogWarning(dllEx, "Tree-sitter grammar not found for language '{Language}'. Skipping files for this language.", config.Language);
                }
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
				// Use hyphenated id to match native lib name: libtree-sitter-c-sharp.so
				Language = "c-sharp"
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

	private static int GetMaxDegreeOfParallelism() {
		string? env = Environment.GetEnvironmentVariable("THAUM_TREESITTER_DOP");
		if (!string.IsNullOrWhiteSpace(env) && int.TryParse(env, out int dop) && dop > 0) {
			return dop;
		}
		return Math.Max(1, Environment.ProcessorCount);
	}

	public class Parser : IDisposable {
		private readonly TreeSitter.Parser _parser;
		private readonly Language          _language;
		private readonly ILogger<Parser>   _logger;

		public Parser(string language) {
			_logger                 = Logging.For<Parser>();
			(string lib, string fn) = ResolveLanguageBinding(language);
			_language               = new Language(lib, fn);
			_parser                 = new TreeSitter.Parser(_language);
			_languageId             = language.ToLowerInvariant();
		}

		public List<CodeSymbol> Parse(string sourceCode, string filePath) {
			List<CodeSymbol> symbols = new List<CodeSymbol>();
			using Tree?      tree    = _parser.Parse(sourceCode);
			string          qtext   = GetQueryForLanguage(_languageId);
			using Query     query   = new Query(_language, qtext);
			List<QueryMatch> matches = query.Execute(tree.RootNode).Matches.ToList();

			foreach (QueryMatch match in matches) {
				Node? nameNode = null;
				Node? bodyNode = null;

				foreach (QueryCapture capture in match.Captures) {
					if (capture.Name.EndsWith(".name")) {
						nameNode = capture.Node;
					} else if (capture.Name.EndsWith(".body")) {
						bodyNode = capture.Node;
					}
				}

				if (nameNode != null && bodyNode != null) {
					string captureName = match.Captures.First(c => c.Name.EndsWith(".name")).Name;
					SymbolKind symbolKind  = GetSymbolKind(captureName);

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

		private static (string library, string function) ResolveLanguageBinding(string id) {
			string lid = id.ToLowerInvariant();
			string lib = $"tree-sitter-{lid}";               // native library name uses hyphens
			string fn  = $"tree_sitter_{lid.Replace('-', '_')}"; // exported function uses underscores
			return (lib, fn);
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

		private static string GetQueryForLanguage(string id) {
			switch (id) {
				case "c-sharp":
					return TreeSitterQueries.UniversalQuery;
				case "python":
					return @"
(function_definition name: (identifier) @function.name) @function.body
(class_definition name: (identifier) @class.name) @class.body
";
				case "javascript":
				case "typescript":
					return @"
(function_declaration name: (identifier) @function.name) @function.body
(method_definition name: (property_identifier) @method.name) @method.body
(class_declaration name: (identifier) @class.name) @class.body
";
				case "rust":
					return @"
(function_item name: (identifier) @function.name) @function.body
(struct_item name: (type_identifier) @class.name) @class.body
(enum_item name: (type_identifier) @enum.name) @enum.body
";
				case "go":
					return @"
(function_declaration name: (identifier) @function.name) @function.body
(method_declaration name: (field_identifier) @method.name) @method.body
(type_declaration (type_spec name: (type_identifier) @class.name)) @class.body
";
				default:
					return @"
(function_declaration name: (identifier) @function.name) @function.body
(class_declaration name: (identifier) @class.name) @class.body
";
			}
		}

		public void Dispose() {
			_parser.Dispose();
		}

		private readonly string _languageId;
	}
}
