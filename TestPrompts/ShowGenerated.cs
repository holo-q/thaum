// This file demonstrates the generated Prompts class structure
using Thaum.Prompts;

namespace TestPrompts;

public static class ShowGenerated {
	public static void Example() {
		// These are the actual generated functions you can call:

		// For compress_function_v6.txt:
		var result1 = Prompts.compressfunctionv6(
			sourcecode: "void Test() { ... }",
			symbolname: "Test",
			availablekeys: "NONE"
		);

		// For optimize_function.txt:
		var result2 = Prompts.optimizefunction(
			sourcecode: "void Test() { ... }",
			symbolname: "Test"
		);

		// For golf_class.txt:
		var result3 = Prompts.golfclass(
			sourcecode: "class Test { ... }",
			symbolname: "Test",
			availablekeys: "NONE"
		);

		// All 24 prompt files become functions in the Prompts static class
		// The class is marked as 'partial' so each prompt file generates
		// its own partial class definition
	}
}