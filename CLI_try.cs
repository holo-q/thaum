using Thaum.CLI.Interactive;
using Thaum.Core;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;
using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

public partial class CLI {
	public async Task CMD_try(string[] args) {
		await HandleTryCommand(args);
	}

	public async Task CMD_try_lsp(string[] args) {
		bool showAll = args.Contains("--all") || args.Contains("-a");
		bool cleanup = args.Contains("--cleanup") || args.Contains("-c");
		println("🔧 Thaum LSP Server Management");
		println("==============================");
		println();

		try {
			LSPDownloader downloader = new LSPDownloader();

			if (cleanup) {
				println("🧹 Cleaning up old LSP server installations...");
				await downloader.CleanupOldServersAsync();
				println("✅ Cleanup complete!");
				return;
			}

			string cacheDir = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Thaum",
				"lsp-servers"
			);
			println($"📁 Cache Directory: {cacheDir}");
			println();

			if (!Directory.Exists(cacheDir)) {
				println("No LSP servers cached yet.");
				println("Run 'dotnet run -- ls <project> --lang <language>' to download servers.");
				return;
			}

			string[] languages = Directory.GetDirectories(cacheDir);
			if (!languages.Any()) {
				println("No LSP servers cached yet.");
				return;
			}
			println("🌐 Cached LSP Servers:");
			println();

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
				println($" (v{version.Trim()}) - Installed: {installDate}");

				if (showAll) {
					string[] files     = Directory.GetFiles(lang, "*", SearchOption.AllDirectories);
					long     totalSize = files.Sum(f => new FileInfo(f).Length);
					println($"      Size: {totalSize / 1024 / 1024:F1} MB");
					println($"      Files: {files.Length}");
					println($"      Path: {lang}");
					println();
				}
			}

			if (!showAll) {
				println();
				println("💡 Use --all to see detailed information");
				println("💡 Use --cleanup to remove old versions");
			}
		} catch (Exception ex) {
			ForegroundColor = ConsoleColor.Red;
			println($"❌ Error: {ex.Message}");
			ResetColor();
		}
	}

	public async Task HandleTryCommand(string[] args) {
		tracein(parameters: new { args = string.Join(" ", args) });

		using var scope = trace_scope("HandleTryCommand");

		if (args.Length < 2) {
			trace("Insufficient arguments provided");
			println("Usage: thaum try <file_path>::<symbol_name> [--prompt <prompt_name>] [--interactive] [--n <rollout_count>]");
			println();
			println("Examples:");
			println("  thaum try CLI/CliApplication.cs::BuildHierarchy");
			println("  thaum try CLI/CliApplication.cs::BuildHierarchy --prompt compress_function_v5");
			println("  thaum try CLI/CliApplication.cs::BuildHierarchy --interactive");
			println("  thaum try CLI/CliApplication.cs::BuildHierarchy --n 5");
			println("  thaum try CLI/CliApplication.cs::BuildHierarchy --prompt compress_function_v5 --n 3");
			traceout();
			return;
		}

		// Parse file::symbol syntax
		string pathSpec = args[1];
		if (!pathSpec.Contains("::")) {
			trace("Invalid path specification: missing '::' separator");
			println("Error: Path must be specified as <file_path>::<symbol_name>");
			println("Example: CLI/CliApplication.cs::BuildHierarchy");
			traceout();
			return;
		}

		string[] pathParts = pathSpec.Split("::", 2);
		string filePath   = pathParts[0];
		string symbolName = pathParts[1];

		trace($"Parsed arguments: filePath='{filePath}', symbolName='{symbolName}'");

		// Parse options
		// ----------------------------------------
		string? customPrompt = null;
		bool    interactive  = false;
		int     rolloutCount = 1;

		for (int i = 2; i < args.Length; i++) {
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
						println("Error: --n requires a positive integer value");
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
				string promptsDirectory = GLB.PromptsDir;
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
			println($"Error: File not found: {filepath}");
			return;
		}

		try {
			println($"Testing prompt on: {Path.GetRelativePath(Directory.GetCurrentDirectory(), filepath)}::{targetName}");
			println(customPrompt != null ? $"Custom Prompt: {customPrompt}" : "Using default prompt from environment configuration");
			println();

			// Get symbols from file
			var codeMap = await _crawler.CrawlFile(filepath);
			CodeSymbol? targetSymbol = codeMap.GetSymbolByName(targetName);

			if (targetSymbol == null) {
				println($"Symbol '{targetName}' not found in {Path.GetRelativePath(Directory.GetCurrentDirectory(), filepath)}");
				println();
				println("Available symbols:");
				foreach (CodeSymbol sym in codeMap.OrderBy(s => s.Name)) {
					println($"  {sym.Name} ({sym.Kind})");
				}
				return;
			}
			println($"Found symbol: {targetSymbol.Name} ({targetSymbol.Kind})");
			println();

			// Get source code
			string src = await _crawler.GetCode(targetSymbol);
			if (string.IsNullOrEmpty(src)) {
				println("Failed to extract source code for symbol");
				return;
			}

			// Determine prompt name with environment variable support
			string promptName = customPrompt ?? GLB.GetDefaultPrompt(targetSymbol);

			if (nRollouts == 1) {
				// Single compression
				println($"Using prompt: {promptName}");
				println();
				Prompter prompter = new Prompter(new HttpLLM(new HttpClient(), GLB.AppConfig));
				await prompter.Compress(src, promptName, targetSymbol);
			} else {
				// Multiple rollouts with fusion
				println($"Multiple rollouts ({nRollouts}) with fusion");
				println($"Using prompt: {promptName}");
				println();
				await _prompter.CompressWithFusion(src, promptName, targetSymbol, nRollouts);
			}
		} catch (Exception ex) {
			println($"Error during prompt test: {ex.Message}");
			Environment.Exit(1);
		}
	}

	public async Task CMD_try(string filePath, string symbolName, string? promptName, bool interactive, int nRollouts) {
		trace($"Executing try command: {filePath}::{symbolName}, prompt: {promptName}, interactive: {interactive}, n: {nRollouts}");
		
		// Build args for legacy method and delegate
		List<string> args = ["try", filePath, symbolName];
		if (!string.IsNullOrEmpty(promptName)) args.AddRange(["--prompt", promptName]);
		if (interactive) args.Add("--interactive");
		if (nRollouts != 1) args.AddRange(["--n", nRollouts.ToString()]);
		
		await CMD_try(args.ToArray());
	}

	public async Task CMD_try_lsp(bool showAll, bool cleanup) {
		trace($"Executing ls-lsp command with showAll: {showAll}, cleanup: {cleanup}");
		List<string> args = ["ls-lsp"];
		if (showAll) args.AddRange(["--all"]);
		if (cleanup) args.AddRange(["--cleanup"]);
		await CMD_try_lsp(args.ToArray());
	}
}