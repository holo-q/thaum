namespace Thaum.Core.Utils;

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
		float analogousOffset      = 30f; // 30° offset
		float finalHue             = (_baseHsl.h + analogousOffset) % 360;
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
			(max == g)       ? (b - r) / delta + 2 :
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