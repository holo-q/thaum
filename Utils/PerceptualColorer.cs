using System.Text.RegularExpressions;

namespace Thaum.Core.Utils;

public record TerminalColorInfo((int r, int g, int b) BackgroundColor, bool IsDark);

/// <summary>
/// Advanced perceptual color engine that generates semantically meaningful colors
/// optimized for any terminal background using color science principles
/// </summary>
public class PerceptualColorer {
	private readonly TerminalColorInfo _terminalInfo;
	private readonly ColorHarmony      _harmony;

	public PerceptualColorer() {
		_terminalInfo = DetectTerminalColors();
		_harmony      = new ColorHarmony(_terminalInfo.BackgroundColor);
	}

	/// <summary>
	/// Generate a perceptually optimal color for the given semantic purpose
	/// </summary>
	public (int r, int g, int b) GenerateSemanticColor(string seed, SemanticColorType colorType) {
		return colorType switch {
			SemanticColorType.Function  => _harmony.GenerateBurntOrange(),
			SemanticColorType.Class     => _harmony.GenerateGreen(),
			SemanticColorType.Interface => _harmony.GenerateAnalogous(0.6f),
			SemanticColorType.Module    => _harmony.GenerateComplementary(0.4f),
			_                           => _harmony.GenerateAnalogous(0.6f)
		};
	}

	private TerminalColorInfo DetectTerminalColors() {
		// Try to detect terminal background via OSC sequences
		(int r, int g, int b)? bgColor = TryDetectBackgroundColor();

		if (bgColor.HasValue) {
			System.Diagnostics.Debug.WriteLine($"Detected background color: RGB({bgColor.Value.r}, {bgColor.Value.g}, {bgColor.Value.b})");
		} else {
			bgColor = EstimateFromEnvironment();
			System.Diagnostics.Debug.WriteLine($"Using estimated background color: RGB({bgColor.Value.r}, {bgColor.Value.g}, {bgColor.Value.b})");
		}

		bool isDark = IsColorDark(bgColor.Value);
		System.Diagnostics.Debug.WriteLine($"Background is {(isDark ? "dark" : "light")}");

		return new TerminalColorInfo(bgColor.Value, isDark);
	}

	private (int r, int g, int b)? TryDetectBackgroundColor() {
		try {
			// Save current console state
			bool originalInputMode = Console.TreatControlCAsInput;
			Console.TreatControlCAsInput = true;

			// Send OSC 11 query for background color
			Console.Write("\u001b]11;?\u001b\\");
			Console.Out.Flush();

			// Wait for response with timeout
			string? response = ReadOscResponseWithTimeout(GLB.OscTimeoutMs); // 1 second timeout

			// Restore console state
			Console.TreatControlCAsInput = originalInputMode;

			if (response != null) {
				return ParseOscColorResponse(response);
			}
		} catch (Exception ex) {
			// Log the error but don't fail - fall back to environment detection
			System.Diagnostics.Debug.WriteLine($"Background color detection failed: {ex.Message}");
		}

		return null;
	}

	private static string? ReadOscResponseWithTimeout(int timeoutMs) {
		List<char> buffer    = new List<char>();
		DateTime   startTime = DateTime.UtcNow;
		bool       inEscape  = false;
		bool       inOsc     = false;

		while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs) {
			if (Console.KeyAvailable) {
				ConsoleKeyInfo keyInfo = Console.ReadKey(true);
				char           ch      = keyInfo.KeyChar;

				buffer.Add(ch);

				// Look for OSC response pattern: ESC ] 11 ; rgb:RRRR/GGGG/BBBB ESC \
				if (ch == '\u001b' && !inEscape) {
					inEscape = true;
					continue;
				}

				if (inEscape && ch == ']') {
					inOsc = true;
					continue;
				}

				if (inOsc && ch == '\u001b') {
					// Look for terminator
					if (Console.KeyAvailable) {
						ConsoleKeyInfo nextKey = Console.ReadKey(true);
						buffer.Add(nextKey.KeyChar);

						if (nextKey.KeyChar == '\\') {
							// Found complete OSC response
							return new string(buffer.ToArray());
						}
					}
				}
			} else {
				Thread.Sleep(10); // Small delay to avoid busy waiting
			}
		}

		return null;
	}

	private (int r, int g, int b)? ParseOscColorResponse(string response) {
		try {
			// Parse response like: ESC]11;rgb:RRRR/GGGG/BBBB ESC\
			// Or: ESC]11;#RRGGBB ESC\

			Match match = Regex.Match(response, @"rgb:([0-9a-fA-F]+)/([0-9a-fA-F]+)/([0-9a-fA-F]+)");
			if (match.Success) {
				// 16-bit RGB values, convert to 8-bit
				int r16 = Convert.ToInt32(match.Groups[1].Value, 16);
				int g16 = Convert.ToInt32(match.Groups[2].Value, 16);
				int b16 = Convert.ToInt32(match.Groups[3].Value, 16);

				// Scale from 16-bit (0-65535) to 8-bit (0-255)
				int r = (int)(r16 * 255.0 / 65535.0);
				int g = (int)(g16 * 255.0 / 65535.0);
				int b = (int)(b16 * 255.0 / 65535.0);

				return (r, g, b);
			}

			// Try hex format
			Match hexMatch = Regex.Match(response, @"#([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})");
			if (hexMatch.Success) {
				int r = Convert.ToInt32(hexMatch.Groups[1].Value, 16);
				int g = Convert.ToInt32(hexMatch.Groups[2].Value, 16);
				int b = Convert.ToInt32(hexMatch.Groups[3].Value, 16);

				return (r, g, b);
			}
		} catch (Exception ex) {
			System.Diagnostics.Debug.WriteLine($"Failed to parse OSC color response '{response}': {ex.Message}");
		}

		return null;
	}

	private (int r, int g, int b) EstimateFromEnvironment() {
		string term             = Environment.GetEnvironmentVariable("TERM")?.ToLowerInvariant() ?? "";
		string colorterm        = Environment.GetEnvironmentVariable("COLORTERM")?.ToLowerInvariant() ?? "";
		string termProgram      = Environment.GetEnvironmentVariable("TERM_PROGRAM")?.ToLowerInvariant() ?? "";
		string terminalEmulator = Environment.GetEnvironmentVariable("TERMINAL_EMULATOR")?.ToLowerInvariant() ?? "";
		string sessionType      = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLowerInvariant() ?? "";

		// Check for light theme indicators first
		if (Environment.GetEnvironmentVariable("COLORFGBG")?.Contains("15;0") == true)
			return (255, 255, 255); // Light background detected via COLORFGBG

		// Terminal-specific detection with better heuristics
		return (term, termProgram, terminalEmulator) switch {
			var (t, p, _) when p.Contains("iterm")                                           => DetectITermTheme(),
			var (t, _, _) when t.Contains("alacritty")                                       => DetectAlacrittyTheme(),
			var (_, _, e) when e.Contains("kitty")                                           => (45, 45, 45), // kitty default dark
			var (_, p, _) when p.Contains("windowsterminal")                                 => (12, 12, 12), // WT dark
			var (_, p, _) when p.Contains("wezterm")                                         => (40, 40, 40), // Wezterm dark
			var (_, p, _) when p.Contains("vscode")                                          => (30, 30, 30),
			var (_, p, _) when p.Contains("hyper")                                           => (0, 0, 0),
			var (t, _, _) when t.Contains("screen")                                          => (0, 0, 0),
			var (t, _, _) when t.Contains("xterm") && sessionType == "x11"                   => (46, 52, 64), // GNOME dark
			var (_, _, _) when Environment.GetEnvironmentVariable("KONSOLE_VERSION") != null => (35, 38, 39), // Konsole dark
			var (t, _, _) when t.Contains("xterm")                                           => (0, 0, 0),    // Classic xterm dark
			var (t, _, _) when t.Contains("rxvt")                                            => DetectUrxvtTheme(),
			_                                                                                => (12, 12, 12) // Conservative dark default
		};
	}

	private static (int r, int g, int b) DetectITermTheme() {
		// Check for iTerm2 theme hints
		string? itermProfile = Environment.GetEnvironmentVariable("ITERM_PROFILE");
		return itermProfile?.ToLowerInvariant() switch {
			var p when p?.Contains("light") == true           => (255, 255, 255),
			var p when p?.Contains("solarized light") == true => (253, 246, 227),
			var p when p?.Contains("solarized dark") == true  => (0, 43, 54),
			_                                                 => (40, 44, 52) // iTerm2 default dark
		};
	}

	private static (int r, int g, int b) DetectAlacrittyTheme() {
		// Alacritty doesn't expose theme via env vars easily
		// Check for common config patterns or use reasonable defaults
		return (29, 32, 33); // Alacritty default dark
	}

	private (int r, int g, int b) DetectUrxvtTheme() {
		// Check X resources or common urxvt themes
		return (0, 0, 0); // urxvt typically dark
	}

	private static bool IsColorDark((int r, int g, int b) color) {
		// Using relative luminance formula (ITU-R BT.709)
		double luminance = 0.2126 * color.r + 0.7152 * color.g + 0.0722 * color.b;
		return luminance < 128;
	}
}

public enum SemanticColorType {
	Function,
	Class,
	Interface,
	Module,
	Namespace,
	Variable,
	Keyword
}