using Ratatui;
using Ratatui.Layout;

namespace Thaum.App.RatatuiTUI;

/// <summary>
/// Ergonomic helpers for constructing Ratatui widgets with C#-friendly defaults.
/// Import statically as: using static Thaum.App.RatatuiTUI.Rat;
/// Then call: List("Title", title_border: true), ListState(selected: 3, offset: 10), Paragraph("text", title: "Info").
/// Or call with Rat.List(...).
/// </summary>
internal static class Rat {
	// Paragraph helpers
	public static Paragraph Paragraph(
		string  text         = "",
		string? title        = null,
		bool    title_border = false) {
		Paragraph p                         = new Paragraph(text);
		if (!string.IsNullOrEmpty(title)) p = p.Title(title!, border: title_border);
		return p;
	}

	// List helpers
	public static List List(
		string? title            = null,
		bool    title_border     = false,
		string? highlight_symbol = null,
		Style?  highlight_style  = null) {
		List l                                         = new List();
		if (!string.IsNullOrEmpty(title)) l            = l.Title(title!, border: title_border);
		if (!string.IsNullOrEmpty(highlight_symbol)) l = l.HighlightSymbol(highlight_symbol!);
		if (highlight_style is Style hs) l             = l.HighlightStyle(hs);
		return l;
	}

	public static ListState ListState(int? selected = null, int? offset = null) {
		ListState st              = new ListState();
		if (selected.HasValue) st = st.Selected(selected.Value);
		if (offset.HasValue) st   = st.Offset(offset.Value);
		return st;
	}

	// Batch append utility for Ratatui.List from strings (simple lines)
	public static void AppendItems(this List list, IEnumerable<string> items) {
		foreach (string s in items) list.AppendItem(s);
	}

	// Style helpers (compile-time friendly inline definitions)
	public static Style S(
		Color? fg        = null,
		Color? bg        = null,
		bool   bold      = false,
		bool   dim       = false,
		bool   italic    = false,
		bool   underline = false)
		=> new Style(fg ?? default, bg ?? default, bold, dim, italic, underline);
}

// layout sugar
internal static class RatLayout {
	// Prefer span-based overloads to avoid params/allocations
	public static IReadOnlyList<Rect> H(Rect area, ReadOnlySpan<Constraint> cs, int gap = 0, int margin = 0)
		=> Layout.SplitHorizontal(area, cs, gap: gap, margin: margin);

	public static IReadOnlyList<Rect> V(Rect area, ReadOnlySpan<Constraint> cs, int gap = 0, int margin = 0)
		=> Layout.SplitVertical(area, cs, gap: gap, margin: margin);

	public static Rect R(int x, int y, int w, int h) => new Rect(x, y, w, h);
}