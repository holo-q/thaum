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
			string? response = ReadOscResponseWithTimeout(1000); // 1 second timeout

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
		DateTime    startTime = DateTime.UtcNow;
		bool    inEscape  = false;
		bool    inOsc     = false;

		while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs) {
			if (Console.KeyAvailable) {
				ConsoleKeyInfo keyInfo = Console.ReadKey(true);
				char ch      = keyInfo.KeyChar;

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

/// <summary>
/// Generates harmonious color families based on color theory and perceptual science
/// </summary>
public class ColorHarmony {
	private readonly (int r, int g, int b)       _baseColor;
	private readonly (float h, float s, float l) _baseHsl;
	private readonly bool                        _isDarkBackground;

	public ColorHarmony((int r, int g, int b) backgroundColor) {
		_baseColor        = backgroundColor;
		_baseHsl          = RgbToHsl(backgroundColor);
		_isDarkBackground = IsColorDark(backgroundColor);
	}

	/// <summary>
	/// Generate green that harmonizes with background for classes
	/// </summary>
	public (int r, int g, int b) GenerateGreen() {
		// Find the perceptually optimal green hue for this background
		float optimalGreenHue = ComputeOptimalGreenHue();

		// Much more subtle for better readability - like 15% opacity
		float saturation = _isDarkBackground ? 0.4f : 0.6f;  // Lower saturation on dark
		float lightness  = _isDarkBackground ? 0.25f : 0.7f; // Much darker on dark bg, lighter on light bg

		return HslToRgb((optimalGreenHue, saturation, lightness));
	}

	/// <summary>
	/// Generate burnt orange/sienna that harmonizes with background for functions
	/// </summary>
	public (int r, int g, int b) GenerateBurntOrange() {
		// Find the perceptually optimal orange hue for this background
		float optimalOrangeHue = ComputeOptimalOrangeHue();

		// Much more subtle for better readability - like 15% opacity
		float saturation = _isDarkBackground ? 0.45f : 0.65f; // Lower saturation on dark
		float lightness  = _isDarkBackground ? 0.3f : 0.65f;  // Much darker on dark bg, lighter on light bg

		return HslToRgb((optimalOrangeHue, saturation, lightness));
	}

	/// <summary>
	/// Generate complementary colors
	/// </summary>
	public (int r, int g, int b) GenerateComplementary(float saturationTarget) {
		float complementaryHue = (_baseHsl.h + 180) % 360;
		float saturation       = saturationTarget;
		float lightness        = _isDarkBackground ? 0.65f : 0.35f;

		return HslToRgb((complementaryHue, Math.Clamp(saturation, 0.3f, 0.8f), lightness));
	}

	/// <summary>
	/// Generate analogous colors
	/// </summary>
	public (int r, int g, int b) GenerateAnalogous(float saturationTarget) {
		float analogousOffset        = 30f; // 30° offset
		float finalHue               = (_baseHsl.h + analogousOffset) % 360;
		if (finalHue < 0) finalHue += 360;

		float saturation = saturationTarget;
		float lightness  = _isDarkBackground ? 0.7f : 0.3f;

		return HslToRgb((finalHue, saturation, lightness));
	}

	private float ComputeOptimalGreenHue() {
		// Compute the green hue that provides optimal contrast with background
		float bgHue = _baseHsl.h;

		// Adjust green based on background color temperature
		return bgHue switch {
			>= 0 and <= 60    => 140, // Red/orange bg -> blue-green
			>= 60 and <= 120  => 160, // Yellow bg -> forest green
			>= 120 and <= 180 => 90,  // Green/cyan bg -> yellow-green (avoid similar hues)
			>= 180 and <= 240 => 120, // Blue bg -> pure green
			>= 240 and <= 300 => 100, // Purple bg -> lime green
			_                 => 130  // Magenta/red bg -> emerald green
		};
	}

	private float ComputeOptimalOrangeHue() {
		// Compute the burnt orange/sienna hue that provides optimal contrast
		float bgHue = _baseHsl.h;

		// Adjust orange based on background, trending toward sienna/burnt orange
		return bgHue switch {
			>= 0 and <= 60    => 25, // Red bg -> burnt orange (slightly different hue)
			>= 60 and <= 120  => 15, // Yellow bg -> red-orange/sienna
			>= 120 and <= 180 => 30, // Green bg -> burnt orange
			>= 180 and <= 240 => 20, // Blue bg -> reddish orange
			>= 240 and <= 300 => 35, // Purple bg -> orange
			_                 => 25  // Default burnt orange
		};
	}

	private static bool IsColorDark((int r, int g, int b) color) {
		double luminance = 0.2126 * color.r + 0.7152 * color.g + 0.0722 * color.b;
		return luminance < 128;
	}

	// Color space conversion utilities
	private static (float h, float s, float l) RgbToHsl((int r, int g, int b) rgb) {
		float r = rgb.r / 255f;
		float g = rgb.g / 255f;
		float b = rgb.b / 255f;

		float max   = Math.Max(r, Math.Max(g, b));
		float min   = Math.Min(r, Math.Min(g, b));
		float delta = max - min;

		float l = (max + min) / 2f;

		if (delta == 0)
			return (0, 0, l); // Grayscale

		float s = l > 0.5f ? delta / (2 - max - min) : delta / (max + min);

		float h = (max == r) ? (g - b) / delta + (g < b ? 6 : 0) :
			(max == g)     ? (b - r) / delta + 2 :
			                 (r - g) / delta + 4;
		h *= 60;

		return (h, s, l);
	}

	private static (int r, int g, int b) HslToRgb((float h, float s, float l) hsl) {
		float c = (1 - Math.Abs(2 * hsl.l - 1)) * hsl.s;
		float x = c * (1 - Math.Abs((hsl.h / 60) % 2 - 1));
		float m = hsl.l - c / 2;

		(float r, float g, float b) = (hsl.h / 60) switch {
			>= 0 and < 1 => (c, x, 0f),
			>= 1 and < 2 => (x, c, 0f),
			>= 2 and < 3 => (0f, c, x),
			>= 3 and < 4 => (0f, x, c),
			>= 4 and < 5 => (x, 0f, c),
			_            => (c, 0f, x)
		};

		return ((int)Math.Round((r + m) * 255),
			(int)Math.Round((g + m) * 255),
			(int)Math.Round((b + m) * 255));
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