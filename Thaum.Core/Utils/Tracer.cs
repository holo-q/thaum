using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Thaum.Core.Utils;

// Backwards-compatible façade that routes to unified Logging.
public static class Tracer {
	private static ILogger _logger = Logging.Factory.CreateLogger("Tracer");
	private static bool    _isInteractiveMode;
	private static int     _terminalWidth      = 80;
	private static string  _operator           = "->";
	private static int     _fixedOperatorWidth = 4; // operator plus surrounding spaces

	public static void Initialize(ILogger logger, bool isInteractiveMode = false) {
		_logger             = logger;
		_isInteractiveMode  = isInteractiveMode;
		_terminalWidth      = GetTerminalWidth();
		_fixedOperatorWidth = _operator.Length + 2;
	}

	public static void tracein(
		[CallerMemberName] string memberName     = "",
		[CallerFilePath]   string sourceFilePath = "",
		object?                   parameters     = null) {
		string className = GetClassNameFromFilePath(sourceFilePath);
		string prefix    = $"({className}.{memberName})";
		string message   = parameters != null ? $"ENTER with: {parameters}" : "ENTER";

		_trace($"{prefix} {message}");
	}

	public static void traceout(
		[CallerMemberName] string memberName     = "",
		[CallerFilePath]   string sourceFilePath = "",
		object?                   result         = null) {
		string className = GetClassNameFromFilePath(sourceFilePath);
		string prefix    = $"({className}.{memberName})";
		string message   = result != null ? $"EXIT with: {result}" : "EXIT";

		_trace($"{prefix} {message}");
	}

	public static void trace(
		string                    message,
		[CallerMemberName] string memberName     = "",
		[CallerFilePath]   string sourceFilePath = "") {
		string className = GetClassNameFromFilePath(sourceFilePath);
		string prefix    = $"({className}.{memberName})";

		_trace($"{prefix} {message}");
	}

	public static void traceop(
		string                    operation,
		[CallerMemberName] string memberName     = "",
		[CallerFilePath]   string sourceFilePath = "") {
		string className = GetClassNameFromFilePath(sourceFilePath);
		string prefix    = $"({className}.{memberName})";

		_trace($"{prefix} OPERATION: {operation}");
	}

	private static void _trace(string message) => _logger.LogTrace(message);

	private static string GetClassNameFromFilePath(string sourceFilePath) {
		if (string.IsNullOrEmpty(sourceFilePath)) return "Unknown";

		string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
		return fileName;
	}

	public static void Dispose() { }


	public static IDisposable trace_scope(
		string                    scopeName,
		[CallerMemberName] string memberName     = "",
		[CallerFilePath]   string sourceFilePath = "") {
		return new TraceScopeHandler(scopeName, memberName, sourceFilePath);
	}

	private class TraceScopeHandler : IDisposable {
		private readonly string _scopeName;
		private readonly string _memberName;
		private readonly string _sourceFilePath;

		public TraceScopeHandler(string scopeName, string memberName, string sourceFilePath) {
			_scopeName      = scopeName;
			_memberName     = memberName;
			_sourceFilePath = sourceFilePath;

			trace($"SCOPE ENTER: {scopeName}", _memberName, _sourceFilePath);
		}

		public void Dispose() {
			trace($"SCOPE EXIT: {_scopeName}", _memberName, _sourceFilePath);
		}
	}

	// Column formatting methods
	public static string tracefmt(string source, string target, string status = "") {
		int availableWidth = _terminalWidth - _fixedOperatorWidth;

		// If status is provided, reserve space for it (with brackets and spacing)
		int statusWidth  = !string.IsNullOrEmpty(status) ? status.Length + 3 : 0; // "[status] "
		int contentWidth = availableWidth - statusWidth;

		// Calculate space distribution
		int sourceWidth = Math.Min(source.Length, contentWidth / 2);
		int targetWidth = contentWidth - sourceWidth;

		// Truncate if necessary
		string truncatedSource = source.Length > sourceWidth
			? $"{source[..(sourceWidth - 3)]}..."
			: source;

		string truncatedTarget = target.Length > targetWidth
			? $"{target[..(targetWidth - 3)]}..."
			: target;

		// Build the formatted line
		StringBuilder sb = new StringBuilder();

		// Add status if provided
		if (!string.IsNullOrEmpty(status)) {
			sb.Append($"[{status}] ");
		}

		// Add source with padding
		sb.Append(truncatedSource.PadRight(sourceWidth));

		// Add operator
		sb.Append($" {_operator} ");

		// Add target
		sb.Append(truncatedTarget);

		return sb.ToString();
	}

	public static string tracehdr(string title) {
		int    padding      = (_terminalWidth - title.Length - 4) / 2; // 4 for "== =="
		string leftPadding  = "=".PadRight(Math.Max(0, padding), '=');
		string rightPadding = "=".PadLeft(Math.Max(0, padding), '=');

		return $"{leftPadding} {title} {rightPadding}".PadRight(_terminalWidth, '=')[.._terminalWidth];
	}

	public static string traceprog(string operation, int current, int total, double percentage) {
		string progressInfo   = $"({current}/{total} - {percentage:F1}%)";
		int    availableWidth = _terminalWidth - progressInfo.Length - 1;

		string truncatedOperation = operation.Length > availableWidth
			? $"{operation[..(availableWidth - 3)]}..."
			: operation;

		return $"{truncatedOperation.PadRight(availableWidth)} {progressInfo}";
	}

	public static void traceln(string source, string target, string status = "") {
		println(tracefmt(source, target, status));
	}

	public static void traceheader(string title) {
		println();
		try {
			Rule rule = new Rule($"{title}");
			AnsiConsole.Write(rule);
		} catch {
			println(tracehdr(title));
		}
		println();
	}

	public static void traceprogress(string operation, int current, int total) {
		double percentage = (double)current / total * 100;
		println(traceprog(operation, current, total, percentage));
	}

	private static int GetTerminalWidth() {
		try {
			return Console.WindowWidth;
		} catch {
			return 80; // Fallback width
		}
	}

	public static void print(object? obj = null) {
		_logger.LogInformation("{Message}", obj?.ToString() ?? "");
	}

	public static void println(object? obj = null) {
		if (obj is null) {
			Logging.NewLine();
			return;
		}
		_logger.LogInformation("{Message}", obj.ToString());
	}
}