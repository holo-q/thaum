using Thaum.Core.Utils;
using Microsoft.Extensions.Logging;
using static Thaum.Core.Utils.Tracer;

class TestLogger {
	static void Main() {
		var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
		var logger        = loggerFactory.CreateLogger<TestLogger>();

		// Test normal mode
		println("Testing normal mode:");
		Tracer.Initialize(logger, false);
		Tracer.tracein();
		Tracer.trace("This should go to console");
		Tracer.traceout();

		// Test interactive mode
		println("\nTesting interactive mode:");
		Tracer.Dispose();
		Tracer.Initialize(logger, true);
		Tracer.tracein();
		Tracer.trace("This should go to interactive.log");
		Tracer.traceout();
		Tracer.Dispose();

		println("Done! Check for interactive.log file.");
	}
}