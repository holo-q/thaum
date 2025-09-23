using Microsoft.Extensions.Logging;

namespace Thaum.Core.Crawling;

// Simplified LSP client manager for initial implementation
public class RegexCrawler : Crawler {
	private readonly ILogger<RegexCrawler>? _logger;
	private readonly string                 _lang;

	public RegexCrawler(string lang) {
		this._lang = lang;
	}

	public override async Task<CodeMap> CrawlDir(string dirpath, CodeMap? codeMap = null) {
		codeMap ??= CodeMap.Create();

		try {
			List<string> sourceFiles = Directory.GetFiles(dirpath, "*.*", SearchOption.AllDirectories)
				.Where(f => IsSourceFileForLanguage(f, _lang))
				.Take(20) // Limit for performance
				.ToList();

			foreach (string file in sourceFiles) {
				List<CodeSymbol> fileSymbols = await ExtractSymbol(file);
				codeMap.AddSymbols(fileSymbols);
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error extracting workspace symbols");
		}

		return codeMap;
	}

	public override async Task<CodeMap> CrawlFile(string filepath, CodeMap? codeMap = null) {
		codeMap ??= CodeMap.Create();
		if (!File.Exists(filepath)) return codeMap;
		List<CodeSymbol> symbols = await ExtractSymbol(filepath);
		codeMap.AddSymbols(symbols);
		return codeMap;
	}

	public override async Task<CodeSymbol?> GetDefinitionFor(string name, CodeLoc location) {
		throw new InvalidOperationException("The regex crawler does not support definitions. LSPs are required for this.");
	}

	public override async Task<List<CodeSymbol>> GetReferencesFor(string name, CodeLoc location) {
		throw new InvalidOperationException("The regex crawler does not support reference crawling. LSPs are required for this.");
	}

	public override Task<string?> GetCode(CodeSymbol targetSymbol) => throw new NotImplementedException();

	private async Task<List<CodeSymbol>> ExtractSymbol(string filePath) {
		List<CodeSymbol> symbols = [];

		try {
			string   content = await File.ReadAllTextAsync(filePath);
			string[] lines   = content.Split('\n');

			// Simple pattern-based extraction based on language
			switch (_lang.ToLowerInvariant()) {
				case "python":
					ExtractPythonSymbols(symbols, lines, filePath);
					break;
				case "c-sharp":
					ExtractCSharpSymbols(symbols, lines, filePath);
					break;
				default:
					ExtractGenericSymbols(symbols, lines, filePath);
					break;
			}
		} catch (Exception ex) {
			_logger.LogError(ex, "Error extracting symbols from {FilePath}", filePath);
		}

		return symbols;
	}

	private void ExtractPythonSymbols(List<CodeSymbol> symbols, string[] lines, string filePath) {
		for (int i = 0; i < lines.Length; i++) {
			string line = lines[i].Trim();

			if (line.StartsWith("def ") && line.Contains('(')) {
				string name = ExtractFunctionName(line, "def ");
				symbols.Add(new CodeSymbol(
					Name: name,
					Kind: SymbolKind.Function,
					FilePath: filePath,
					StartCodeLoc: new CodeLoc(i, 0),
					EndCodeLoc: new CodeLoc(i, line.Length)
				));
			} else if (line.StartsWith("class ") && line.Contains(':')) {
				string name = ExtractClassName(line, "class ");
				symbols.Add(new CodeSymbol(
					Name: name,
					Kind: SymbolKind.Class,
					FilePath: filePath,
					StartCodeLoc: new CodeLoc(i, 0),
					EndCodeLoc: new CodeLoc(i, line.Length)
				));
			}
		}
	}

	private void ExtractCSharpSymbols(List<CodeSymbol> symbols, string[] lines, string filePath) {
		for (int i = 0; i < lines.Length; i++) {
			string line = lines[i].Trim();

			// Skip comments and empty lines
			if (line.StartsWith("//") || string.IsNullOrEmpty(line))
				continue;

			// Match methods/functions with proper C# syntax (not records, properties, etc.)
			if ((line.StartsWith("public ") || line.StartsWith("private ") ||
			     line.StartsWith("protected ") || line.StartsWith("internal ")) &&
			    line.Contains('(') && line.Contains(')') &&
			    !line.Contains("class ") && !line.Contains("interface ") &&
			    !line.Contains("record ") && !line.Contains("enum ") &&
			    !line.Contains("=") &&                              // Skip property assignments
			    !line.Contains("new ") &&                           // Skip constructors called with 'new'
			    !line.Contains("get;") && !line.Contains("set;") && // Skip properties
			    !line.Contains(" => ") &&                           // Skip expression-bodied members
			    (line.Contains("void ") || line.Contains("async ") || line.Contains("Task") ||
			     line.Contains("string ") || line.Contains("int ") || line.Contains("bool ") ||
			     line.Contains("List<") || line.Contains("IAsync") || line.Contains("public static"))) // Must return something or be static
			{
				string name = ExtractMethodName(line);
				if (!string.IsNullOrEmpty(name) && name.Length > 1 &&
				    char.IsLetter(name[0])) // Must start with letter
				{
					symbols.Add(new CodeSymbol(
						Name: name,
						Kind: SymbolKind.Method,
						FilePath: filePath,
						StartCodeLoc: new CodeLoc(i, 0),
						EndCodeLoc: new CodeLoc(i, line.Length)
					));
				}
			}
			// Match class declarations
			else if ((line.StartsWith("public class ") || line.StartsWith("internal class ") ||
			          line.StartsWith("class ")) && !line.Contains("//")) {
				string name = ExtractCSharpClassName(line);
				if (!string.IsNullOrEmpty(name) && name.Length > 1 &&
				    char.IsLetter(name[0])) // Must start with letter
				{
					symbols.Add(new CodeSymbol(
						Name: name,
						Kind: SymbolKind.Class,
						FilePath: filePath,
						StartCodeLoc: new CodeLoc(i, 0),
						EndCodeLoc: new CodeLoc(i, line.Length)
					));
				}
			}
		}
	}

	private void ExtractGenericSymbols(List<CodeSymbol> symbols, string[] lines, string filePath) {
		for (int i = 0; i < lines.Length; i++) {
			string line = lines[i].Trim();

			if (line.Contains("function ") && line.Contains('(')) {
				string name = ExtractFunctionName(line, "function ");
				symbols.Add(new CodeSymbol(
					Name: name,
					Kind: SymbolKind.Function,
					FilePath: filePath,
					StartCodeLoc: new CodeLoc(i, 0),
					EndCodeLoc: new CodeLoc(i, line.Length)
				));
			}
		}
	}

	private string ExtractFunctionName(string line, string prefix) {
		int start = line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
		int end   = line.IndexOf('(', start);
		return end > start ? line[start..end].Trim() : "Unknown";
	}

	private string ExtractClassName(string line, string prefix) {
		int start          = line.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
		int end            = line.IndexOfAny([':', '(', '{'], start);
		if (end == -1) end = line.Length;
		return end > start ? line[start..end].Trim() : "Unknown";
	}

	private string ExtractMethodName(string line) {
		int openParen = line.IndexOf('(');
		if (openParen == -1) return "";

		string[] words = line[..openParen].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return words.Length > 0 ? words[^1] : "";
	}

	private string ExtractCSharpClassName(string line) {
		int classIndex = line.IndexOf("class ", StringComparison.Ordinal);
		if (classIndex == -1) return "";

		int start          = classIndex + 6;
		int end            = line.IndexOfAny([' ', ':', '{', '<'], start);
		if (end == -1) end = line.Length;

		return end > start ? line[start..end].Trim() : "";
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
}