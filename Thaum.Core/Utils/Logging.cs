using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Compact;
using Serilog.Sinks.SystemConsole.Themes;
using Serilog.Sinks.SpectreConsole;
using ILogger = Serilog.ILogger;

// Optional rich console integration; if the package is present, we use it.
// If not restored, the fallback console sink path still works.
// ReSharper disable once RedundantUsingDirective
using Spectre.Console;

namespace Thaum.Utils;

/// <summary>
/// Opinionated, uniform logging: Serilog core + Spectre console for humans, structured files for machines.
/// Provides ambient typed loggers, indent scopes, timed scopes, and clean CLI/TUI configurations.
/// </summary>
public static class Logging {
	public static readonly ILoggerFactory Factory = new SerilogLoggerFactory();

	// Async-local indent depth and label stack for per-async-flow formatting
	private static readonly AsyncLocal<Stack<string>> IndentStack = new();
	private static string IndentUnit => "·· ";

	public enum Mode { Cli, Tui, Quiet }

	/// <summary>
	/// Creates typed logger preserving class context.
/// </summary>
	public static ILogger<T> For<T>() => Factory.CreateLogger<T>();

	/// <summary>
	/// Setup Serilog for CLI use: Spectre console (or ANSI console) + rolling file + optional Seq.
	/// </summary>
	public static void SetupCLI() => Setup(Mode.Cli);

	/// <summary>
	/// Setup Serilog for TUI use: file + Seq only, no stdout to avoid TUI redraw clashes.
	/// </summary>
	public static void SetupTUI() => Setup(Mode.Tui);

	public static void Setup(Mode mode) {
		LoggerConfiguration cfg = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.FromLogContext()
			.Enrich.With(new IndentEnricher())
			.Enrich.WithProperty("App", "Thaum");

		if (mode == Mode.Cli) {
			// Human console: prefer Spectre sink when available; fallback to ANSI console sink.
			cfg = UseSpectreConsole(cfg)
				?? cfg.WriteTo.Console(
						outputTemplate: "{Timestamp:HH:mm:ss} {Level:u3} {Indent}{Message:lj}{NewLine}{Exception}",
						theme: AnsiConsoleTheme.Code,
						standardErrorFromLevel: LogEventLevel.Warning);
		}

		// File: structured (compact JSON) and readable text side-by-side
		cfg = cfg
			.WriteTo.File(new CompactJsonFormatter(),
				GLB.OutputLogFile + ".ndjson",
				rollingInterval: RollingInterval.Day,
				shared: true)
			.WriteTo.File(
				path: GLB.OutputLogFile,
				outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Indent}{Message:lj}{NewLine}{Exception}",
				shared: true,
				flushToDiskInterval: mode == Mode.Tui ? TimeSpan.FromMilliseconds(500) : (TimeSpan?)null);

		// Optional Seq (matches prior behavior). Controlled by env THAUM_SEQ_URL or defaults to localhost.
		string? seqUrl = Environment.GetEnvironmentVariable("THAUM_SEQ_URL") ?? "http://localhost:5341";
		if (!string.IsNullOrWhiteSpace(seqUrl)) {
			cfg = cfg.WriteTo.Seq(seqUrl);
		}

		Log.Logger = cfg.CreateLogger();
	}

	private static LoggerConfiguration? UseSpectreConsole(LoggerConfiguration cfg) {
		try {
			// If Serilog.Sinks.SpectreConsole is available at runtime, use it.
			// Note: This relies on package reference. If not present, this path is skipped.
			return cfg.WriteTo.SpectreConsole(
				"{Timestamp:HH:mm:ss} {Level:u3} {Indent}{Message:lj}{NewLine}{Exception}",
				Serilog.Events.LogEventLevel.Verbose);
		} catch (Exception) {
			return null;
		}
	}

	// -------- Indentation API --------
	public static IDisposable Push(string? label = null, LogLevel level = LogLevel.Information,
		[CallerMemberName] string member = "", [CallerFilePath] string file = "") {
		EnsureStack();
		if (!string.IsNullOrWhiteSpace(label)) {
			// Emit label before increasing indent so children align beneath
			var human = $"({Path.GetFileNameWithoutExtension(file)}.{member}) {label}";
			Serilog.Log.Logger.ForContext("Indent", CurrentIndent())
				.Write(Convert(level), "{Message}", human);
		}
		IndentStack.Value!.Push(IndentUnit);
		return new PopScope();
	}

	public static IDisposable Scope(string label, LogLevel level = LogLevel.Information,
		[CallerMemberName] string member = "", [CallerFilePath] string file = "") => Push(label, level, member, file);

	public static void Pop() {
		EnsureStack();
		if (IndentStack.Value!.Count > 0) IndentStack.Value.Pop();
	}

	private static void EnsureStack() {
		IndentStack.Value ??= new Stack<string>(8);
	}

	private static string CurrentIndent() => string.Concat(IndentStack.Value ?? new Stack<string>());

	private sealed class PopScope : IDisposable {
		private bool _disposed;
		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			Pop();
		}
	}

	// -------- Timed scope (tracer) --------
	public static IDisposable Time(string name,
		double thresholdMs = 0,
		[CallerMemberName] string member = "",
		[CallerFilePath] string file = "") => new TimerScope(name, thresholdMs, member, file);

	private sealed class TimerScope : IDisposable {
		private readonly string _name;
		private readonly double _thresholdMs;
		private readonly string _member;
		private readonly string _file;
		private readonly Stopwatch _sw = Stopwatch.StartNew();

		public TimerScope(string name, double thresholdMs, string member, string file) {
			_name       = name;
			_thresholdMs = thresholdMs;
			_member     = member;
			_file       = file;
		}

		public void Dispose() {
			_sw.Stop();
			double ms = _sw.Elapsed.TotalMilliseconds;
			if (ms < _thresholdMs) return;

			(string color, string pretty) style = ms switch {
				<= 200  => ("green",  $"({_sw.Elapsed.TotalSeconds:F3}s) {_name}"),
				<= 1000 => ("yellow", $"({_sw.Elapsed.TotalSeconds:F3}s) {_name}"),
				_       => ("red",    $"({_sw.Elapsed.TotalSeconds:F3}s) {_name}")
			};

			string msg = $"[{style.color}]{style.pretty}[/]"; // Spectre markup; harmless if not parsed
			var human = $"({Path.GetFileNameWithoutExtension(_file)}.{_member}) {msg}";
			Serilog.Log.Logger.ForContext("Indent", CurrentIndent())
				.Information("{Message}", human);
		}
	}

	// -------- Convenience --------
	public static void NewLine() {
		try {
			// Prefer Spectre when available to avoid interfering with Serilog formatting
			AnsiConsole.WriteLine();
		} catch {
			Console.WriteLine();
		}
	}

	public static void WriteException(Exception ex) {
		try {
			AnsiConsole.WriteException(ex, new ExceptionSettings {
				Format = ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods
			});
		} catch {
			Serilog.Log.Error(ex, "Unhandled exception");
		}
	}

	/// <summary>
	/// Enricher that injects current indent string into log events for templates.
	/// </summary>
	private sealed class IndentEnricher : ILogEventEnricher {
		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
			string indent = CurrentIndent();
			logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("Indent", indent));
		}
	}

	private static LogEventLevel Convert(LogLevel lvl) => lvl switch {
		LogLevel.Trace       => LogEventLevel.Verbose,
		LogLevel.Debug       => LogEventLevel.Debug,
		LogLevel.Information => LogEventLevel.Information,
		LogLevel.Warning     => LogEventLevel.Warning,
		LogLevel.Error       => LogEventLevel.Error,
		LogLevel.Critical    => LogEventLevel.Fatal,
		_                    => LogEventLevel.Information
	};
}
