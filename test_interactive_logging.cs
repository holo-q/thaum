using Thaum.Core.Utils;
using Microsoft.Extensions.Logging;
using static Thaum.Core.Utils.Tracer;

class TestLogger {
	static void Main() {
		var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
		var logger        = loggerFactory.CreateLogger<TestLogger>();

		// Test normal mode
		println("Testing normal mode:");
		Initialize(logger, false);
		tracein();
		trace("This should go to console");
		traceout();

		// Test interactive mode
		println("\nTesting interactive mode:");
		Dispose();
		Initialize(logger, true);
		tracein();
		trace("This should go to interactive.log");
		traceout();
		Dispose();

		println("Done! Check for interactive.log file.");
	}
}