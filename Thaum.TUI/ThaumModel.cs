using Ratatui.Sugar;
using Thaum.Core;
using Thaum.Core.Crawling;

namespace Thaum.App.RatatuiTUI;

public sealed class ThaumModel {
	public ThaumTUI.Panel focus = ThaumTUI.Panel.Files;

	public Crawler      _crawler;
	public Defragmentor defrag;

	public List<string>     allFiles   = [];
	public List<CodeSymbol> allSymbols = [];

	public RatList<string> visibleFiles { get; } = [];
	public int             fileOffset;
	public string          fileFilter = string.Empty;

	public RatList<CodeSymbol> visibleSymbols { get; } = [];
	public int                 symOffset;
	public string              symFilter = string.Empty;

	public string? summary;

	// Per-symbol summarization tracking
	private readonly Dictionary<CodeSymbol, RunningTask> _symbolTasks = new();

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

	public void ApplySymbolFilter() {
		IEnumerable<CodeSymbol> baseSet = SymbolsForFile(visibleFiles.SafeSelected);
		IEnumerable<CodeSymbol> filtered = string.IsNullOrWhiteSpace(symFilter)
			? baseSet
			: baseSet.Where(s => s.Name.ToLowerInvariant().Contains(symFilter.ToLowerInvariant()));

		visibleSymbols.Reset(filtered, 0);
		symOffset = 0;
		summary   = null;
	}

	public IEnumerable<CodeSymbol> SymbolsForFile(string? file) => string.IsNullOrEmpty(file)
		? allSymbols
		: allSymbols.Where(s => s.FilePath == file);

	public void ApplyFileFilter() {
		IEnumerable<string> filtered = string.IsNullOrWhiteSpace(fileFilter)
			? allFiles
			: allFiles.Where(p => p.ToLowerInvariant().Contains(fileFilter.ToLowerInvariant()));

		visibleFiles.Reset(filtered, 0);
		fileOffset = 0;
		summary    = null;

		string? file = visibleFiles.SafeSelected;
		if (file is null) visibleSymbols.Clear();
		else visibleSymbols.Reset(SymbolsForFile(file), 0);
		symOffset = 0;
	}

	public async Task EnsureSource() {
		if (visibleSymbols.Count == 0) {
			sourceLines = [];
			return;
		}

		if (sourceLines is { Count: > 0 })
			return;

		CodeSymbol s   = visibleSymbols.Selected;
		string?    src = await _crawler.GetCode(s);
		sourceLines    = (src ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
		sourceSelected = 0;
		sourceOffset   = 0;
	}

	public async Task EnsureRefs() {
		if (visibleSymbols.Count == 0) {
			refs = [];
			return;
		}

		if (refs is { Count: > 0 })
			return;

		CodeSymbol       s       = visibleSymbols.Selected;
		List<CodeSymbol> rawRefs = await _crawler.GetReferencesFor(s.Name, s.StartCodeLoc);
		refs         = rawRefs.Select(r => new CodeRef(r.FilePath, r.StartCodeLoc.Line, r.Name)).ToList();
		refsSelected = 0;
		refsOffset   = 0;
	}

	// Symbol task tracking methods
	public bool IsSymbolLoading(CodeSymbol symbol) {
		return _symbolTasks.TryGetValue(symbol, out var task) && task.IsBusy;
	}

	public void StartSymbolTask(CodeSymbol symbol, RunningTask task) {
		_symbolTasks[symbol] = task;
	}

	public void CompleteSymbolTask(CodeSymbol symbol) {
		_symbolTasks.Remove(symbol);
	}

	public string? GetSymbolTaskError(CodeSymbol symbol) {
		return _symbolTasks.TryGetValue(symbol, out var task) ? task.ErrorMessage : null;
	}
}