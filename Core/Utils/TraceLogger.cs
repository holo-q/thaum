using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

namespace Thaum.Core.Utils;

public static class TraceLogger {
	private static ILogger?    _logger;
	private static FileWriter? _interactiveFileWriter;
	private static bool        _isInteractiveMode = false;

	public static void Initialize(ILogger logger, bool isInteractiveMode = false) {
		_logger            = logger;
		_isInteractiveMode = isInteractiveMode;

		if (isInteractiveMode) {
			_interactiveFileWriter = new FileWriter("interactive.log");
		}
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

	private static void _trace(string message) {
		if (_isInteractiveMode && _interactiveFileWriter != null) {
			_interactiveFileWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] TRACE: {message}");
		} else {
			_logger?.LogTrace(message);
		}
	}

	private static string GetClassNameFromFilePath(string sourceFilePath) {
		if (string.IsNullOrEmpty(sourceFilePath)) return "Unknown";

		string fileName = Path.GetFileNameWithoutExtension(sourceFilePath);
		return fileName;
	}

	public static void Dispose() {
		_interactiveFileWriter?.Dispose();
	}

	private class FileWriter : IDisposable {
		private readonly StreamWriter _writer;
		private readonly object       _lock = new object();

		public FileWriter(string filePath) {
			_writer = new StreamWriter(filePath, append: true) {
				AutoFlush = true
			};
		}

		public void WriteLine(string message) {
			lock (_lock) {
				_writer.WriteLine(message);
			}
		}

		public void Dispose() {
			_writer?.Dispose();
		}
	}
}

public static class ScopeTracer {
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

			TraceLogger.trace($"SCOPE ENTER: {scopeName}", _memberName, _sourceFilePath);
		}

		public void Dispose() {
			TraceLogger.trace($"SCOPE EXIT: {_scopeName}", _memberName, _sourceFilePath);
		}
	}
}