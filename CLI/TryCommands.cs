using Thaum.Core.Models;
using static System.Console;
using static Thaum.Core.Utils.TraceLogger;

namespace Thaum.CLI.Commands;

public static class TryCommands {
	private static string DetectLanguage(string directoryPath) {
		// Simple language detection based on file extensions in directory
		string[] files = Directory.GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);

		if (files.Any(f => f.EndsWith(".cs"))) return "c-sharp";
		if (files.Any(f => f.EndsWith(".rs"))) return "rust";
		if (files.Any(f => f.EndsWith(".go"))) return "go";
		if (files.Any(f => f.EndsWith(".py"))) return "python";
		if (files.Any(f => f.EndsWith(".js") || f.EndsWith(".ts"))) return "typescript";

		return "c-sharp"; // Default
	}

	private static async Task<string> GetSymbolSourceCode(CodeSymbol targetSymbol) {
		// TODO this is wrong, it should be done through the LSP interface
		try {
			string[] lines     = await File.ReadAllLinesAsync(targetSymbol.FilePath);
			int      startLine = Math.Max(0, targetSymbol.StartCodeLoc.Line);
			int      endLine   = Math.Min(lines.Length - 1, targetSymbol.EndCodeLoc.Line);

			WriteLine($"Debug: StartLine={startLine}, EndLine={endLine}, TotalLines={lines.Length}");

			if (startLine >= 0 && endLine >= startLine && endLine < lines.Length) {
				WriteLine($"Debug: StartLine content: '{lines[startLine].Trim()}'");
				WriteLine($"Debug: EndLine content: '{lines[endLine].Trim()}'");

				var symbolLines = lines.Skip(startLine).Take(endLine - startLine + 1);
				return string.Join("\n", symbolLines);
			}

			return "";
		} catch (Exception ex) {
			trace($"Error extracting symbol source: {ex.Message}");
			return "";
		}
	}

	private static string GetDefaultPromptFromEnvironment(CodeSymbol targetSymbol) {
		string? envPrompt = Environment.GetEnvironmentVariable("THAUM_DEFAULT_PROMPT");
		if (!string.IsNullOrEmpty(envPrompt)) {
			return envPrompt;
		}

		// Default based on symbol type
		return targetSymbol.Kind is SymbolKind.Function or SymbolKind.Method
			? "compress_function_v2"
			: "compress_class";
	}





}