using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace Thaum.Utils;

/// <summary>
/// Ambient logging infrastructure using Serilog where static factory eliminates DI ceremony
/// where For<T>() creates typed loggers maintaining source context where CLI/TUI modes
/// configure appropriate sinks where Seq integration enables structured log analysis where
/// file output preserves execution history for debugging
/// </summary>
public static class Logging {
	public static readonly ILoggerFactory Factory = new SerilogLoggerFactory();

	/// <summary>
	/// Creates typed logger preserving class context where generic parameter enables
	/// automatic source identification where ambient pattern eliminates constructor injection
	/// </summary>
	public static ILogger<T> For<T>() => Factory.CreateLogger<T>();

	/// <summary>
	/// Configures CLI logging where console output provides immediate feedback where
	/// file output creates persistent record where Seq enables structured analysis where
	/// verbose level captures maximum detail for debugging
	/// </summary>
	public static void SetupCLI() {
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Console()
			.WriteTo.File("output.log", outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
			.WriteTo.Seq("http://localhost:5341")
			.CreateLogger();
	}

	/// <summary>
	/// Configures TUI logging suppressing console to avoid Terminal.Gui conflicts where
	/// aggressive flush interval ensures log visibility during crashes where file-only
	/// output prevents visual corruption of terminal interface
	/// </summary>
	public static void SetupTUI() {
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.File("output.log",
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
				flushToDiskInterval: TimeSpan.FromMilliseconds(500))
			.WriteTo.Seq("http://localhost:5341")
			.CreateLogger();
	}
}