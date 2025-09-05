using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Extensions.Logging;
using Thaum.CLI.Interactive;
using Thaum.Core.Services;
using Thaum.Core.Models;
using static System.Console;
using static Thaum.Core.Utils.ScopeTracer;
using static Thaum.Core.Utils.TraceLogger;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Thaum.CLI.Commands;

public class TryCommands {
	private readonly ILogger         _logger;
	private readonly ILanguageServer _languageServerManager;

	public TryCommands(ILogger logger, ILanguageServer languageServerManager) {
		_logger                = logger;
		_languageServerManager = languageServerManager;
	}

	public async Task HandleTryCommand(string[] args) {
		tracein(parameters: new { args = string.Join(" ", args) });

		using var scope = trace_scope("HandleTryCommand");

		if (args.Length < 3) {
			trace("Insufficient arguments provided");
			WriteLine("Usage: thaum try <file_path> <symbol_name> [--prompt <prompt_name>] [--interactive] [--n <rollout_count>]");
			WriteLine();
			WriteLine("Examples:");
			WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy");
			WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt compress_function_v5");
			WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --interactive");
			WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --n 5");
			WriteLine("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt compress_function_v5 --n 3");
			traceout();
			return;
		}

		string filePath   = args[1];
		string symbolName = args[2];

		trace($"Parsed arguments: filePath='{filePath}', symbolName='{symbolName}'");

		// Parse options
		string? customPrompt = null;
		bool    interactive  = false;
		int     rolloutCount = 1;

		for (int i = 3; i < args.Length; i++) {
			switch (args[i]) {
				case "--prompt" when i + 1 < args.Length:
					customPrompt = args[++i];
					trace($"Custom prompt specified: {customPrompt}");
					break;
				case "--interactive":
					interactive = true;
					trace("Interactive mode enabled");
					break;
				case "--n" when i + 1 < args.Length:
					if (int.TryParse(args[++i], out rolloutCount) && rolloutCount > 0) {
						trace($"Multiple rollouts specified: {rolloutCount}");
					} else {
						WriteLine("Error: --n requires a positive integer value");
						traceout();
						return;
					}
					break;
			}
		}

		// Make file path absolute
		if (!Path.IsPathRooted(filePath)) {
			string originalPath = filePath;
			filePath = Path.Combine(Directory.GetCurrentDirectory(), filePath);
			trace($"Converted relative path '{originalPath}' to absolute: '{filePath}'");
		}

		if (interactive) {
			await RunInteractiveTry(filePath, symbolName, customPrompt);
		} else {
			await RunNonInteractiveTry(filePath, symbolName, customPrompt, rolloutCount);
		}

		traceout();
	}

	private async Task RunInteractiveTry(string filePath, string symbolName, string? customPrompt) {
		trace("Initializing TraceLogger for interactive mode");

		// Re-initialize TraceLogger for interactive mode with file output
		Dispose();
		Initialize(_logger, isInteractiveMode: true);

		// Completely disable console logging during TUI to prevent interference
		var originalLogger = Log.Logger;
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.File("output.log",
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
				flushToDiskInterval: TimeSpan.FromMilliseconds(500))
			.WriteTo.Seq("http://localhost:5341")
			.CreateLogger();

		// Also redirect Console.WriteLine to suppress any other console output
		var originalOut   = Out;
		var originalError = Error;
		SetOut(TextWriter.Null);
		SetError(TextWriter.Null);

		try {
			trace("Starting interactive mode");

			// Setup TUI configuration
			var config = new TUIConfig();

			// Set up file watcher if custom prompt is provided
			if (!string.IsNullOrEmpty(customPrompt)) {
				string promptsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "prompts");
				string promptFilePath   = Path.Combine(promptsDirectory, $"{customPrompt}.md");
				if (File.Exists(promptFilePath)) {
					config.WatchFilePath = promptFilePath;
				}
			}

			// Set parameters for the TUI view
			config.Parameters = new Dictionary<string, object> {
				["filePath"]       = filePath,
				["symbolName"]     = symbolName,
				["customPrompt"]   = customPrompt ?? "",
				["languageServer"] = _languageServerManager
			};

			// Run the interactive TUI
			var tuiHost = new TUIHost<TryTUI>(_logger);
			await tuiHost.RunAsync(config);
		} finally {
			// Restore original logger and console output
			Log.Logger = originalLogger;
			SetOut(originalOut);
			SetError(originalError);
		}
	}

	private async Task RunNonInteractiveTry(string filePath, string symbolName, string? customPrompt, int rolloutCount) {
		trace("Starting non-interactive mode");

		if (!File.Exists(filePath)) {
			WriteLine($"Error: File not found: {filePath}");
			return;
		}

		try {
			WriteLine($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}::{symbolName}");
			WriteLine(customPrompt != null ? $"Custom Prompt: {customPrompt}" : "Using default prompt from environment configuration");
			WriteLine();

			// Start language server
			string language = DetectLanguage(Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			bool   started  = await _languageServerManager.StartLanguageServerAsync(language, Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
			if (!started) {
				WriteLine($"Failed to start {language} language server");
				Environment.Exit(1);
			}

			// Get symbols from file
			List<CodeSymbol> symbols      = await _languageServerManager.GetDocumentSymbolsAsync(language, filePath);
			CodeSymbol?      targetSymbol = symbols.FirstOrDefault(s => s.Name == symbolName);

			if (targetSymbol == null) {
				WriteLine($"Symbol '{symbolName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), filePath)}");
				WriteLine();
				WriteLine("Available symbols:");
				foreach (CodeSymbol sym in symbols.OrderBy(s => s.Name)) {
					WriteLine($"  {sym.Name} ({sym.Kind})");
				}
				return;
			}

			WriteLine($"Found symbol: {targetSymbol.Name} ({targetSymbol.Kind})");
			WriteLine();

			// Get source code
			string sourceCode = await GetSymbolSourceCode(targetSymbol);
			if (string.IsNullOrEmpty(sourceCode)) {
				WriteLine("Failed to extract source code for symbol");
				return;
			}

			// Determine prompt name with environment variable support
			string promptName = customPrompt ?? GetDefaultPromptFromEnvironment(targetSymbol);

			if (rolloutCount == 1) {
				// Single compression
				WriteLine($"Using prompt: {promptName}");
				WriteLine();
				await RunSingleCompression(sourceCode, promptName, targetSymbol);
			} else {
				// Multiple rollouts with fusion
				WriteLine($"Multiple rollouts ({rolloutCount}) with fusion");
				WriteLine($"Using prompt: {promptName}");
				WriteLine();
				await RunMultipleRolloutsWithFusion(sourceCode, promptName, targetSymbol, rolloutCount);
			}
		} catch (Exception ex) {
			WriteLine($"Error during prompt test: {ex.Message}");
			Environment.Exit(1);
		}
	}

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
		try {
			string[] lines     = await File.ReadAllLinesAsync(targetSymbol.FilePath);
			int      startLine = Math.Max(0, targetSymbol.StartPosition.Line);
			int      endLine   = Math.Min(lines.Length - 1, targetSymbol.EndPosition.Line);

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
		return targetSymbol.Kind == SymbolKind.Function || targetSymbol.Kind == SymbolKind.Method
			? "compress_function_v2"
			: "compress_class";
	}

	private async Task RunSingleCompression(string sourceCode, string promptName, CodeSymbol targetSymbol) {
		// Build context (simplified for testing)
		OptimizationContext context = new OptimizationContext(
			Level: targetSymbol.Kind == SymbolKind.Function || targetSymbol.Kind == SymbolKind.Method ? 1 : 2,
			AvailableKeys: new List<string>(),          // No keys for testing
			CompressionLevel: CompressionLevel.Compress // Not used anymore, just for compatibility
		);

		// Build prompt directly
		string prompt = await BuildCustomPromptAsync(promptName, targetSymbol, context, sourceCode);

		WriteLine("═══ GENERATED PROMPT ═══");
		WriteLine(prompt);
		WriteLine();
		WriteLine("═══ TESTING LLM RESPONSE ═══");

		// Get model from configuration
		string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
		               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

		// Setup services
		IConfigurationRoot configuration = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: true)
			.AddEnvironmentVariables()
			.Build();

		HttpClient     httpClient    = new HttpClient();
		ILoggerFactory loggerFactory = new SerilogLoggerFactory();
		HttpLLM        llmProvider   = new HttpLLM(httpClient, configuration, loggerFactory.CreateLogger<HttpLLM>());

		// Stream response
		IAsyncEnumerable<string> streamResponse = await llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));

		await foreach (string token in streamResponse) {
			Write(token);
		}
		WriteLine();
		WriteLine();
		WriteLine("═══ TEST COMPLETE ═══");
	}

	private async Task RunSingleCompression(string sourceCode, string promptName) {
		string promptContent = await LoadPrompt(promptName);
		if (string.IsNullOrEmpty(promptContent)) {
			WriteLine($"Error: Could not load prompt '{promptName}'");
			return;
		}

		// Replace placeholder with actual source code
		string finalPrompt = promptContent.Replace("{sourceCode}", sourceCode);

		// Placeholder for LLM call - in full implementation would use _languageServerManager or similar
		WriteLine("═══ SINGLE COMPRESSION OUTPUT ═══");
		WriteLine($"[Would send to LLM: {finalPrompt.Length} characters]");
		WriteLine("TOPOLOGY: [Compressed structural representation]");
		WriteLine("MORPHISM: [Compressed semantic representation]");
		WriteLine("POLICY: [Compressed growth policy]");
		WriteLine("MANIFEST: [Resurrection formula]");
	}

	private static async Task<string> BuildCustomPromptAsync(string promptName, CodeSymbol symbol, OptimizationContext context, string sourceCode) {
		Dictionary<string, object> parameters = new Dictionary<string, object> {
			["sourceCode"] = sourceCode,
			["symbolName"] = symbol.Name,
			["availableKeys"] = context.AvailableKeys.Any()
				? string.Join("\n", context.AvailableKeys.Select(k => $"- {k}"))
				: "None"
		};

		ILoggerFactory loggerFactory = new SerilogLoggerFactory();
		PromptLoader   promptLoader  = new PromptLoader(loggerFactory.CreateLogger<PromptLoader>());

		return await promptLoader.FormatPromptAsync(promptName, parameters);
	}

	private async Task RunMultipleRolloutsWithFusion(string sourceCode, string promptName, CodeSymbol targetSymbol, int rolloutCount) {
		var representations = new List<string>();

		// Add original source code as holographic reference
		representations.Add($"<ORIGINAL_SOURCE>\n{sourceCode}\n</ORIGINAL_SOURCE>");

		// Run multiple compression rollouts
		for (int i = 1; i <= rolloutCount; i++) {
			WriteLine($"Running rollout {i}/{rolloutCount}...");
			string rolloutResult = await RunCompressionRollout(sourceCode, promptName, targetSymbol, i);
			representations.Add($"<COMPRESSION_ROLLOUT_{i}>\n{rolloutResult}\n</COMPRESSION_ROLLOUT_{i}>");
		}

		// Load fusion prompt and apply
		string fusionPrompt = await LoadPrompt("fusion_v1");
		if (string.IsNullOrEmpty(fusionPrompt)) {
			WriteLine("Error: Could not load fusion_v1 prompt");
			return;
		}

		// Combine all representations for fusion
		string allRepresentations = string.Join("\n\n", representations);
		string finalFusionPrompt  = fusionPrompt.Replace("{representations}", allRepresentations);

		WriteLine("═══ FUSION OUTPUT ═══");
		WriteLine($"[Fusing {rolloutCount} rollouts + original source]");

		// Actually call LLM with fusion prompt
		await CallLLMWithPrompt(finalFusionPrompt, "FUSION");
	}

	private async Task<string> RunCompressionRollout(string sourceCode, string promptName, CodeSymbol targetSymbol, int rolloutNumber) {
		// Build context (simplified for testing)
		OptimizationContext context = new OptimizationContext(
			Level: targetSymbol.Kind == SymbolKind.Function || targetSymbol.Kind == SymbolKind.Method ? 1 : 2,
			AvailableKeys: new List<string>(),          // No keys for testing
			CompressionLevel: CompressionLevel.Compress // Not used anymore, just for compatibility
		);

		// Build prompt directly
		string prompt = await BuildCustomPromptAsync(promptName, targetSymbol, context, sourceCode);

		// Call LLM and capture output
		var output = new System.Text.StringBuilder();
		await CallLLMWithPrompt(prompt, $"ROLLOUT_{rolloutNumber}", output);

		return output.ToString();
	}

	private async Task CallLLMWithPrompt(string prompt, string title, System.Text.StringBuilder? captureOutput = null) {
		WriteLine($"═══ {title} OUTPUT ═══");

		try {
			// Get model from configuration
			string model = Environment.GetEnvironmentVariable("LLM__DefaultModel") ??
			               throw new InvalidOperationException("LLM__DefaultModel environment variable is required");

			// Setup services
			IConfigurationRoot configuration = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: true)
				.AddEnvironmentVariables()
				.Build();

			HttpClient     httpClient    = new HttpClient();
			ILoggerFactory loggerFactory = new SerilogLoggerFactory();
			HttpLLM        llmProvider   = new HttpLLM(httpClient, configuration, loggerFactory.CreateLogger<HttpLLM>());

			// Stream response
			IAsyncEnumerable<string> streamResponse = await llmProvider.StreamCompleteAsync(prompt, new LlmOptions(Temperature: 0.3, MaxTokens: 1024, Model: model));

			await foreach (string token in streamResponse) {
				Write(token);
				captureOutput?.Append(token);
			}
			WriteLine();
			WriteLine();
		} catch (Exception ex) {
			WriteLine($"Error calling LLM: {ex.Message}");
		}
	}

	private static async Task<string> LoadPrompt(string promptName) {
		ILoggerFactory loggerFactory = new SerilogLoggerFactory();
		PromptLoader   promptLoader  = new PromptLoader(loggerFactory.CreateLogger<PromptLoader>());

		try {
			var parameters = new Dictionary<string, object>();
			return await promptLoader.FormatPromptAsync(promptName, parameters);
		} catch (Exception ex) {
			trace($"Error loading prompt: {ex.Message}");
			return "";
		}
	}
}