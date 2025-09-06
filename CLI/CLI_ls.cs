using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Thaum.CLI.Models;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Thaum.Utils;
using static System.Console;
using static Thaum.Core.Utils.TraceLogger;

namespace Thaum.CLI;

/// <summary>
/// Partial CLI class containing ls command implementation where symbols are discovered
/// through TreeSitter or assembly reflection where hierarchical display reveals structure
/// where perceptual coloring creates semantic visual feedback
/// </summary>
public partial class CLI {
	[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetReferencedAssemblies()")]
	private async Task CMD_ls(string[] args) {
		LsOptions opts = ParseLsOptions(args);

		// First, always try to load assemblies that we reference
		foreach (var asmName in Assembly.GetExecutingAssembly().GetReferencedAssemblies()) {
			try {
				Assembly.Load(asmName);
			} catch { }
		}

		// Check if asking for an assembly by name (e.g., "TreeSitter" or "TreeSitter.DotNet")
		// Try to find the assembly in the current AppDomain first before checking filesystem
		Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault(a => {
				var name = a.GetName().Name;
				return name?.Equals(opts.ProjectPath, StringComparison.OrdinalIgnoreCase) == true ||
				       (opts.ProjectPath.Equals("TreeSitter.DotNet", StringComparison.OrdinalIgnoreCase) &&
				        name?.Equals("TreeSitter", StringComparison.OrdinalIgnoreCase) == true);
			});

		if (assembly != null) {
			await CMD_ls_assembly(assembly, opts);
			return;
		}

		// Check if the path is a DLL/EXE file
		if (File.Exists(opts.ProjectPath) &&
		    (opts.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		     opts.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) {
			Assembly fileAssembly = Assembly.LoadFrom(opts.ProjectPath);
			await CMD_ls_assembly(fileAssembly, opts);
			return;
		}

		// Also check if it's a DLL/EXE that doesn't exist yet (for better error message)
		if (opts.ProjectPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
		    opts.ProjectPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
			WriteLine($"Error: Could not find assembly file: {opts.ProjectPath}");
			WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
			return;
		}

		// If we reach here and the path doesn't exist, list available assemblies to help debug
		if (!Directory.Exists(opts.ProjectPath) && !File.Exists(opts.ProjectPath)) {
			WriteLine($"Path '{opts.ProjectPath}' not found.");
			WriteLine("\nAvailable loaded assemblies:");
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.GetName().Name)) {
				var name = asm.GetName().Name;
				WriteLine($"  - {name}");
			}
			WriteLine("\nTry 'thaum ls <assembly-name>' where <assembly-name> is one of the above.");
			return;
		}

		WriteLine($"Scanning {opts.ProjectPath} for {opts.Language} symbols...");

		// Get symbols
		List<CodeSymbol> symbols = await _crawler.CrawlDir(opts.ProjectPath);

		if (!symbols.Any()) {
			WriteLine("No symbols found.");
			return;
		}

		// Build and display hierarchy
		List<TreeNode> hierarchy = TreeNode.BuildHierarchy(symbols, _colorer);
		TreeNode.DisplayHierarchy(hierarchy, opts);

		WriteLine($"\nFound {symbols.Count} symbols total");
	}

	[RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetTypes()")]
	private async Task CMD_ls_assembly(Assembly assembly, LsOptions options) {
		WriteLine($"Scanning assembly {assembly.GetName().Name}...");

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
				// Use Class for enums and structs too since we don't have specific kinds for them

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

				// Get fields
				FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
				foreach (FieldInfo field in fields) {
					// Skip compiler-generated fields
					if (field.Name.Contains("<") || field.Name.Contains(">"))
						continue;

					CodeSymbol fieldSymbol = new CodeSymbol(
						Name: field.Name,
						Kind: SymbolKind.Field,
						FilePath: assembly.Location,
						StartCodeLoc: new CodeLoc(0, 0),
						EndCodeLoc: new CodeLoc(0, 0)
					);
					typeChildren.Add(fieldSymbol);
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
				WriteLine("No symbols found in assembly.");
				return;
			}

			// Build and display hierarchy
			List<TreeNode> tree = TreeNode.BuildHierarchy(symbols, _colorer);
			TreeNode.DisplayHierarchy(tree, options);

			WriteLine($"\nFound {symbols.Count} types in assembly");
			WriteLine($"Total symbols: {symbols.Count + symbols.SelectMany(s => s.Children ?? []).Count()}");
		} catch (Exception ex) {
			WriteLine($"Error loading assembly: {ex.Message}");
			_logger.LogError(ex, "Failed to load assembly {AssemblyName}", assembly.GetName().Name);
			Environment.Exit(1);
		}

		await Task.CompletedTask;
	}
}