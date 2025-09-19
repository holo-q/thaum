using Ratatui;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Thaum.App.RatatuiTUI;

public abstract class Screen {
	protected readonly ThaumTUI       tui;
	protected readonly IEditorOpener  opener;
	protected readonly string         projectPath;
	protected readonly KeybindManager keys = new KeybindManager();

	private CancellationTokenSource? _cts;

	public   bool    IsBusy            { get; private set; }
	public   string? ErrorMessage      { get; private set; }
	internal Screen? PendingTransition { get; private set; }

	protected Screen(ThaumTUI tui, IEditorOpener opener, string projectPath) {
		this.tui         = tui;
		this.opener      = opener;
		this.projectPath = projectPath;
	}


	public virtual string FooterHint(ThaumTUI.State app)                                              => string.Empty;
	public virtual string Title(ThaumTUI.State      app)                                              => string.Empty;
	public virtual Task   OnEnter(ThaumTUI.State    app)                                              => Task.CompletedTask;
	public virtual Task   OnLeave(ThaumTUI.State    app)                                              => Task.CompletedTask;
	public virtual void   OnResize(int              width, int            height, ThaumTUI.State app) { }
	public virtual void   OnTick(TimeSpan           dt,    ThaumTUI.State app) { }

	public abstract void Draw(Terminal term, Rect area, ThaumTUI.State app, string projectPath);

	protected void RequestTransition(Screen target) => PendingTransition = target;
	internal  void ClearTransition()                => PendingTransition = null;

	protected void StartTask(Func<CancellationToken, Task> work) {
		try {
			_cts?.Cancel();
			_cts = new CancellationTokenSource();
			CancellationToken ct = _cts.Token;
			IsBusy       = true;
			ErrorMessage = null;
			_ = Task.Run(async () => {
				try { await work(ct); } catch (OperationCanceledException) { } catch (Exception ex) { ErrorMessage = ex.Message; } finally { IsBusy = false; }
			}, ct);
		} catch (Exception ex) {
			IsBusy       = false;
			ErrorMessage = ex.Message;
		}
	}

	public virtual bool HandleKey(Event ev, ThaumTUI.State app) => keys.Handle(ev, app);

	// Base default key registrations (call in OnEnter once if needed)
    protected void ConfigureDefaultGlobalKeys() {
        keys.RegisterKey(KeyCode.ESC, "Esc", "quit", _ => {
            Environment.Exit(0);
            return true;
        });
        keys.RegisterChar('q', "quit", _ => {
            Environment.Exit(0);
            return true;
        });
        keys.Register("Ctrl-C", "quit", ev => ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char } && ((char)ev.Key.Char == 'c' || (char)ev.Key.Char == 'C') && ev.Key.Ctrl,
            (ev, _) => { Environment.Exit(0); return true; });
        keys.RegisterChar('1', "mode", a => {
            tui.NavigateTo(tui.scrBrowser, a);
            return true;
        });
		keys.RegisterChar('2', "mode", a => {
			tui.NavigateTo(tui.scrSource, a);
			return true;
		});
		keys.RegisterChar('3', "mode", a => {
			tui.NavigateTo(tui.scrSummary, a);
			return true;
		});
		keys.RegisterChar('4', "mode", a => {
			tui.NavigateTo(tui.scrReferences, a);
			return true;
		});
		keys.RegisterChar('5', "mode", a => {
			tui.NavigateTo(tui.scrMode, a);
			return true;
		});
	}

	public IReadOnlyList<KeyBinding> GetHelp(int max) => keys.GetHelp(max);
}

public readonly record struct KeyBinding(string Key, string Description);
