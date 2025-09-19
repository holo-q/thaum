using System;
using System.Linq;
using Ratatui;
using Ratatui.Layout;

namespace Ratatui.Reload;

public readonly record struct HotReloadState(
	bool       Building,
	bool       BuildFailed,
	bool       ChangesPending,
	string     LastBuildLog,
	DateTime?  LastSuccessUtc,
	ConsoleKey ReloadKey
);

public interface IHotReloadUi {
	void OnResize(int width, int height);

	// Return true if the host UI consumed the event
	bool HandleEvent(Event ev);

	// Draw the frame; call drawPlugin() at the appropriate place
	void Draw(Terminal term, HotReloadState state, Action drawPlugin);
}

internal sealed class DevHostUi : IHotReloadUi {
	private bool _consoleVisible;
	private int  _consoleHeight = 6;

	public void OnResize(int width, int height) {
		// Clamp console height to viewport
		_consoleHeight = Math.Max(3, Math.Min(12, height / 3));
	}

	public bool HandleEvent(Event ev) {
		if (ev.Kind == EventKind.Key) {
			if (ev.Key.CodeEnum == KeyCode.F12) {
				_consoleVisible = !_consoleVisible;
				return true;
			}
		}
		return false;
	}

	public void Draw(Terminal term, HotReloadState st, Action drawPlugin) {
		(int W0, int H0) = term.Size();
		int W = Math.Max(1, W0);
		int H = Math.Max(1, H0);

		// Layout: header (1), footer (1), optional console bottom panel
		int headerH  = 1;
		int footerH  = 1;
		int consoleH = _consoleVisible ? Math.Min(_consoleHeight, Math.Max(1, H - headerH - footerH - 1)) : 0;
		int pluginH  = Math.Max(1, H - headerH - footerH - consoleH);

		Rect rHeader  = new Rect(0, 0, W, headerH);
		Rect rPlugin  = new Rect(0, headerH, W, pluginH);
		Rect rConsole = new Rect(0, headerH + pluginH, W, consoleH);
		Rect rFooter  = new Rect(0, H - footerH, W, footerH);

		// Header: build state + time since last success
		using (Paragraph header = new Paragraph("")) {
			string state = st.Building ? "building…" : st.BuildFailed  ? "failed" : "ok";
			Color    color = st.Building ? Color.Yellow : st.BuildFailed ? Color.LightRed : Color.LightGreen;
			header.AppendSpan("DevHost • ", new Style(fg: Color.Gray));
			header.AppendSpan(state, new Style(fg: color, bold: true));
			if (st.LastSuccessUtc.HasValue) {
				TimeSpan ago = (DateTime.UtcNow - st.LastSuccessUtc.Value);
				header.AppendSpan($"  last: {FormatAgo(ago)}", new Style(fg: Color.Gray));
			}
			if (st.ChangesPending) {
				header.AppendSpan($"  ⟳ changes pending — press {(char)st.ReloadKey}", new Style(fg: Color.LightYellow));
			}
			term.Draw(header, rHeader);
		}

		// Plugin area
		drawPlugin();

		// Console panel (build log tail)
		if (_consoleVisible && consoleH > 0) {
			int       maxLines = Math.Max(1, consoleH);
			string[]  lines    = st.LastBuildLog?.Split('\n') ?? Array.Empty<string>();
			string       tail     = string.Join('\n', lines.TakeLast(maxLines));
			using Paragraph p        = new Paragraph(tail).Title("Console", border: true);
			term.Draw(p, rConsole);
		}

		// Footer: hints
		using (Paragraph footer = new Paragraph("")) {
			if (st.Building) {
				footer.AppendSpan(" ⟳ building… ", new Style(fg: Color.Yellow));
			} else if (st.BuildFailed) {
				footer.AppendSpan(" build failed — check console (F12) ", new Style(fg: Color.LightRed));
			} else {
				footer.AppendSpan(" F12 dev console  ", new Style(fg: Color.Gray));
				footer.AppendSpan($" reload {(char)st.ReloadKey} ", new Style(fg: Color.Gray));
			}
			term.Draw(footer, rFooter);
		}
	}

	private static string FormatAgo(TimeSpan ago) {
		if (ago.TotalHours >= 1) return $"{(int)ago.TotalHours}h{ago.Minutes:D2}m";
		if (ago.TotalMinutes >= 1) return $"{(int)ago.TotalMinutes}m{ago.Seconds:D2}s";
		return $"{ago.Seconds}s";
	}
}