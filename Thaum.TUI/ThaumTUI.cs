using System.Text;
using Ratatui;
using Ratatui.Reload.Abstractions;
using Thaum.Core;
using Thaum.Core.Crawling;
using Thaum.Meta;
using static Thaum.App.RatatuiTUI.Styles;

namespace Thaum.App.RatatuiTUI;

[LoggingIntrinsics]
public partial class ThaumTUI : RatTUI<ThaumTUI>, IReloadableApp {
	public enum Panel {
		Files,
		Symbols
	}

	public readonly string projectPath;

	public ThaumModel model;

	public BrowserScreen    scrBrowser;
	public SourceScreen     scrSource;
	public SummaryScreen    scrSummary;
	public ReferencesScreen scrReferences;
	public InfoScreen       scrMode;

	// Parameterless constructor for hot-reload instantiation
	public ThaumTUI() {
		// Initialize with placeholder data - will be replaced when Initialize() is called
		projectPath = ".";
		model       = new ThaumModel();
		InitializeScreens();
	}

	public ThaumTUI(string projectPath, CodeMap codemap, Crawler crawler, Defragmentor defrag) {
		this.projectPath = projectPath;

		List<CodeSymbol> allSymbols = codemap
			.ToList()
			.OrderBy(s => (s.FilePath, s.StartCodeLoc.Line))
			.ToList();

		if (allSymbols.Count == 0) {
			Console.WriteLine("No symbols to display");
			return;
		}

		model = new ThaumModel {
			allSymbols = allSymbols,
			allFiles   = allSymbols.Select(s => s.FilePath).Distinct().OrderBy(x => x).ToList(),
			summary    = null,
			_crawler   = crawler,
			defrag     = defrag
		};

		// Constructors now aligned - all screens just take ThaumTUI
		scrBrowser    = new BrowserScreen(this);
		scrSource     = new SourceScreen(this);
		scrSummary    = new SummaryScreen(this);
		scrReferences = new ReferencesScreen(this);
		scrMode       = new InfoScreen(this);

		// TODO does not take a handler, it instead sets a field on itself which is handled in GetNextState
		keys.RegisterScreen('1', scrBrowser, "browser", (tui,       screen) => tui.Navigate(screen));
		keys.RegisterScreen('2', scrSource, "source", (tui,         screen) => tui.Navigate(screen));
		keys.RegisterScreen('3', scrSummary, "summary", (tui,       screen) => tui.Navigate(screen));
		keys.RegisterScreen('4', scrReferences, "references", (tui, screen) => tui.Navigate(screen));
		keys.RegisterScreen('5', scrMode, "info", (tui,             screen) => tui.Navigate(screen));
		// TODO same for this, different field
		keys.RegisterExits(escape: true, q: true, ctrlC: true);
	}

	internal async Task<string> LoadSymbolDetail(CodeSymbol sym) {
		try {
			string? src = await model._crawler.GetCode(sym);
			if (string.IsNullOrEmpty(src))
				return "No source available for symbol.";

			OptimizationContext ctx = new OptimizationContext(
				Level: sym.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
				AvailableKeys: [],
				PromptName: GLB.GetDefaultPrompt(sym));

			return await model.defrag.RewriteAsync(sym, ctx, src);
		} catch (Exception ex) {
			err(ex, "Error summarizing symbol {Name}", sym.Name);
			return $"Error: {ex.Message}";
		}
	}

	public override void OnDraw(Terminal tm) {
		// Compute explicit header/content/footer rects to avoid any potential
		// layout engine edge-cases causing overlap or clears between draws.
		Vec2 viewport = tm.SizeVec().EnsureMin();

		const int H_HEADER = 1;
		const int H_FOOTER = 1;

		Rect frame = Rat.rect_sz(Vec2.zero, viewport);

		(Rect r1, Rect r2, Rect r3) = frame.SplitTopBottom(H_HEADER, H_FOOTER);
		Paragraph header = ComposeHeader();
		Paragraph footer = ComposeFooter();

		CurrentScreen.Draw(tm, r2);
		tm.Draw(header, r1);
		tm.Draw(footer, r3);
	}

	private Paragraph ComposeHeader() {
		Screen screen = CurrentScreen;
		string title  = screen.TitleMsg;
		if (screen.TaskBusyMsg)
			title += $"  {Spinner()}";
		if (!string.IsNullOrWhiteSpace(screen.ErrMsg))
			title += "  [error]";

		return Rat.Paragraph($"Thaum — {Path.GetFileName(projectPath)}  {title}");
	}

	private Paragraph ComposeFooter() {
		Screen screen = CurrentScreen ?? scrBrowser;
		// Prefer key bindings if provided; fallback to FooterHint
		IReadOnlyList<KeyBinding> help = keys.GetHelp(6);
		string                    hint;
		if (help.Count > 0) {
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < help.Count; i++) {
				if (i > 0)
					sb.Append("   ");
				KeyBinding b = help[i];
				sb.Append(b.Key);
				sb.Append(' ');
				sb.Append(b.Description);
			}
			hint = sb.ToString();
		} else {
			hint = screen.FooterMsg;
		}

		Paragraph ret = Rat.Paragraph(hint);
		if (Environment.GetEnvironmentVariable("THAUM_TUI_DEBUG_KEYS") == "1")
			ret += S_HINT | $"  | Focus={(model.focus == Panel.Files ? "Files" : "Symbols")} F sel={model.SelectedFile} off={model.fileOffset}  S sel={model.SelectedSymbol} off={model.symOffset}";
		if (!string.IsNullOrWhiteSpace(screen.ErrMsg))
			ret += S_ERROR | $"   Error: {screen.ErrMsg}";
		ret += S_HINT | "   Ratatui.cs";
		return ret;
	}


	public void EnsureVisible(ref int offset, int selected) {
		if (selected < offset)
			offset = selected;
		int max = offset + 20;
		if (selected >= max)
			offset = Math.Max(0, selected - 19);
	}

	// External-driver lifecycle (for hot reload host)
	public async Task PrepareAsync() {
		model.visibleFiles.Reset(model.allFiles, 0);
		string? firstFile = model.visibleFiles.SafeSelected;
		if (firstFile is null)
			model.visibleSymbols.Clear();
		else
			model.visibleSymbols.Reset(model.SymbolsForFile(firstFile), 0);

		CurrentScreen = scrBrowser;
		Screen lastScreen = CurrentScreen;
		await lastScreen.OnEnter();
		invalidated = true;
	}

	public override void OnResize(int width, int height) {
		CurrentScreen.OnResize(width, height);
		Invalidate();
	}

	public override bool OnEvent(Event ev) {
		switch (ev.Kind) {
			case EventKind.Resize:
				Vec2 s = ev.Size();
				OnResize(s.w, s.h);
				return true;
			case EventKind.Mouse:
				Invalidate();
				return true;
			case EventKind.Key:
				bool handled = CurrentScreen.OnKey(ev);
				if (handled) Invalidate();
				return handled;
		}
		return false;
	}

	public override void OnTick(TimeSpan dt) {
		CurrentScreen.Tick(dt);
	}

	private void InitializeScreens() {
		scrBrowser    = new BrowserScreen(this);
		scrSource     = new SourceScreen(this);
		scrSummary    = new SummaryScreen(this);
		scrReferences = new ReferencesScreen(this);
		scrMode       = new InfoScreen(this);
	}

	// IReloadableApp implementation
	public async Task<bool> RunAsync(Terminal terminal, CancellationToken cancellationToken) {
		// This method is called by RatHost, but the actual event loop is handled by RatHost itself
		// We just need to return true to indicate successful initialization
		await Task.CompletedTask;
		return true;
	}
}