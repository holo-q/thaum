using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Core.Utils;
using Thaum.Utils;
using Thaum.Core.Triads;
using Thaum.Core.Eval;
using System.Text.Json;
using Spectre.Console;
using CoreTreeNode = Thaum.Core.Utils.TreeNode;
using static System.Console;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing ls command implementation where symbols are discovered
/// through TreeSitter or assembly reflection where hierarchical display reveals structure
/// where perceptual coloring creates semantic visual feedback
/// </summary>
public partial class CLI {
	[RequiresUnreferencedCode("Uses reflection for object serialization")]
	public async Task HandleLs(LsOptions options) {
		trace($"Executing ls command with options: {options}");

		// If a batch JSON is provided, print triads for its rows instead of symbol tree
		if (!string.IsNullOrWhiteSpace(options.BatchJson)) {
			await CMD_ls_triads_from_batch(options.ProjectPath, options.BatchJson!, options.Split);
			return;
		}

		// Handle assembly specifiers
		if (options.ProjectPath.StartsWith("assembly:")) {
			string assemblyName = options.ProjectPath[9..]; // Remove "assembly:" prefix
			await CMD_ls_dotnet(assemblyName, options);
			return;
		}

		// Handle file paths for assemblies
		if (options.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		    options.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
			if (!File.Exists(options.ProjectPath)) {
				println($"Error: Could not find assembly file: {options.ProjectPath}");
				println($"Current directory: {Directory.GetCurrentDirectory()}");
				return;
			}

			Assembly fileAssembly = Assembly.LoadFrom(options.ProjectPath);
			await CMD_ls_binary(fileAssembly, options);
			return;
		}

		// If we reach here and the path doesn't exist, list available assemblies to help debug
		if (!Directory.Exists(options.ProjectPath) && !File.Exists(options.ProjectPath)) {
			println($"Path '{options.ProjectPath}' not found.");
			println("\nAvailable loaded assemblies:");
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				println($"  - {name}");
			}
			println("\nTry 'thaum ls assembly:<assembly-name>' where <assembly-name> is one of the above.");
			return;
		}

		println($"Scanning {options.ProjectPath} for {options.Language} symbols...");

		// Get symbols
		var codeMap = await _crawler.CrawlDir(options.ProjectPath);

		if (codeMap.Count == 0) {
			println("No symbols found.");
			return;
		}

		// Build and display hierarchy
		List<CoreTreeNode> hierarchy = CoreTreeNode.BuildHierarchy(codeMap.ToList(), _colorer);
		CoreTreeNode.DisplayHierarchy(hierarchy, options.MaxDepth);
		println($"\nFound {codeMap.Count} symbols total");
	}

	/// <summary>
	/// Prints all triads referenced by a batch evaluation JSON report.
	/// TODO we could add filters (passed-only, symbol regex), and support ordering.
	/// TODO we could colorize sections for readability when not piping to files.
	/// </summary>
	private async Task CMD_ls_triads_from_batch(string root, string batchJsonPath, bool split = false) {
		if (!File.Exists(batchJsonPath)) {
			println($"Batch JSON not found: {batchJsonPath}");
			return;
		}

		string json = await File.ReadAllTextAsync(batchJsonPath);
		// Try parse as batch report first
		try {
			var report = JsonSerializer.Deserialize<BatchReport>(json, GLB.JsonOptions);
			if (report is not null && report.Rows.Count > 0) {
				await PrintTriadsForBatchRows(root, report.Rows, split);
				return;
			}
		} catch { /* fall through */ }

		// Otherwise, treat as session index (from compress-batch)
		try {
			using var doc = JsonDocument.Parse(json);
			if (doc.RootElement.TryGetProperty("triads", out var triads) && triads.ValueKind == JsonValueKind.Array) {
				var triadObjs = new List<FunctionTriad>();
				foreach (var t in triads.EnumerateArray()) {
					string triadPath = t.GetProperty("triadPath").GetString() ?? string.Empty;
					if (string.IsNullOrEmpty(triadPath)) continue;
					string absTriad = ResolveTriadPath(Path.GetDirectoryName(batchJsonPath)!, triadPath, root);
					try {
						var triadJson = await File.ReadAllTextAsync(absTriad);
						var triad     = JsonSerializer.Deserialize<FunctionTriad>(triadJson, GLB.JsonOptions);
						if (triad is null) continue;
						triadObjs.Add(triad);
					} catch { /* ignore per-file errors */ }
				}
				PrintTriadsTree(root, triadObjs, split);
				return;
			}
			if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array) {
				var triadObjs = new List<FunctionTriad>();
				foreach (var it in items.EnumerateArray()) {
					if (!it.TryGetProperty("triadPath", out var p) || p.ValueKind != JsonValueKind.String) continue;
					string triadPath = p.GetString() ?? string.Empty;
					if (string.IsNullOrEmpty(triadPath)) continue;
					string absTriad = ResolveTriadPath(Path.GetDirectoryName(batchJsonPath)!, triadPath, root);
					try {
						var triadJson = await File.ReadAllTextAsync(absTriad);
						var triad     = JsonSerializer.Deserialize<FunctionTriad>(triadJson, GLB.JsonOptions);
						if (triad is null) continue;
						triadObjs.Add(triad);
					} catch { /* ignore per-file errors */ }
				}
				if (triadObjs.Count == 0) {
					println("No triads resolved from session items; verify triadPath fields point to existing files.");
				}
				PrintTriadsTree(root, triadObjs, split);
				return;
			}
		} catch (Exception ex) {
			println($"Failed to parse session index: {ex.Message}");
		}

		println("Unrecognized JSON format. Provide a batch report.json or a session_index.json");
	}

	private async Task PrintTriadsForBatchRows(string root, IEnumerable<BatchRow> rows, bool split = false) {
		// Load triads from cache/sessions
		string sessionsDir = Path.Combine(GLB.CacheDir, "sessions");
		var    triadsMap   = new Dictionary<(string file, string symbol), FunctionTriad>();
		if (Directory.Exists(sessionsDir)) {
			foreach (var triadPath in Directory.GetFiles(sessionsDir, "*.triad.json", SearchOption.AllDirectories)) {
				try {
					string triadJson = await File.ReadAllTextAsync(triadPath);
					var    triad     = JsonSerializer.Deserialize<FunctionTriad>(triadJson, GLB.JsonOptions);
					if (triad is null) continue;
					triadsMap[(Path.GetFullPath(triad.FilePath), triad.SymbolName)] = triad;
				} catch { /* ignore */ }
			}
		}

		var triads  = new List<FunctionTriad>();
		int missing = 0;
		foreach (var row in rows) {
			string absFile = Path.GetFullPath(Path.Combine(root, row.File));
			if (!triadsMap.TryGetValue((absFile, row.Symbol), out var triad)) { missing++; continue; }
			triads.Add(triad);
		}
		PrintTriadsTree(root, triads, split);
		println($"Missing matches: {missing}");
	}

	private static void PrintTriadsTree(string root, IEnumerable<FunctionTriad> triads, bool split = false) {
		var ordered = triads.OrderBy(x => x.FilePath).ThenBy(x => x.SymbolName).ToList();
		int c1      = RenderSection("topology", "blue",    ordered.Where(t => !string.IsNullOrWhiteSpace(t.Topology)) .Select(t => (t, t.Topology!)),  root, split);
		int c2      = RenderSection("morphism", "magenta", ordered.Where(t => !string.IsNullOrWhiteSpace(t.Morphism)).Select(t => (t, t.Morphism!)), root, split);
		int c3      = RenderSection("policy",   "yellow",  ordered.Where(t => !string.IsNullOrWhiteSpace(t.Policy))  .Select(t => (t, t.Policy!)),   root, split);
		int c4      = RenderSection("manifest", "green",   ordered.Where(t => !string.IsNullOrWhiteSpace(t.Manifest)).Select(t => (t, t.Manifest!)), root, split);
		if (c1 + c2 + c3 + c4 == 0) {
			AnsiConsole.MarkupLine("[dim]No non-empty triad blocks to display for this session.[/]");
		}
	}

	private static int RenderSection(string label, string color, IEnumerable<(FunctionTriad triad, string content)> items, string root, bool split) {
		var list = items.ToList();
		if (list.Count == 0) return 0;
		AnsiConsole.MarkupLine($"└── [bold {color}]{label}[/]");
		string basePrefix = "    ";
		int    totalWidth = GetConsoleWidth();
		// Compute alignment column from longest left label (file::function:)
		int maxRelSymLen = 0;
		foreach (var (triad, _) in list) {
			string rel                           = Path.GetRelativePath(root, triad.FilePath);
			string sym                           = triad.SymbolName;
			int    len                           = rel.Length + 2 + sym.Length + 2; // "rel::sym: " (include trailing ': ')
			if (len > maxRelSymLen) maxRelSymLen = len;
		}
		int connectorLen = "└── ".Length; // same visual width as "├── "
		int col          = Math.Min(totalWidth - 8, basePrefix.Length + connectorLen + maxRelSymLen);
		for (int i = 0; i < list.Count; i++) {
			var (triad, contentRaw) = list[i];
			string rel       = Path.GetRelativePath(root, triad.FilePath);
			string sym       = triad.SymbolName;
			string content   = (contentRaw ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");
			string connector = i == list.Count - 1 ? "└── " : "├── ";
			string leftPlain = $"{basePrefix}{connector}{rel}::{sym}: ";
			if (split) {
				int    pad       = Math.Max(1, col - leftPlain.Length);
				int    available = Math.Max(8, totalWidth - (leftPlain.Length + pad));
				string clipped   = Ellipsize(content, available);
				AnsiConsole.MarkupLine($"{basePrefix}{connector}[{color}]{Markup.Escape(rel)}[/]::[cyan]{Markup.Escape(sym)}[/]:" + new string(' ', pad) + $"[dim]{Markup.Escape(clipped)}[/]");
			} else {
				int    available = Math.Max(8, totalWidth - leftPlain.Length);
				string clipped   = Ellipsize(content, available);
				AnsiConsole.MarkupLine($"{basePrefix}{connector}[{color}]{Markup.Escape(rel)}[/]::[cyan]{Markup.Escape(sym)}[/]: [dim]{Markup.Escape(clipped)}[/]");
			}
		}
		return list.Count;
	}

	private static string ResolveTriadPath(string sessionDir, string triadPath, string repoRoot) {
		try {
			if (Path.IsPathRooted(triadPath) && File.Exists(triadPath)) return triadPath;
			string cand1 = Path.GetFullPath(Path.Combine(sessionDir, triadPath));
			if (File.Exists(cand1)) return cand1;
			string cand2 = Path.GetFullPath(triadPath);
			if (File.Exists(cand2)) return cand2;
			string cand3 = Path.GetFullPath(Path.Combine(repoRoot, triadPath));
			if (File.Exists(cand3)) return cand3;
		} catch { }
		return Path.GetFullPath(Path.Combine(sessionDir, triadPath));
	}

	private static int GetConsoleWidth() { try { return Console.WindowWidth; } catch { return GLB.ConsoleMinWidth; } }
	private static string Ellipsize(string s, int max) {
		if (string.IsNullOrEmpty(s) || max <= 0) return string.Empty;
		if (s.Length <= max) return s;
		if (max <= 1) return "…";
		return s.Substring(0, max - 1) + "…";
	}

	[RequiresUnreferencedCode("Calls Thaum.CLI.CLI.CMD_ls_binary(Assembly, LsOptions)")]
	private async Task CMD_ls_dotnet(string assemblyName, LsOptions options) {
		// Load and handle assembly listing
		var assemblies = AppDomain.CurrentDomain.GetAssemblies();
		var matchedAssembly = assemblies.FirstOrDefault(a =>
			a.GetName().Name?.Contains(assemblyName, StringComparison.OrdinalIgnoreCase) == true);

		if (matchedAssembly != null) {
			await CMD_ls_binary(matchedAssembly, options);
		} else {
			println($"Assembly '{assemblyName}' not found.");
			println("\nAvailable loaded assemblies:");
			foreach (var asm in assemblies.OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				println($"  - {name}");
			}
		}
	}

	[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetTypes()")]
	private async Task CMD_ls_binary(Assembly assembly, LsOptions options) {
		println($"Scanning assembly {assembly.GetName().Name}...");

		try {
			List<CodeSymbol> symbols = [];

			// Extract types from the assembly
			Type[] types = assembly.GetTypes();

			foreach (Type type in types) {
				// Skip compiler-generated types
				if (type.Name.Contains("<") || type.Name.Contains(">"))
					continue;

				// Create a symbol for the type (class/interface/enum)
				SymbolKind typeKind = SymbolKind.Class;
				if (type.IsInterface)
					typeKind = SymbolKind.Interface;

				List<CodeSymbol> typeChildren = [];

				// Get methods
				MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				foreach (MethodInfo method in methods) {
					// Skip compiler-generated methods
					if (method.Name.Contains("<") || method.Name.Contains(">") ||
					    method.Name.StartsWith("get_") || method.Name.StartsWith("set_"))
						continue;

					CodeSymbol methodSymbol = new CodeSymbol(
						Name: method.Name,
						Kind: SymbolKind.Method,
						FilePath: assembly.Location,
						StartCodeLoc: new CodeLoc(0, 0),
						EndCodeLoc: new CodeLoc(0, 0)
					);
					typeChildren.Add(methodSymbol);
				}

				// Get properties
				PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				foreach (PropertyInfo property in properties) {
					CodeSymbol propertySymbol = new CodeSymbol(
						Name: property.Name,
						Kind: SymbolKind.Property,
						FilePath: assembly.Location,
						StartCodeLoc: new CodeLoc(0, 0),
						EndCodeLoc: new CodeLoc(0, 0)
					);
					typeChildren.Add(propertySymbol);
				}

				CodeSymbol typeSymbol = new CodeSymbol(
					Name: type.Name,
					Kind: typeKind,
					FilePath: assembly.Location,
					StartCodeLoc: new CodeLoc(0, 0),
					EndCodeLoc: new CodeLoc(0, 0),
					Children: typeChildren.Any() ? typeChildren : null
				);

				symbols.Add(typeSymbol);
			}

			if (!symbols.Any()) {
				println("No symbols found in assembly.");
				return;
			}

			// Build and display hierarchy
			List<CoreTreeNode> tree = CoreTreeNode.BuildHierarchy(symbols, _colorer);
			CoreTreeNode.DisplayHierarchy(tree, options.MaxDepth);
			println($"\nFound {symbols.Count} types in assembly");
		} catch (Exception ex) {
			println($"Error loading assembly: {ex.Message}");
			_logger.LogError(ex, "Failed to load assembly {AssemblyName}", assembly.GetName().Name);
		}

		await Task.CompletedTask;
	}
}