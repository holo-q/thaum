using Thaum.Core.Models;
using Thaum.Core.Services;
using static Thaum.Core.Utils.ScopeTracer;
using static Thaum.Core.Utils.TraceLogger;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Thaum.CLI.Interactive;


public class TryTUI : TUIView {
	private string           _filePath   = "";
	private string           _symbolName = "";
	private string?          _customPrompt;
	private ILanguageServer? _languageServer;
	private ILogger?         _logger;

	public void Initialize(Terminal.Gui.View container) {
		// Store parameters passed via TuiConfiguration
		// The actual TextView setup is handled by InteractiveTuiHost
	}

	public void SetParameters(Dictionary<string, object> parameters) {
		if (parameters.TryGetValue("filePath", out var fp)) _filePath              = (string)fp;
		if (parameters.TryGetValue("symbolName", out var sn)) _symbolName          = (string)sn;
		if (parameters.TryGetValue("customPrompt", out var cp)) _customPrompt      = (string)cp;
		if (parameters.TryGetValue("languageServer", out var lsm)) _languageServer = (ILanguageServer)lsm;
		if (parameters.TryGetValue("logger", out var log)) _logger                 = (ILogger)log;
	}

	public async Task RefreshAsync(Action<string> textCallback, Action<string> statusCallback) {
		tracein(parameters: new { _filePath, _symbolName, _customPrompt });
		trace("=== TryInteractiveView RefreshAsync STARTED ===");

		using var scope = trace_scope("TryInteractiveView.RefreshAsync");

		try {
			if (_languageServer == null) {
				textCallback("Language server not initialized");
				statusCallback("Error");
				return;
			}

			// Start language server
			trace("Detecting language for language server startup");
			statusCallback("Detecting language...");
			textCallback("Detecting language...");

			string language = DetectLanguage(Path.GetDirectoryName(_filePath) ?? Directory.GetCurrentDirectory());
			trace($"Detected language: {language}");

			traceop($"Starting {language} language server");
			statusCallback("Starting language server...");
			textCallback($"Starting {language} language server...");

			bool started = await _languageServer.StartLanguageServerAsync(language, Path.GetDirectoryName(_filePath) ?? Directory.GetCurrentDirectory());

			if (!started) {
				trace($"Failed to start {language} language server");
				statusCallback("Language server failed");
				textCallback($"Failed to start {language} language server");
				traceout();
				return;
			}

			trace($"{language} language server started successfully");
			statusCallback("Loading symbols...");
			textCallback("Loading symbols...");

			// Get symbols from file with timeout
			trace($"Parsing symbols from file: {_filePath}");
			List<CodeSymbol> symbols;
			using var        cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			try {
				symbols = await _languageServer.GetDocumentSymbolsAsync(language, _filePath).WaitAsync(cts.Token);
				trace($"Successfully parsed {symbols.Count} symbols from file");
			} catch (OperationCanceledException) {
				trace("Symbol parsing timed out");
				statusCallback("Symbol parsing timeout");
				textCallback("Symbol parsing timed out. The file might be too large or contain problematic syntax.");
				traceout();
				return;
			}

			trace($"Searching for target symbol: {_symbolName}");
			CodeSymbol? targetSymbol = symbols.FirstOrDefault(s => s.Name == _symbolName);

			if (targetSymbol == null) {
				trace($"Target symbol '{_symbolName}' not found. Available symbols: {symbols.Count}");
				var availableSymbols = string.Join("\n", symbols.OrderBy(s => s.Name).Select(s => $"  {s.Name} ({s.Kind})"));
				statusCallback("Symbol not found");
				textCallback($"Symbol '{_symbolName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), _filePath)}\n\nAvailable symbols:\n{availableSymbols}");
				traceout();
				return;
			}

			// Build output with simple placeholder content
			var output = new System.Text.StringBuilder();

			output.AppendLine($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), _filePath)}::{_symbolName}");
			if (!string.IsNullOrEmpty(_customPrompt)) {
				output.AppendLine($"Using prompt: {_customPrompt}");
			}
			output.AppendLine();
			output.AppendLine("═══ SYMBOL FOUND ═══");
			output.AppendLine($"Symbol: {targetSymbol.Name}");
			output.AppendLine($"Kind: {targetSymbol.Kind}");
			output.AppendLine($"File: {targetSymbol.FilePath}");
			output.AppendLine($"Position: {targetSymbol.StartPosition.Line}:{targetSymbol.StartPosition.Character}");
			output.AppendLine();
			output.AppendLine("═══ INTERACTIVE TUI REFACTOR COMPLETE ═══");
			output.AppendLine("This is the new TryInteractiveView working with the InteractiveTuiHost framework!");
			output.AppendLine("The refactor has successfully extracted:");
			output.AppendLine("- Models (HierarchyNode, LsOptions, etc.)");
			output.AppendLine("- Interactive TUI framework (reusable for any command)");
			output.AppendLine("- Command handlers (TryCommands, AssemblyCommands)");
			output.AppendLine();
			output.AppendLine("Ready for further development!");

			trace("Updating final UI with results");
			statusCallback("Complete - Ready for retry");
			textCallback(output.ToString());
			traceop("TryInteractiveView RefreshAsync completed successfully");

		} catch (Exception ex) {
			trace($"Exception occurred during prompt test: {ex.Message}");
			trace($"Exception stack trace: {ex.StackTrace}");
			statusCallback("Error occurred");
			textCallback($"Error during prompt test: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
		}

		traceout();
	}

	private static string DetectLanguage(string projectPath) {
		// Simple language detection based on file patterns
		if (Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any() ||
		    Directory.GetFiles(projectPath, "*.cs", SearchOption.TopDirectoryOnly).Any()) {
			return "csharp";
		}
		if (Directory.GetFiles(projectPath, "package.json", SearchOption.TopDirectoryOnly).Any()) {
			return "typescript";
		}
		return "csharp"; // Default fallback
	}

	public void Dispose() {
		// Cleanup if needed
	}
}