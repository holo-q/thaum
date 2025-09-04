using Thaum.Core.Utils;
using Microsoft.Extensions.Logging;

class TestLogger {
    static void Main() {
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<TestLogger>();
        
        // Test normal mode
        Console.WriteLine("Testing normal mode:");
        TraceLogger.Initialize(logger, false);
        TraceLogger.TraceEnter();
        TraceLogger.TraceInfo("This should go to console");
        TraceLogger.TraceExit();
        
        // Test interactive mode
        Console.WriteLine("\nTesting interactive mode:");
        TraceLogger.Dispose();
        TraceLogger.Initialize(logger, true);
        TraceLogger.TraceEnter();
        TraceLogger.TraceInfo("This should go to interactive.log");
        TraceLogger.TraceExit();
        TraceLogger.Dispose();
        
        Console.WriteLine("Done! Check for interactive.log file.");
    }
}