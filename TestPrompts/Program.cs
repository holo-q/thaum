using Thaum.Prompts;
using System.Reflection;

// Show that IntelliSense can see all the generated functions
var methods = typeof(Prompts).GetMethods(BindingFlags.Public | BindingFlags.Static)
	.Where(m => m.DeclaringType == typeof(Prompts))
	.OrderBy(m => m.Name);

Console.WriteLine("Generated Prompts functions:");
Console.WriteLine("============================");

foreach (var method in methods) {
	var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
	Console.WriteLine($"public static string {method.Name}({parameters})");
}

Console.WriteLine($"\nTotal functions: {methods.Count()}");

// Test one function
if (methods.Any(m => m.Name == "compressfunctionv6")) {
	Console.WriteLine("\n✅ The Prompts class is fully generated and accessible!");
	Console.WriteLine("✅ IntelliSense should show all functions when you type 'Prompts.'");
}