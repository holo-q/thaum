using Microsoft.Extensions.Logging;
using Thaum.Core.Models;
using Thaum.Core.Services;
using Ratatui;
using Ratatui.Layout;
using Thaum.Utils;

namespace Thaum.App.RatatuiTUI;

public class ThaumTUI {
	public sealed class State {
		public Panel   focus = Panel.Files;
		public Screen? active;

		public List<string> allFiles     = [];
		public List<string> visibleFiles = [];
		public int          fileSelected;
		public int          fileOffset;
		public string       fileFilter = string.Empty;

		public List<CodeSymbol> allSymbols     = [];
		public List<CodeSymbol> visibleSymbols = [];
		public int              symSelected;
		public int              symOffset;
		public string           symFilter = string.Empty;

		public string? summary;
		public bool    isLoading;

		// Operation screens state
		public List<string>? sourceLines;
		public int           sourceSelected;
		public int           sourceOffset;

		public List<CodeRef>? refs;
		public int            refsSelected;
		public int            refsOffset;
	}

    private string _projectPath;
    private State  _app;
    private Screen _browser, _source, _summary, _refs, _info;

    // Expose screen references for keybinding navigation
    public Screen ScreenBrowser    => _browser;
    public Screen ScreenSource     => _source;
    public Screen ScreenSummary    => _summary;
    public Screen ScreenReferences => _refs;
    public Screen ScreenInfo       => _info;

	private readonly Crawler _crawler;
	private readonly Golfer  _golfer;
	private readonly ILogger _logger;

	private bool _dirty;

	public enum Panel { Files, Symbols }

	public ThaumTUI(string projectPath, CodeMap codeMap, Crawler crawler, Golfer golfer) {
		this._projectPath = projectPath;

		List<CodeSymbol> allSymbols = codeMap.ToList()
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

		_browser = new BrowserScreen(this, opener, projectPath);
		_source  = new SourceScreen(this, opener, projectPath);
		_summary = new SummaryScreen(this, opener, projectPath);
		_refs    = new ReferencesScreen(this, opener, projectPath);
		_info    = new InfoScreen(this, opener, projectPath);

		_logger = Logging.For<ThaumTUI>();
	}

	public void NavigateTo(Screen? target, State app) {
		if (target is null) return;
		app.active = target;
	}

	public async Task RunAsync() {
		using Terminal term = new Terminal().Raw().AltScreen().ShowCursor(false);

		_app.visibleFiles = _app.allFiles.ToList();
		string? firstFile = _app.visibleFiles.FirstOrDefault();
		_app.visibleSymbols = firstFile == null ? [] : SymbolsForFile(_app, firstFile).ToList();

		// Browser lists are now built inside BrowserScreen; no local ListState needed.
		TimeSpan poll = TimeSpan.FromMilliseconds(75);
		_app.active = _browser;

		Screen lastScreen = _app.active;
		await lastScreen.OnEnter(_app);

		// initial OnEnter already called above

		while (true) {
			if (_dirty) {
				Draw(term);
				_dirty = false;
			}

			if (!term.NextEvent(poll, out Event ev)) {
				// Tick update for animations/status
				// Use a lightweight dt based on poll interval
				(_app.active ?? _browser).OnTick(poll, _app);
				if (_app.isLoading)
					Invalidate();
				continue;
			}

			switch (ev.Kind) {
				case EventKind.Resize:
					Invalidate();
					(int Width, int Height) size = term.Size();
					(_app.active ?? _browser).OnResize(size.Width, size.Height, _app);
					continue;
				case EventKind.Key:
					if ((_app.active ?? _browser).HandleKey(ev, _app)) {
						Invalidate();
					}
					break;
			}

			// Screen lifecycle
			if ((_app.active ?? _browser) != lastScreen) {
				Screen old  = lastScreen;
				Screen @new = _app.active ?? _browser;
				await old.OnLeave(_app);
				await @new.OnEnter(_app);
				lastScreen = @new;
				Invalidate();
			}
		}
	}

	public void Invalidate() {
		_dirty = true;
	}

	internal async Task<string> LoadSymbolDetail(CodeSymbol sym) {
		try {
			string? source = await _crawler.GetCode(sym);
			if (string.IsNullOrEmpty(source)) return "No source available for symbol.";
			OptimizationContext ctx = new OptimizationContext(
				Level: sym.Kind is SymbolKind.Function or SymbolKind.Method ? 1 : 2,
				AvailableKeys: [],
				PromptName: GLB.GetDefaultPrompt(sym));
			return await _golfer.RewriteAsync(sym, ctx, source);
		} catch (Exception ex) {
			_logger.LogError(ex, "Error summarizing symbol {Name}", sym.Name);
			return $"Error: {ex.Message}";
		}
	}

	private void Draw(Terminal term) {
		(int w, int h) = term.Size();
		Rect area = Rect.FromSize(Math.Max(1, w), Math.Max(1, h));

		IReadOnlyList<Rect> rows = RatLayout.H(area, [
			Constraint.Length(3),
			Constraint.Percentage(100),
			Constraint.Length(1)
		]);

		Screen          active = _app.active ?? _browser;
		using Paragraph header = Rat.Paragraph("", title: $"Thaum — {Path.GetFileName(_projectPath)}  {ComposeHeaderTitle(active, _app)}");
		term.Draw(header, rows[0]);

		(_app.active ?? _browser).Draw(term, rows[1], _app, _projectPath);

		using Paragraph footer = MakeFooter(_app, active);
		term.Draw(footer, rows[2]);
	}

	private string ComposeHeaderTitle(Screen active, State app) {
		string title                                                            = active.Title(app);
		if (active is { IsBusy: true }) title                                   += $"  {TuiTheme.Spinner()}";
		if (!string.IsNullOrWhiteSpace((active as Screen)?.ErrorMessage)) title += "  [error]";
		return title;
	}

	// header built inline
	private Paragraph MakeFooter(State app, Screen active) {
		// Prefer key bindings if provided; fallback to FooterHint
		// TODO no LINQ allowed unless its some superfast special version
		IEnumerable<string> bindings = active.GetHelp(6).Select(b => $"{b.Key} {b.Description}");
		string              hint     = bindings.Any() ? string.Join("   ", bindings) : active.FooterHint(app);
		Paragraph           p        = Rat.Paragraph(hint);
		if (!string.IsNullOrWhiteSpace(active.ErrorMessage)) p.AppendSpan($"   Error: {active.ErrorMessage}", TuiTheme.Error);
		return p.AppendSpan("   Ratatui.cs", TuiTheme.Hint);
	}

	private IEnumerable<CodeSymbol> SymbolsForFile(State app, string? file)
		=> string.IsNullOrEmpty(file) ? app.allSymbols : app.allSymbols.Where(s => s.FilePath == file);

	private void ApplyFileFilter(State app) {
		if (string.IsNullOrWhiteSpace(app.fileFilter)) app.visibleFiles = app.allFiles.ToList();
		else {
			string f = app.fileFilter.ToLowerInvariant();
			app.visibleFiles = app.allFiles.Where(p => p.ToLowerInvariant().Contains(f)).ToList();
		}
		app.fileSelected = 0;
		app.fileOffset   = 0;
		app.summary      = null;
		string? file = app.visibleFiles.FirstOrDefault();
		app.visibleSymbols = file == null ? [] : SymbolsForFile(app, file).ToList();
		app.symSelected    = 0;
		app.symOffset      = 0;
	}

	private void ApplySymbolFilter(State app) {
		string?                 file    = app.visibleFiles.Count == 0 ? null : app.visibleFiles[Math.Min(app.fileSelected, app.visibleFiles.Count - 1)];
		IEnumerable<CodeSymbol> baseSet = SymbolsForFile(app, file);
		if (string.IsNullOrWhiteSpace(app.symFilter)) app.visibleSymbols = baseSet.ToList();
		else {
			string f = app.symFilter.ToLowerInvariant();
			app.visibleSymbols = baseSet.Where(s => s.Name.ToLowerInvariant().Contains(f)).ToList();
		}
		app.symSelected = 0;
		app.symOffset   = 0;
		app.summary     = null;
	}

	internal async Task EnsureSource(State app) {
		if (app.visibleSymbols.Count == 0) {
			app.sourceLines = [];
			return;
		}
		if (app.sourceLines is { Count: > 0 }) return;
		CodeSymbol s   = app.visibleSymbols[app.symSelected];
		string?    src = await _crawler.GetCode(s);
		app.sourceLines    = (src ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
		app.sourceSelected = 0;
		app.sourceOffset   = 0;
	}

	internal async Task EnsureRefs(State app) {
		if (app.visibleSymbols.Count == 0) {
			app.refs = [];
			return;
		}
		if (app.refs is { Count: > 0 }) return;
		CodeSymbol       s    = app.visibleSymbols[app.symSelected];
		List<CodeSymbol> refs = await _crawler.GetReferencesFor(s.Name, s.StartCodeLoc);
		app.refs         = refs.Select(r => new CodeRef(r.FilePath, r.StartCodeLoc.Line, r.Name)).ToList();
		app.refsSelected = 0;
		app.refsOffset   = 0;
	}

	internal void EnsureVisible(ref int offset, int selected) {
		if (selected < offset)
			offset = selected;
		int max = offset + 20;
		if (selected >= max)
			offset = Math.Max(0, selected - 19);
	}
}
