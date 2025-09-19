using System.Text;
using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Ratatui;
using Ratatui.Layout;
using Ratatui.Reload.Abstractions;
using Thaum.Core;
using Thaum.Core.Crawling;
using Thaum.Meta;
using Thaum.App.RatatuiTUI;
using static Thaum.App.RatatuiTUI.Styles;

namespace Thaum.App.RatatuiTUI;

[LoggingIntrinsics]
public partial class ThaumTUI : RatTUI, IReloadableApp {
	public enum Panel {
		Files,
		Symbols
	}

	public sealed class State {
		public Panel  focus = Panel.Files;
		public Screen screen;

		public List<string>     allFiles   = [];
		public List<CodeSymbol> allSymbols = [];

		public RatList<string> visibleFiles { get; } = [];
		public int             fileOffset;
		public string          fileFilter = string.Empty;

		public RatList<CodeSymbol> visibleSymbols { get; } = [];
		public int                 symOffset;
		public string              symFilter = string.Empty;

		public string? summary;
		public bool    isLoading;

		// Operation screens state
		public List<string>? sourceLines;
		public int           sourceSelected;
		public int           sourceOffset;

		public List<CodeRef>? refs;
		public int            refsSelected;
		public int            refsOffset;

		public int SelectedFile {
			get => visibleFiles.SafeSelectedIndex ?? 0;
			set => visibleFiles.SetSelectedIndexSafe(value);
		}

		public int SelectedSymbol {
			get => visibleSymbols.SafeSelectedIndex ?? 0;
			set => visibleSymbols.SetSelectedIndexSafe(value);
		}
	}

	public readonly string projectPath;

	private readonly Crawler _crawler;
	private readonly Golfer  _golfer;

	private State _app;

	public BrowserScreen    scrBrowser;
	public SourceScreen     scrSource;
	public SummaryScreen    scrSummary;
	public ReferencesScreen scrReferences;
	public InfoScreen       scrMode;

	// Parameterless constructor for hot-reload instantiation
	public ThaumTUI() {
		// Initialize with placeholder data - will be replaced when Initialize() is called
		projectPath = ".";
		_crawler = null!;
		_golfer = null!;
		_app = new State();
		InitializeScreens();
	}

	public ThaumTUI(string projectPath, CodeMap codemap, Crawler crawler, Golfer golfer) {
		this.projectPath = projectPath;

		List<CodeSymbol> allSymbols = codemap
			.ToList()
			.OrderBy(s => (s.FilePath, s.StartCodeLoc.Line))
			.ToList();

		if (allSymbols.Count == 0) {
			Console.WriteLine("No symbols to display");
			return;
		}

		_app = new State {
			allSymbols = allSymbols,
			allFiles   = allSymbols.Select(s => s.FilePath).Distinct().OrderBy(x => x).ToList(),
			summary    = null,
			isLoading  = false
		};
		_crawler = crawler;
		_golfer  = golfer;

		DefaultEditorOpener opener = new DefaultEditorOpener();

		scrBrowser    = new BrowserScreen(this, opener, projectPath);
		scrSource     = new SourceScreen(this, opener, projectPath);
		scrSummary    = new SummaryScreen(this, opener, projectPath);
		scrReferences = new ReferencesScreen(this, opener, projectPath);
		scrMode       = new InfoScreen(this, opener, projectPath);
	}

	public void NavigateTo(Screen? target, State app) {
		if (target is null) return;
		app.screen = target;
	}


	internal async Task<string> LoadSymbolDetail(CodeSymbol sym) {
		try {
			string? src = await _crawler.GetCode(sym);
			if (string.IsNullOrEmpty(src))
				return "No source available for symbol.";

			OptimizationContext ctx = new OptimizationContext(
				Level: sym.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
				AvailableKeys: [],
				PromptName: GLB.GetDefaultPrompt(sym));

			return await _golfer.RewriteAsync(sym, ctx, src);
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

		Rect frame = Rat.Rect(Vec2.Zero, viewport);

		(Rect r1, Rect r2, Rect r3) = frame.SplitTopBottom(H_HEADER, H_FOOTER);
		Paragraph header = ComposeHeader();
		Paragraph footer = ComposeFooter();

		_app.screen?.Draw(tm, r2, _app, projectPath);
		tm.Draw(header, r1);
		tm.Draw(footer, r3);
	}

	private Paragraph ComposeHeader() {
		Screen screen = _app.screen;
		string title  = screen.Title(_app);
		if (screen.IsBusy)
			title += $"  {Spinner()}";
		if (!string.IsNullOrWhiteSpace(screen.ErrorMessage))
			title += "  [error]";

		return Rat.Paragraph($"Thaum — {Path.GetFileName(projectPath)}  {title}");
	}

	private Paragraph ComposeFooter() {
		Screen screen = _app.screen ?? scrBrowser;
		// Prefer key bindings if provided; fallback to FooterHint
		IReadOnlyList<KeyBinding> help = screen.GetHelp(6);
		string                    hint;
		if (help.Count > 0) {
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < help.Count; i++) {
				if (i > 0) sb.Append("   ");
				KeyBinding b = help[i];
				sb.Append(b.Key).Append(' ').Append(b.Description);
			}
			hint = sb.ToString();
		} else {
			hint = screen.FooterHint(_app);
		}

		Paragraph ret = Rat.Paragraph(hint);
		if (Environment.GetEnvironmentVariable("THAUM_TUI_DEBUG_KEYS") == "1")
			ret += S_HINT | $"  | Focus={(_app.focus == Panel.Files ? "Files" : "Symbols")} F sel={_app.SelectedFile} off={_app.fileOffset}  S sel={_app.SelectedSymbol} off={_app.symOffset}";
		if (!string.IsNullOrWhiteSpace(screen.ErrorMessage))
			ret += S_ERROR | $"   Error: {screen.ErrorMessage}";
		ret += S_HINT | "   Ratatui.cs";
		return ret;
	}

	public IEnumerable<CodeSymbol> SymbolsForFile(State app, string? file) => string.IsNullOrEmpty(file)
		? app.allSymbols
		: app.allSymbols.Where(s => s.FilePath == file);

	private void ApplyFileFilter(State app) {
		IEnumerable<string> filtered = string.IsNullOrWhiteSpace(app.fileFilter)
			? app.allFiles
			: app.allFiles.Where(p => p.ToLowerInvariant().Contains(app.fileFilter.ToLowerInvariant()));

		app.visibleFiles.Reset(filtered, 0);
		app.fileOffset = 0;
		app.summary    = null;

		string? file = app.visibleFiles.SafeSelected;
		if (file is null) app.visibleSymbols.Clear();
		else app.visibleSymbols.Reset(SymbolsForFile(app, file), 0);
		app.symOffset = 0;
	}

	public async Task EnsureSource(State app) {
		if (app.visibleSymbols.Count == 0) {
			app.sourceLines = [];
			return;
		}

		if (app.sourceLines is { Count: > 0 })
			return;

		CodeSymbol s   = app.visibleSymbols.Selected;
		string?    src = await _crawler.GetCode(s);
		app.sourceLines    = (src ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
		app.sourceSelected = 0;
		app.sourceOffset   = 0;
	}

	public async Task EnsureRefs(State app) {
		if (app.visibleSymbols.Count == 0) {
			app.refs = [];
			return;
		}
		if (app.refs is { Count: > 0 }) return;
		CodeSymbol       s    = app.visibleSymbols.Selected;
		List<CodeSymbol> refs = await _crawler.GetReferencesFor(s.Name, s.StartCodeLoc);
		app.refs         = refs.Select(r => new CodeRef(r.FilePath, r.StartCodeLoc.Line, r.Name)).ToList();
		app.refsSelected = 0;
		app.refsOffset   = 0;
	}

	public void EnsureVisible(ref int offset, int selected) {
		if (selected < offset)
			offset = selected;
		int max = offset + 20;
		if (selected >= max)
			offset = Math.Max(0, selected - 19);
	}

	private void ApplySymbolFilter(ThaumTUI thaumTUI) {
		IEnumerable<CodeSymbol> baseSet = thaumTUI.SymbolsForFile(_app, _app.visibleFiles.SafeSelected);
		IEnumerable<CodeSymbol> filtered = string.IsNullOrWhiteSpace(_app.symFilter)
			? baseSet
			: baseSet.Where(s => s.Name.ToLowerInvariant().Contains(_app.symFilter.ToLowerInvariant()));

		_app.visibleSymbols.Reset(filtered, 0);
		_app.symOffset = 0;
		_app.summary   = null;
	}

	// External-driver lifecycle (for hot reload host)
	public async Task PrepareAsync() {
		_app.visibleFiles.Reset(_app.allFiles, 0);
		string? firstFile = _app.visibleFiles.SafeSelected;
		if (firstFile is null) _app.visibleSymbols.Clear();
		else _app.visibleSymbols.Reset(SymbolsForFile(_app, firstFile), 0);

		_app.screen = scrBrowser;
		Screen lastScreen = _app.screen;
		await lastScreen.OnEnter(_app);
		invalidated = true;
	}

	public override void OnResize(int width, int height) {
		(_app.screen ?? scrBrowser).OnResize(width, height, _app);
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
				bool handled = (_app.screen ?? scrBrowser).HandleKey(ev, _app);
				if (handled) Invalidate();
				return handled;
		}
		return false;
	}

	public override void OnUpdate(TimeSpan dt) {
		(_app.screen ?? scrBrowser).OnTick(dt, _app);
	}

	private void InitializeScreens() {
		DefaultEditorOpener opener = new DefaultEditorOpener();
		scrBrowser    = new BrowserScreen(this, opener, projectPath);
		scrSource     = new SourceScreen(this, opener, projectPath);
		scrSummary    = new SummaryScreen(this, opener, projectPath);
		scrReferences = new ReferencesScreen(this, opener, projectPath);
		scrMode       = new InfoScreen(this, opener, projectPath);
	}

	// IReloadableApp implementation
	public async Task<bool> RunAsync(Terminal terminal, CancellationToken cancellationToken) {
		// This method is called by RatHost, but the actual event loop is handled by RatHost itself
		// We just need to return true to indicate successful initialization
		await Task.CompletedTask;
		return true;
	}
}