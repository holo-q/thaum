using Thaum.CLI.Interactive;
using Thaum.Core;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;
using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

public partial class CLI {
	private async Task CMD_try(string[] args) {
		await HandleTryCommand(args);
	}

	private async Task CMD_try_lsp(string[] args) {
		bool showAll = args.Contains("--all") || args.Contains("-a");
		bool cleanup = args.Contains("--cleanup") || args.Contains("-c");
		ln("🔧 Thaum LSP Server Management");
		ln("==============================");
		ln();

		try {
			LSPDownloader downloader = new LSPDownloader();

			if (cleanup) {
				ln("🧹 Cleaning up old LSP server installations...");
				await downloader.CleanupOldServersAsync();
				ln("✅ Cleanup complete!");
				return;
			}

			string cacheDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Thaum",
				"lsp-servers"
			);
			ln($"📁 Cache Directory: {cacheDir}");
			ln();

			if (!Directory.Exists(cacheDir)) {
				ln("No LSP servers cached yet.");
				ln("Run 'dotnet run -- ls <project> --lang <language>' to download servers.");
				return;
			}

			string[] languages = Directory.GetDirectories(cacheDir);
			if (!languages.Any()) {
				ln("No LSP servers cached yet.");
				return;
			}
			ln("🌐 Cached LSP Servers:");
			ln();

			foreach (string lang in languages.OrderBy(Path.GetFileName)) {
				string langName    = Path.GetFileName(lang);
				string versionFile = Path.Combine(lang, ".version");
				string version     = "unknown";
				string installDate = "unknown";

				if (File.Exists(versionFile)) {
					version     = await File.ReadAllTextAsync(versionFile);
					installDate = File.GetCreationTime(versionFile).ToString("yyyy-MM-dd HH:mm");
				}

				ForegroundColor = ConsoleColor.Green;
				Write($"  📦 {langName.ToUpper()}");
				ResetColor();
				ln($" (v{version.Trim()}) - Installed: {installDate}");

				if (showAll) {
					string[] files     = Directory.GetFiles(lang, "*", SearchOption.AllDirectories);
					long     totalSize = files.Sum(f => new FileInfo(f).Length);
					ln($"      Size: {totalSize / 1024 / 1024:F1} MB");
					ln($"      Files: {files.Length}");
					ln($"      Path: {lang}");
					ln();
				}
			}

			if (!showAll) {
				ln();
				ln("💡 Use --all to see detailed information");
				ln("💡 Use --cleanup to remove old versions");
			}
		} catch (Exception ex) {
			ForegroundColor = ConsoleColor.Red;
			ln($"❌ Error: {ex.Message}");
			ResetColor();
		}
	}

	public async Task HandleTryCommand(string[] args) {
		tracein(parameters: new { args = string.Join(" ", args) });

		using var scope = trace_scope("HandleTryCommand");

		if (args.Length < 3) {
			trace("Insufficient arguments provided");
			ln("Usage: thaum try <file_path> <symbol_name> [--prompt <prompt_name>] [--interactive] [--n <rollout_count>]");
			ln();
			ln("Examples:");
			ln("  thaum try CLI/CliApplication.cs BuildHierarchy");
			ln("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt compress_function_v5");
			ln("  thaum try CLI/CliApplication.cs BuildHierarchy --interactive");
			ln("  thaum try CLI/CliApplication.cs BuildHierarchy --n 5");
			ln("  thaum try CLI/CliApplication.cs BuildHierarchy --prompt compress_function_v5 --n 3");
			traceout();
			return;
		}

		string filePath   = args[1];
		string symbolName = args[2];

		trace($"Parsed arguments: filePath='{filePath}', symbolName='{symbolName}'");

		// Parse options
		// ----------------------------------------
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
						ln("Error: --n requires a positive integer value");
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
			await TryTUI(filePath, symbolName, customPrompt);
		} else {
			await Try(filePath, symbolName, customPrompt, rolloutCount);
		}

		traceout();
	}

	private async Task TryTUI(string filePath, string symbolName, string? customPrompt) {
		trace("Initializing TraceLogger for interactive mode");

		// Re-initialize TraceLogger for interactive mode with file output
		Dispose();
		Initialize(_logger, isInteractiveMode: true);

		// Completely disable console logging during TUI to prevent interference
		// TODO move to logging so we can reuse setup for
		Logging.SetupTUI();

		// Also redirect println to suppress any other console output
		var originalOut   = Out;
		var originalError = Error;
		SetOut(TextWriter.Null);
		SetError(TextWriter.Null);

		try {
			trace("Starting interactive mode");
			var config = new TUIConfig {
				Parameters = new Dictionary<string, object> {
					["filePath"]       = filePath,
					["symbolName"]     = symbolName,
					["customPrompt"]   = customPrompt ?? "",
					["languageServer"] = _crawler
				}
			};

			// Set up file watcher if custom prompt is provided
			if (!string.IsNullOrEmpty(customPrompt)) {
				string promptsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "prompts");
				string promptFilePath   = Path.Combine(promptsDirectory, $"{customPrompt}.md");
				if (File.Exists(promptFilePath)) {
					config.WatchFilePath = promptFilePath;
				}
			}

			await new TUIHost<TryTUI>(_logger).RunAsync(config);
		} finally {
			SetOut(originalOut);
			SetError(originalError);
		}
	}

	private async Task Try(string filepath, string targetName, string? customPrompt, int nRollouts) {
		trace("Starting non-interactive mode");

		if (!File.Exists(filepath)) {
			ln($"Error: File not found: {filepath}");
			return;
		}

		try {
			ln($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), filepath)}::{targetName}");
			ln(customPrompt != null ? $"Custom Prompt: {customPrompt}" : "Using default prompt from environment configuration");
			ln();

			// Get symbols from file
			List<CodeSymbol> symbols      = await _crawler.CrawlFile(filepath);
			CodeSymbol?      targetSymbol = symbols.FirstOrDefault(s => s.Name == targetName);

			if (targetSymbol == null) {
				ln($"Symbol '{targetName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), filepath)}");
				ln();
				ln("Available symbols:");
				foreach (CodeSymbol sym in symbols.OrderBy(s => s.Name)) {
					ln($"  {sym.Name} ({sym.Kind})");
				}
				return;
			}
			ln($"Found symbol: {targetSymbol.Name} ({targetSymbol.Kind})");
			ln();

			// Get source code
			string src = await _crawler.GetCode(targetSymbol);
			if (string.IsNullOrEmpty(src)) {
				ln("Failed to extract source code for symbol");
				return;
			}

			// Determine prompt name with environment variable support
			string promptName = customPrompt ?? GLB.GetDefaultPromptFromEnvironment(targetSymbol);

			if (nRollouts == 1) {
				// Single compression
				ln($"Using prompt: {promptName}");
				ln();
				Prompter prompter = new Prompter(new HttpLLM(new HttpClient(), GLB.AppConfig));
				await prompter.Compress(src, promptName, targetSymbol);
			} else {
				// Multiple rollouts with fusion
				ln($"Multiple rollouts ({nRollouts}) with fusion");
				ln($"Using prompt: {promptName}");
				ln();
				await _prompter.CompressWithFusion(src, promptName, targetSymbol, nRollouts);
			}
		} catch (Exception ex) {
			ln($"Error during prompt test: {ex.Message}");
			Environment.Exit(1);
		}
	}
}