using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Utils;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI.Commands;

// TODO this should become a crawler for C# assemblies

internal class AssemblyCommands {
	private readonly ILogger           _logger;
	private readonly PerceptualColorer _colorer;

	public AssemblyCommands(ILogger logger, PerceptualColorer colorer) {
		_logger  = logger;
		_colorer = colorer;
	}

    [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetTypes()")]
    public async Task HandleAssemblyListing(Assembly assembly, LsOptions options) {
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
				// Use Class for enums and structs too since we don't have specific kinds for them

				List<CodeSymbol> typeChildren = new List<CodeSymbol>();

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
				println("No symbols found in assembly.");
				return;
			}

			// Build and display hierarchy
			List<TreeNode> hierarchy = TreeNode.BuildHierarchy(symbols, _colorer);
			TreeNode.DisplayHierarchy(hierarchy, options.MaxDepth);

			println($"\nFound {symbols.Count} types in assembly");
			println($"Total symbols: {symbols.Count + symbols.SelectMany(s => s.Children ?? new List<CodeSymbol>()).Count()}");
		} catch (Exception ex) {
			println($"Error loading assembly: {ex.Message}");
			_logger.LogError(ex, "Failed to load assembly {AssemblyName}", assembly.GetName().Name);
			Environment.Exit(1);
		}

		await Task.CompletedTask;
	}

    [RequiresUnreferencedCode("Calls System.Reflection.Assembly.GetTypes()")]
    public async Task HandleLoadedAssemblyListing(string assemblyNamePattern, LsOptions options) {
		println($"Searching for loaded assemblies matching '{assemblyNamePattern}'...");

		try {
			// Get all loaded assemblies
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

			// Filter assemblies by name pattern
			List<Assembly> matchedAssemblies = assemblies
				.Where(a => a.GetName().Name != null &&
				            a.GetName().Name.Contains(assemblyNamePattern, StringComparison.OrdinalIgnoreCase))
				.ToList();

			if (!matchedAssemblies.Any()) {
				println($"No loaded assemblies found matching '{assemblyNamePattern}'");
				println("\nAvailable loaded assemblies:");
				foreach (var asm in assemblies.OrderBy(a => a.GetName().Name)) {
					if (asm.GetName().Name != null)
						println($"  - {asm.GetName().Name}");
				}
				return;
			}

			foreach (Assembly assembly in matchedAssemblies) {
				println($"\nAssembly: {assembly.GetName().Name} v{assembly.GetName().Version}");
				println(new string('=', 60));

				List<CodeSymbol> symbols = new List<CodeSymbol>();

				// Extract types from the assembly
				Type[] types;
				try {
					types = assembly.GetTypes();
				} catch (ReflectionTypeLoadException ex) {
					// Handle partial loading
					types = ex.Types.Where(t => t != null).ToArray()!;
				}

				foreach (Type type in types) {
					// Skip compiler-generated types
					if (type.Name.Contains("<") || type.Name.Contains(">"))
						continue;

					// Create a symbol for the type
					SymbolKind typeKind = SymbolKind.Class;
					if (type.IsInterface)
						typeKind = SymbolKind.Interface;

					List<CodeSymbol> typeChildren = new List<CodeSymbol>();

					// Get methods
					try {
						MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
						foreach (MethodInfo method in methods) {
							// Skip property accessors and compiler-generated methods
							if (method.Name.Contains("<") || method.Name.Contains(">") ||
							    method.Name.StartsWith("get_") || method.Name.StartsWith("set_") ||
							    method.Name.StartsWith("add_") || method.Name.StartsWith("remove_"))
								continue;

							// Build method signature
							string parameters = string.Join(", ", method.GetParameters().Select(p =>
								$"{p.ParameterType.Name} {p.Name}"));
							string methodName = $"{method.Name}({parameters})";

							CodeSymbol methodSymbol = new CodeSymbol(
								Name: methodName,
								Kind: SymbolKind.Method,
								FilePath: assembly.Location,
								StartCodeLoc: new CodeLoc(0, 0),
								EndCodeLoc: new CodeLoc(0, 0)
							);
							typeChildren.Add(methodSymbol);
						}
					} catch {
						// Skip types we can't reflect on
					}

					// Get properties
					try {
						PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
						foreach (PropertyInfo property in properties) {
							string propertyInfo = $"{property.Name}: {property.PropertyType.Name}";
							CodeSymbol propertySymbol = new CodeSymbol(
								Name: propertyInfo,
								Kind: SymbolKind.Property,
								FilePath: assembly.Location,
								StartCodeLoc: new CodeLoc(0, 0),
								EndCodeLoc: new CodeLoc(0, 0)
							);
							typeChildren.Add(propertySymbol);
						}
					} catch {
						// Skip types we can't reflect on
					}

					// Get fields
					try {
						FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
						foreach (FieldInfo field in fields) {
							// Skip compiler-generated fields
							if (field.Name.Contains("<") || field.Name.Contains(">"))
								continue;

							string fieldInfo = $"{field.Name}: {field.FieldType.Name}";
							CodeSymbol fieldSymbol = new CodeSymbol(
								Name: fieldInfo,
								Kind: SymbolKind.Field,
								FilePath: assembly.Location,
								StartCodeLoc: new CodeLoc(0, 0),
								EndCodeLoc: new CodeLoc(0, 0)
							);
							typeChildren.Add(fieldSymbol);
						}
					} catch {
						// Skip types we can't reflect on
					}

					// Include full type name with namespace
					string typeName = type.Namespace != null ? $"{type.Namespace}.{type.Name}" : type.Name;

					CodeSymbol typeSymbol = new CodeSymbol(
						Name: typeName,
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
					continue;
				}

				// Build and display hierarchy
				List<TreeNode> hierarchy = TreeNode.BuildHierarchy(symbols, _colorer);
				TreeNode.DisplayHierarchy(hierarchy, options.MaxDepth);

				println($"\nFound {symbols.Count} types in assembly");
				println($"Total symbols: {symbols.Count + symbols.SelectMany(s => s.Children ?? new List<CodeSymbol>()).Count()}");
			}
		} catch (Exception ex) {
			println($"Error inspecting loaded assemblies: {ex.Message}");
			_logger.LogError(ex, "Failed to inspect loaded assembly {AssemblyName}", assemblyNamePattern);
		}

		await Task.CompletedTask;
	}
}