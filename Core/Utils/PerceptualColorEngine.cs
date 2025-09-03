using System.Text.RegularExpressions;

namespace Thaum.Core.Utils;

/// <summary>
/// Advanced perceptual color engine that generates semantically meaningful colors
/// optimized for any terminal background using color science principles
/// </summary>
public class PerceptualColorEngine
{
    private readonly TerminalColorInfo _terminalInfo;
    private readonly ColorHarmony _harmony;
    
    public PerceptualColorEngine()
    {
        _terminalInfo = DetectTerminalColors();
        _harmony = new ColorHarmony(_terminalInfo.BackgroundColor);
    }
    
    /// <summary>
    /// Generate a perceptually optimal color for the given semantic purpose
    /// </summary>
    public (int r, int g, int b) GenerateSemanticColor(string seed, SemanticColorType colorType)
    {
        return colorType switch
        {
            SemanticColorType.Function => _harmony.GenerateBurntOrange(),
            SemanticColorType.Class => _harmony.GenerateGreen(),
            SemanticColorType.Interface => _harmony.GenerateAnalogous(0.6f),
            SemanticColorType.Module => _harmony.GenerateComplementary(0.4f),
            _ => _harmony.GenerateAnalogous(0.6f)
        };
    }
    
    private TerminalColorInfo DetectTerminalColors()
    {
        // Try to detect terminal background via OSC sequences
        var bgColor = TryDetectBackgroundColor();
        
        if (bgColor.HasValue)
        {
            System.Diagnostics.Debug.WriteLine($"Detected background color: RGB({bgColor.Value.r}, {bgColor.Value.g}, {bgColor.Value.b})");
        }
        else
        {
            bgColor = EstimateFromEnvironment();
            System.Diagnostics.Debug.WriteLine($"Using estimated background color: RGB({bgColor.Value.r}, {bgColor.Value.g}, {bgColor.Value.b})");
        }
        
        var isDark = IsColorDark(bgColor.Value);
        System.Diagnostics.Debug.WriteLine($"Background is {(isDark ? "dark" : "light")}");
        
        return new TerminalColorInfo(bgColor.Value, isDark);
    }
    
    private (int r, int g, int b)? TryDetectBackgroundColor()
    {
        try
        {
            // Save current console state
            var originalInputMode = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;
            
            // Send OSC 11 query for background color
            Console.Write("\u001b]11;?\u001b\\");
            Console.Out.Flush();
            
            // Wait for response with timeout
            var response = ReadOscResponseWithTimeout(1000); // 1 second timeout
            
            // Restore console state
            Console.TreatControlCAsInput = originalInputMode;
            
            if (response != null)
            {
                return ParseOscColorResponse(response);
            }
        }
        catch (Exception ex)
        {
            // Log the error but don't fail - fall back to environment detection
            System.Diagnostics.Debug.WriteLine($"Background color detection failed: {ex.Message}");
        }
        
        return null;
    }
    
    private string? ReadOscResponseWithTimeout(int timeoutMs)
    {
        var buffer = new List<char>();
        var startTime = DateTime.UtcNow;
        var inEscape = false;
        var inOsc = false;
        
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (Console.KeyAvailable)
            {
                var keyInfo = Console.ReadKey(true);
                var ch = keyInfo.KeyChar;
                
                buffer.Add(ch);
                
                // Look for OSC response pattern: ESC ] 11 ; rgb:RRRR/GGGG/BBBB ESC \
                if (ch == '\u001b' && !inEscape)
                {
                    inEscape = true;
                    continue;
                }
                
                if (inEscape && ch == ']')
                {
                    inOsc = true;
                    continue;
                }
                
                if (inOsc && ch == '\u001b')
                {
                    // Look for terminator
                    if (Console.KeyAvailable)
                    {
                        var nextKey = Console.ReadKey(true);
                        buffer.Add(nextKey.KeyChar);
                        
                        if (nextKey.KeyChar == '\\')
                        {
                            // Found complete OSC response
                            return new string(buffer.ToArray());
                        }
                    }
                }
            }
            else
            {
                Thread.Sleep(10); // Small delay to avoid busy waiting
            }
        }
        
        return null;
    }
    
    private (int r, int g, int b)? ParseOscColorResponse(string response)
    {
        try
        {
            // Parse response like: ESC]11;rgb:RRRR/GGGG/BBBB ESC\
            // Or: ESC]11;#RRGGBB ESC\
            
            var match = Regex.Match(response, @"rgb:([0-9a-fA-F]+)/([0-9a-fA-F]+)/([0-9a-fA-F]+)");
            if (match.Success)
            {
                // 16-bit RGB values, convert to 8-bit
                var r16 = Convert.ToInt32(match.Groups[1].Value, 16);
                var g16 = Convert.ToInt32(match.Groups[2].Value, 16);
                var b16 = Convert.ToInt32(match.Groups[3].Value, 16);
                
                // Scale from 16-bit (0-65535) to 8-bit (0-255)
                var r = (int)(r16 * 255.0 / 65535.0);
                var g = (int)(g16 * 255.0 / 65535.0);
                var b = (int)(b16 * 255.0 / 65535.0);
                
                return (r, g, b);
            }
            
            // Try hex format
            var hexMatch = Regex.Match(response, @"#([0-9a-fA-F]{2})([0-9a-fA-F]{2})([0-9a-fA-F]{2})");
            if (hexMatch.Success)
            {
                var r = Convert.ToInt32(hexMatch.Groups[1].Value, 16);
                var g = Convert.ToInt32(hexMatch.Groups[2].Value, 16);
                var b = Convert.ToInt32(hexMatch.Groups[3].Value, 16);
                
                return (r, g, b);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse OSC color response '{response}': {ex.Message}");
        }
        
        return null;
    }
    
    private (int r, int g, int b) EstimateFromEnvironment()
    {
        var term = Environment.GetEnvironmentVariable("TERM")?.ToLowerInvariant() ?? "";
        var colorterm = Environment.GetEnvironmentVariable("COLORTERM")?.ToLowerInvariant() ?? "";
        var termProgram = Environment.GetEnvironmentVariable("TERM_PROGRAM")?.ToLowerInvariant() ?? "";
        var terminalEmulator = Environment.GetEnvironmentVariable("TERMINAL_EMULATOR")?.ToLowerInvariant() ?? "";
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.ToLowerInvariant() ?? "";
        
        // Check for light theme indicators first
        if (Environment.GetEnvironmentVariable("COLORFGBG")?.Contains("15;0") == true)
            return (255, 255, 255); // Light background detected via COLORFGBG
            
        // Terminal-specific detection with better heuristics
        return (term, termProgram, terminalEmulator) switch
        {
            // iTerm2 variations
            var (t, p, _) when p.Contains("iterm") => DetectITermTheme(),
            
            // Alacritty
            var (t, _, _) when t.Contains("alacritty") => DetectAlacrittyTheme(),
            
            // kitty
            var (_, _, e) when e.Contains("kitty") => (45, 45, 45),  // kitty default dark
            
            // Windows Terminal
            var (_, p, _) when p.Contains("windowsterminal") => (12, 12, 12),  // WT dark
            
            // Wezterm
            var (_, p, _) when p.Contains("wezterm") => (40, 40, 40),  // Wezterm dark
            
            // VS Code
            var (_, p, _) when p.Contains("vscode") => (30, 30, 30),
            
            // Hyper
            var (_, p, _) when p.Contains("hyper") => (0, 0, 0),
            
            // tmux/screen
            var (t, _, _) when t.Contains("screen") => (0, 0, 0),
            
            // GNOME Terminal
            var (t, _, _) when t.Contains("xterm") && sessionType == "x11" => (46, 52, 64), // GNOME dark
            
            // Konsole
            var (_, _, _) when Environment.GetEnvironmentVariable("KONSOLE_VERSION") != null => (35, 38, 39), // Konsole dark
            
            // xterm variations
            var (t, _, _) when t.Contains("xterm") => (0, 0, 0),  // Classic xterm dark
            
            // urxvt
            var (t, _, _) when t.Contains("rxvt") => DetectUrxvtTheme(),
            
            _ => (12, 12, 12) // Conservative dark default
        };
    }
    
    private (int r, int g, int b) DetectITermTheme()
    {
        // Check for iTerm2 theme hints
        var itermProfile = Environment.GetEnvironmentVariable("ITERM_PROFILE");
        return itermProfile?.ToLowerInvariant() switch
        {
            var p when p?.Contains("light") == true => (255, 255, 255),
            var p when p?.Contains("solarized light") == true => (253, 246, 227),
            var p when p?.Contains("solarized dark") == true => (0, 43, 54),
            _ => (40, 44, 52) // iTerm2 default dark
        };
    }
    
    private (int r, int g, int b) DetectAlacrittyTheme()
    {
        // Alacritty doesn't expose theme via env vars easily
        // Check for common config patterns or use reasonable defaults
        return (29, 32, 33); // Alacritty default dark
    }
    
    private (int r, int g, int b) DetectUrxvtTheme()
    {
        // Check X resources or common urxvt themes
        return (0, 0, 0); // urxvt typically dark
    }
    
    private static bool IsColorDark((int r, int g, int b) color)
    {
        // Using relative luminance formula (ITU-R BT.709)
        var luminance = 0.2126 * color.r + 0.7152 * color.g + 0.0722 * color.b;
        return luminance < 128;
    }
}

/// <summary>
/// Generates harmonious color families based on color theory and perceptual science
/// </summary>
public class ColorHarmony
{
    private readonly (int r, int g, int b) _baseColor;
    private readonly (float h, float s, float l) _baseHsl;
    private readonly bool _isDarkBackground;
    
    public ColorHarmony((int r, int g, int b) backgroundColor)
    {
        _baseColor = backgroundColor;
        _baseHsl = RgbToHsl(backgroundColor);
        _isDarkBackground = IsColorDark(backgroundColor);
    }
    
    /// <summary>
    /// Generate green that harmonizes with background for classes
    /// </summary>
    public (int r, int g, int b) GenerateGreen()
    {
        // Find the perceptually optimal green hue for this background
        var optimalGreenHue = ComputeOptimalGreenHue();
        
        // Much more subtle for better readability - like 15% opacity
        var saturation = _isDarkBackground ? 0.4f : 0.6f;  // Lower saturation on dark
        var lightness = _isDarkBackground ? 0.25f : 0.7f;  // Much darker on dark bg, lighter on light bg
            
        return HslToRgb((optimalGreenHue, saturation, lightness));
    }
    
    /// <summary>
    /// Generate burnt orange/sienna that harmonizes with background for functions
    /// </summary>
    public (int r, int g, int b) GenerateBurntOrange()
    {
        // Find the perceptually optimal orange hue for this background  
        var optimalOrangeHue = ComputeOptimalOrangeHue();
        
        // Much more subtle for better readability - like 15% opacity
        var saturation = _isDarkBackground ? 0.45f : 0.65f;  // Lower saturation on dark
        var lightness = _isDarkBackground ? 0.3f : 0.65f;    // Much darker on dark bg, lighter on light bg
            
        return HslToRgb((optimalOrangeHue, saturation, lightness));
    }
    
    /// <summary>
    /// Generate complementary colors
    /// </summary>
    public (int r, int g, int b) GenerateComplementary(float saturationTarget)
    {
        var complementaryHue = (_baseHsl.h + 180) % 360;
        var saturation = saturationTarget;
        var lightness = _isDarkBackground ? 0.65f : 0.35f;
        
        return HslToRgb((complementaryHue, Math.Clamp(saturation, 0.3f, 0.8f), lightness));
    }
    
    /// <summary>
    /// Generate analogous colors
    /// </summary>
    public (int r, int g, int b) GenerateAnalogous(float saturationTarget)
    {
        var analogousOffset = 30f; // 30° offset
        var finalHue = (_baseHsl.h + analogousOffset) % 360;
        if (finalHue < 0) finalHue += 360;
        
        var saturation = saturationTarget;
        var lightness = _isDarkBackground ? 0.7f : 0.3f;
        
        return HslToRgb((finalHue, saturation, lightness));
    }
    
    private float ComputeOptimalGreenHue()
    {
        // Compute the green hue that provides optimal contrast with background
        var bgHue = _baseHsl.h;
        
        // Adjust green based on background color temperature
        return bgHue switch
        {
            >= 0 and <= 60 => 140,     // Red/orange bg -> blue-green
            >= 60 and <= 120 => 160,   // Yellow bg -> forest green
            >= 120 and <= 180 => 90,   // Green/cyan bg -> yellow-green (avoid similar hues)
            >= 180 and <= 240 => 120,  // Blue bg -> pure green
            >= 240 and <= 300 => 100,  // Purple bg -> lime green
            _ => 130                    // Magenta/red bg -> emerald green
        };
    }
    
    private float ComputeOptimalOrangeHue()
    {
        // Compute the burnt orange/sienna hue that provides optimal contrast
        var bgHue = _baseHsl.h;
        
        // Adjust orange based on background, trending toward sienna/burnt orange
        return bgHue switch
        {
            >= 0 and <= 60 => 25,      // Red bg -> burnt orange (slightly different hue)
            >= 60 and <= 120 => 15,    // Yellow bg -> red-orange/sienna
            >= 120 and <= 180 => 30,   // Green bg -> burnt orange
            >= 180 and <= 240 => 20,   // Blue bg -> reddish orange
            >= 240 and <= 300 => 35,   // Purple bg -> orange
            _ => 25                     // Default burnt orange
        };
    }
    
    private static bool IsColorDark((int r, int g, int b) color)
    {
        var luminance = 0.2126 * color.r + 0.7152 * color.g + 0.0722 * color.b;
        return luminance < 128;
    }
    
    // Color space conversion utilities
    private static (float h, float s, float l) RgbToHsl((int r, int g, int b) rgb)
    {
        float r = rgb.r / 255f;
        float g = rgb.g / 255f;
        float b = rgb.b / 255f;
        
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        
        var l = (max + min) / 2f;
        
        if (delta == 0)
            return (0, 0, l); // Grayscale
            
        var s = l > 0.5f ? delta / (2 - max - min) : delta / (max + min);
        
        var h = (max == r) ? (g - b) / delta + (g < b ? 6 : 0) :
                (max == g) ? (b - r) / delta + 2 :
                             (r - g) / delta + 4;
        h *= 60;
        
        return (h, s, l);
    }
    
    private static (int r, int g, int b) HslToRgb((float h, float s, float l) hsl)
    {
        var c = (1 - Math.Abs(2 * hsl.l - 1)) * hsl.s;
        var x = c * (1 - Math.Abs((hsl.h / 60) % 2 - 1));
        var m = hsl.l - c / 2;
        
        var (r, g, b) = (hsl.h / 60) switch
        {
            >= 0 and < 1 => (c, x, 0f),
            >= 1 and < 2 => (x, c, 0f),
            >= 2 and < 3 => (0f, c, x),
            >= 3 and < 4 => (0f, x, c),
            >= 4 and < 5 => (x, 0f, c),
            _ => (c, 0f, x)
        };
        
        return ((int)Math.Round((r + m) * 255), 
                (int)Math.Round((g + m) * 255), 
                (int)Math.Round((b + m) * 255));
    }
}

public enum SemanticColorType
{
    Function,
    Class, 
    Interface,
    Module,
    Namespace,
    Variable,
    Keyword
}

public record TerminalColorInfo((int r, int g, int b) BackgroundColor, bool IsDark);