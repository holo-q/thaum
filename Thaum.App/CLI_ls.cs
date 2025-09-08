using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Core.Utils;
using Thaum.Utils;
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
    public async Task CMD_ls(LsOptions options) {
		trace($"Executing ls command with options: {options}");

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
		List<TreeNode> hierarchy = TreeNode.BuildHierarchy(codeMap.ToList(), _colorer);
		TreeNode.DisplayHierarchy(hierarchy, options.MaxDepth);
		println($"\nFound {codeMap.Count} symbols total");
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
			List<TreeNode> tree = TreeNode.BuildHierarchy(symbols, _colorer);
			TreeNode.DisplayHierarchy(tree, options.MaxDepth);
			println($"\nFound {symbols.Count} types in assembly");
		} catch (Exception ex) {
			println($"Error loading assembly: {ex.Message}");
			_logger.LogError(ex, "Failed to load assembly {AssemblyName}", assembly.GetName().Name);
		}

		await Task.CompletedTask;
	}
}