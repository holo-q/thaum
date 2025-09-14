using Ratatui;
using Thaum.Core.Models;

namespace Thaum.App.RatatuiTUI;

internal static class TuiTheme
{
    public static readonly Style Hint       = new Style(dim: true);
    public static readonly Style FilePath   = new Style(fg: Color.Cyan);
    public static readonly Style LineNumber = new Style(fg: Color.DarkGray);
    public static readonly Style Error      = new Style(fg: Color.LightRed, bold: true);
    public static readonly Style Success    = new Style(fg: Color.LightGreen, bold: true);
    public static readonly Style Info       = new Style(fg: Color.LightBlue);
    public static readonly Style Title      = new Style(bold: true);
    public static readonly Style CodeHi     = new Style(fg: Color.LightYellow, bold: true);

    public static Style StyleForKind(SymbolKind k) => k switch
    {
        SymbolKind.Class => new Style(fg: Color.LightYellow),
        SymbolKind.Method => new Style(fg: Color.LightGreen),
        SymbolKind.Function => new Style(fg: Color.LightGreen),
        SymbolKind.Interface => new Style(fg: Color.LightBlue),
        SymbolKind.Enum => new Style(fg: Color.Magenta),
        SymbolKind.Property => new Style(fg: Color.White),
        SymbolKind.Field => new Style(fg: Color.White),
        SymbolKind.Variable => new Style(fg: Color.White),
        _ => new Style(fg: Color.Gray)
    };

    public static string Spinner()
    {
        int t = (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 120) % 4);
        return "-\\|/"[t].ToString();
    }
}

