using System.Diagnostics;
using Serilog;

namespace Ratatui.Reload;

public static class TermUtil {
	private static string? _detectedTerminal;

	/// <summary>
	/// Detects if external terminal launch is supported and which terminal to use.
	/// Checks environment variables and common terminal applications.
	/// </summary>
	public static bool DetectExternalTerminalSupport() {
		// Allow user to disable with environment variable
		if (Environment.GetEnvironmentVariable("THAUM_NO_EXTERNAL_TERMINAL") == "1") {
			Log.Information("External terminal disabled by THAUM_NO_EXTERNAL_TERMINAL");
			return false;
		}

		// Try to detect preferred terminal
		_detectedTerminal = DetectPreferredTerminal();
		if (_detectedTerminal != null) {
			Log.Information("Detected terminal for .external TUI: {Terminal}", _detectedTerminal);
			return true;
		}

		Log.Information("No suitable external t.erminal detected, using current terminal");
		return false;
	}

	/// <summary>
	/// Detects the user's preferred terminal application.
	/// Priority: TERMINAL env var > common terminal detection > null
	/// </summary>
	public static string? DetectPreferredTerminal() {
		// Check explicit preference
		string? explicitTerminal = Environment.GetEnvironmentVariable("TERMINAL");
		if (!string.IsNullOrEmpty(explicitTerminal) && IsTerminalAvailable(explicitTerminal)) {
			return explicitTerminal;
		}

		// Detect based on current terminal (from environment)
		string? currentTerm = Environment.GetEnvironmentVariable("TERM_PROGRAM")
		                      ?? Environment.GetEnvironmentVariable("TERM");

		// Map current terminal to launcher command
		if (!string.IsNullOrEmpty(currentTerm)) {
			string? mapped = currentTerm.ToLowerInvariant() switch {
				var x when x.Contains("kitty")     => "kitty",
				var x when x.Contains("alacritty") => "alacritty",
				var x when x.Contains("wezterm")   => "wezterm",
				var x when x.Contains("gnome")     => "gnome-terminal",
				var x when x.Contains("konsole")   => "konsole",
				var x when x.Contains("xterm")     => "xterm",
				_                                  => null
			};
			if (mapped != null && IsTerminalAvailable(mapped)) {
				return mapped;
			}
		}

		// Fallback: try common terminals in order of preference
		string[] candidates = ["kitty", "alacritty", "wezterm", "gnome-terminal", "konsole", "xterm"];
		foreach (string terminal in candidates) {
			if (IsTerminalAvailable(terminal)) {
				return terminal;
			}
		}

		return null;
	}

	/// <summary>
	/// Checks if a terminal application is available in PATH.
	/// </summary>
	public static bool IsTerminalAvailable(string terminal) {
		try {
			ProcessStartInfo psi = new(terminal, "--version") {
				UseShellExecute        = false,
				RedirectStandardOutput = true,
				RedirectStandardError  = true,
				CreateNoWindow         = true
			};
			using Process proc = Process.Start(psi)!;
			proc.WaitForExit(1000); // 1 second timeout
			return proc.ExitCode == 0;
		} catch {
			return false;
		}
	}

	public static bool ApplyExternalTerminalOverride(bool detected) {
		string? overrideValue = Environment.GetEnvironmentVariable("THAUM_EXTERNAL_TERMINAL");
		if (string.IsNullOrEmpty(overrideValue))
			return detected;

		switch (overrideValue.Trim().ToLowerInvariant()) {
			case "0":
			case "false":
			case "off":
				return false;
			case "1":
			case "true":
			case "on":
				return true;
			default:
				return detected;
		}
	}
}