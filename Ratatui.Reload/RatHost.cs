using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ratatui.Reload.Abstractions;
using Thaum.App.RatatuiTUI;
using Thaum.Meta;

namespace Ratatui.Reload;

/// <summary>
/// HES A RAT!!!!!!!
/// - bossman jack
/// </summary>
[SuppressMessage("ReSharper", "RedundantVerbatimPrefix")]
[LoggingIntrinsics]
public partial class RatHost : IDisposable {
	private readonly IServiceProvider _services;
	private readonly string           _pluginProject;
	private readonly string           _configuration;
	private readonly TimeSpan         _debounce;
	private readonly string           _buildOutputDir;
	private readonly bool             _manualReload;   // if true, don't rebuild automatically; wait for key
	private readonly bool             _showReloadHint; // if true, draw bottom-left hint when changes pending
	private readonly ConsoleKey       _reloadKey;      // key used to trigger reload
	private readonly bool             _devUI;          // show dev host UI wrapper
	private readonly bool             _watchEnabled;   // file watching + autobuild

	private volatile bool _rebuildRequested;
	private volatile bool _changesPending; // set by watcher when changes detected (debounced)
	private volatile bool _buildFailed;
	private volatile bool _building;
	private volatile bool _swapPending;

	private readonly IHotReloadUi _ui = new DevHostUi();

	private Task?     _buildTask;
	private string    _lastBuildLog = string.Empty;
	private DateTime? _lastBuildOkUtc;

	private FileSystemWatcher? _watcher;
	private Task?              _watcherTask;

	private PluginLoadContext? _alc;
	private RatTUI             _tui;
	private HostTUI            _hostTui;
	private WeakReference?     _unloadRef;

	private readonly CancellationTokenSource _cts = new();

	public RatHost(string    pluginProject,
	               string    configuration  = "Debug",
	               TimeSpan? debounce       = null,
	               string?   buildOutputDir = null) {
#if DEBUG
		bool watchEnabled = true;
		bool devUi        = true;
#else
		bool watchEnabled = false; // gracefully deactivate in consumer builds
		bool devUi = false;        // hide dev UI
#endif
		_services = new ServiceCollection().BuildServiceProvider();

		_pluginProject  = pluginProject;
		_configuration  = configuration;
		_debounce       = debounce ?? TimeSpan.FromMilliseconds(300);
		_buildOutputDir = buildOutputDir ?? Path.Combine(Path.GetTempPath(), "thaum-reload", Guid.NewGuid().ToString("n"));
		_manualReload   = false;
		_reloadKey      = ConsoleKey.R;
		_devUI          = devUi;
		_watchEnabled   = watchEnabled;
		Directory.CreateDirectory(_buildOutputDir);

		// Initialize host TUI as guaranteed fallback
		_hostTui = new HostTUI();
		_tui = _hostTui; // Start with host TUI until plugin loads
		_hostTui.SetMessage("Starting Thaum Host...");
	}

	public IServiceProvider  Services        => _services;
	public CancellationToken AppCancellation => _cts.Token;
	public string            ProjectPath     => Path.GetFullPath(Path.GetDirectoryName(_pluginProject)!);

	// TODO this is the old code and we need to ensure it has been fully adapted for this class

	// public async Task RunAsync() {
	// 	bool noAlt = string.Equals(Environment.GetEnvironmentVariable("THAUM_TUI_NO_ALTSCREEN"), "1", StringComparison.OrdinalIgnoreCase);
	// 	bool noRaw = string.Equals(Environment.GetEnvironmentVariable("THAUM_TUI_NO_RAW"), "1", StringComparison.OrdinalIgnoreCase);
	//
	// 	using Terminal tm = new Terminal()
	// 		.Raw(!noRaw)
	// 		.AltScreen(!noAlt)
	// 		.ShowCursor(false);
	//
	// 	await PrepareAsync();
	//
	// 	// Shorter poll ensures smoother redraws for terminals that don't persist frames well
	// 	TimeSpan poll       = TimeSpan.FromMilliseconds(33);
	// 	Screen   lastScreen = _app.screen;
	//
	// 	bool quit = false;
	// 	while (!quit) {
	// 		if (_invalidated) {
	// 			Draw(tm);
	// 			_invalidated = false;
	// 		}
	//
	// 		if (!tm.NextEvent(poll, out Event ev)) {
	// 			// Tick update for animations/status and drive a periodic redraw to avoid stale frames
	// 			(_app.screen ?? scrBrowser).OnTick(poll, _app);
	// 			Invalidate();
	// 			continue;
	// 		}
	//
	// 		switch (ev.Kind) {
	// 			case EventKind.Resize:
	// 				trace("Resize to {Size}", ev.Size());
	// 				Invalidate();
	// 				Vec2i size = tm.Size();
	// 				OnResize(size.w, size.h);
	// 				continue;
	// 			case EventKind.Mouse:
	// 				// Some terminals only visibly refresh while receiving input
	// 				trace("Mouse {Mouse}", ev.Mouse);
	// 				Invalidate();
	// 				break;
	// 			case EventKind.Key:
	// 				trace("Key {Key}", ev.Key);
	// 				// Global quit keys: Esc, q/Q, Ctrl-C
	// 				if (ev.Key.IsEscape() || ev.Key.IsChar('q', ignoreCase: true) || ev.Key.IsCtrlChar('c', ignoreCase: true)) {
	// 					quit = true;
	// 					continue;
	// 				}
	// 				bool handled = HandleEvent(ev);
	// 				trace("Key dispatch handled={Handled} by {Screen}", handled, (_app.screen ?? scrBrowser).GetType().Name);
	// 				if (handled) { Invalidate(); }
	// 				break;
	// 		}
	//
	// 		// Screen lifecycle
	// 		if ((_app.screen ?? scrBrowser) != lastScreen) {
	// 			Screen old  = lastScreen;
	// 			Screen @new = _app.screen ?? scrBrowser;
	// 			await old.OnLeave(_app);
	// 			await @new.OnEnter(_app);
	// 			lastScreen = @new;
	// 			Invalidate();
	// 		}
	// 	}
	// }

	public async Task<int> RunAsync() {
		// TODO we could set this on the host as a property
		using CancellationTokenSource cts = new CancellationTokenSource();
		Console.CancelKeyPress += (s, e) => {
			e.Cancel = true;
			cts.Cancel();
		};
		CancellationToken cancel = cts.Token;

		// Initial build and load (background) so UI can show spinner
		_building = true;
		_hostTui.SetLoading("Initial build...");
		_buildTask = Task.Run(async () => {
			bool ok = await BuildPluginAsync(cancel);
			if (ok) {
				_buildFailed = false;
				_swapPending = true;
			} else {
				_buildFailed = true;
			}
			_building = false;
		}, cancel);

		if (_watchEnabled)
			StartWatcher();

		using Terminal term = new Terminal().Raw().AltScreen().ShowCursor(false);
		(int w, int h) = (Console.WindowWidth, Console.WindowHeight);
		_tui.OnResize(w, h);
		_ui.OnResize(w, h);

		Stopwatch sw = new Stopwatch();
		sw.Start();
		TimeSpan last = sw.Elapsed;

		while (!cancel.IsCancellationRequested && !AppCancellation.IsCancellationRequested) {
			// Handle host-level keys first (e.g., manual reload)
			if (term.NextEvent(TimeSpan.FromMilliseconds(10), out Event e)) {
				if (HandleHostKeys(e)) {
					/* consumed */
				} else if (!_ui.HandleEvent(e)) {
					DispatchToApp(e);
				}
			}

			// Handle rebuild requests (kick off background build)
			if (_watchEnabled && _rebuildRequested && !_building) {
				_rebuildRequested = false;
				_changesPending   = false;
				_building         = true;
				_hostTui.SetLoading("Building plugin...");
				_buildTask = Task.Run(async () => {
					bool ok = await BuildPluginAsync(cancel);
					if (ok) {
						_buildFailed = false;
						_swapPending = true;
					} else {
						_buildFailed = true;
					}
					_building = false;
				}, cancel);
			}

			// Perform swap after successful build
			if (_swapPending) {
				_swapPending = false;
				ReloadSwap();
				_tui.OnResize(w, h);
				_ui.OnResize(w, h);
			}

			// Pump a frame
			TimeSpan now = sw.Elapsed;
			TimeSpan dt  = now - last;
			last = now;

			// If no event was pending above, still poll a bit to keep UI responsive,
			// and process it if present.
			if (term.NextEvent(TimeSpan.FromMilliseconds(40), out Event ev)) {
				if (!HandleHostKeys(ev) && !_ui.HandleEvent(ev)) {
					DispatchToApp(ev);
				}
			}

			using (term.BeginFrame()) {
				_tui.OnUpdate(dt);
				_tui.OnDraw(term);
			}

			// TODO this has to be reviewed following the recent refactors
			// Draw everything in a single frame to avoid flicker from multiple presents
			// var state = new HotReloadState(
			// 	Building: _building,
			// 	BuildFailed: _buildFailed,
			// 	ChangesPending: _changesPending,
			// 	LastBuildLog: _lastBuildLog ?? string.Empty,
			// 	LastSuccessUtc: _lastBuildOkUtc,
			// 	ReloadKey: _reloadKey);
			//
			// _ui.Draw(term, state, () => _currentApp?.Draw(term));
		}

		return 0;
	}

	private void DrawBuildOverlay(Terminal term, string log) {
		(int w, int h)  size   = term.Size();
		int             width  = Math.Max(20, Math.Min(120, size.w - 4));
		int             height = Math.Max(6, Math.Min(30, size.h - 4));
		Rect            rect   = new Rect(2, 1, width, height);
		using Paragraph para   = new Paragraph("").Title("Build Error", border: true);
		string          tail   = string.Join('\n', log.Split('\n').TakeLast(height - 4));
		para.AppendSpan(tail, new Style(fg: Color.LightRed));
		term.Draw(para, rect);
	}

	private void DrawBuildingOverlay(Terminal term) {
		(int w, int h) = term.Size();

		// Lightweight scrim: just draw a border instead of filling the area
		// TODO this looks ugly - I feel like this is a pattern we can stuff away into Rat
		int scrimW = Math.Max(1, w - 2);
		int scrimH = Math.Max(1, h - 2);
		using (Paragraph border = new Paragraph("")) {
			border.Block(new Block().Borders(Borders.All));
			term.Draw(border, new Rect(1, 1, scrimW, scrimH));
		}

		// Center modal
		int mw = Math.Max(24, Math.Min(60, w - 4));
		int mh = 5;
		int mx = (w - mw) / 2;
		int my = (h - mh) / 2;

		using Paragraph overlay = new Paragraph($" {Spinner()} Rebuilding plugin…")
			.Title("Reloading", border: true)
			.Align(Alignment.Center);

		term.Draw(overlay, new Rect(mx, my, mw, mh));
	}

	private void DrawSpinner(Terminal term) {
		(int W, int H) = term.Size();

		string msg = $" {Spinner()} Reload";
		int    w   = Math.Min(16, Math.Max(10, msg.Length + 2));
		int    x   = Math.Max(0, W - w - 1);
		int    y   = Math.Max(0, H - 2);

		using Paragraph p = new Paragraph(msg);
		term.Draw(p, new Rect(x, y, w, 1));
	}

	private static string Spinner() {
		int t = (int)((DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond / 120) % 4);
		return "-\\|/"[t].ToString();
	}

	private bool HandleHostKeys(Event ev) {
		// Map ConsoleKey from Ratatui key
		if (ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char }) {
			char ch = (char)ev.Key.Char;
			if (char.ToUpperInvariant(ch) == char.ToUpperInvariant((char)_reloadKey) && _manualReload && _changesPending && !_building) {
				_rebuildRequested = true;
				return true;
			}
		}
		return false;
	}

	private void DispatchToApp(Event ev) {
		(int w, int h) size = (Console.WindowWidth, Console.WindowHeight);
		if (!_tui.OnEvent(ev)) {
			if (ev.Kind == EventKind.Resize) {
				_tui.OnResize(size.w, size.h);
			}
		}
	}

	private async Task<bool> BuildPluginAsync(CancellationToken cancel) {
		try {
			string args = $"build \"{_pluginProject}\" -c {_configuration} -o \"{_buildOutputDir}\"";
			ProcessStartInfo psi = new ProcessStartInfo("dotnet", args) {
				UseShellExecute        = false,
				RedirectStandardOutput = true,
				RedirectStandardError  = true,
				CreateNoWindow         = true,
			};
			Process       proc = Process.Start(psi)!;
			StringBuilder sb   = new StringBuilder();
			Task<string>  @out = proc.StandardOutput.ReadToEndAsync(cancel);
			Task<string>  @err = proc.StandardError.ReadToEndAsync(cancel);

			await Task.WhenAll(@out, @err);
			await proc.WaitForExitAsync(cancel);
			sb.AppendLine(@out.Result);
			sb.AppendLine(@err.Result);

			_lastBuildLog = sb.ToString();
			info("Build exited {Code}", proc.ExitCode);
			return proc.ExitCode == 0;
		} catch (Exception ex) {
			_lastBuildLog = ex.ToString();
			err(ex, "Build failed");
			return false;
		}
	}

	private void StartWatcher() {
		try {
			string dir = Path.GetDirectoryName(_pluginProject)!;
			_watcher = new FileSystemWatcher(dir) {
				IncludeSubdirectories = true,
				EnableRaisingEvents   = true,
				NotifyFilter          = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
			};
			ConcurrentQueue<DateTime> queue = new ConcurrentQueue<DateTime>();
			_watcher.Changed += (_, __) => queue.Enqueue(DateTime.UtcNow);
			_watcher.Created += (_, __) => queue.Enqueue(DateTime.UtcNow);
			_watcher.Deleted += (_, __) => queue.Enqueue(DateTime.UtcNow);
			_watcher.Renamed += (_, __) => queue.Enqueue(DateTime.UtcNow);

			_watcherTask = Task.Run(async () => {
				DateTime last = DateTime.MinValue;
				while (!_cts.IsCancellationRequested) {
					if (queue.TryDequeue(out DateTime t)) {
						last = t;
					}
					if (last != DateTime.MinValue && (DateTime.UtcNow - last) > _debounce) {
						if (_manualReload) {
							_changesPending = true;
						} else {
							_rebuildRequested = true;
						}
						last = DateTime.MinValue;
					}
					await Task.Delay(50, AppCancellation);
				}
			}, AppCancellation);
		} catch (Exception ex) {
			err(ex, "Watcher error");
		}
	}

	private void DrawReloadHint(Terminal term) {
		(int W, int H) = term.Size();
		// Guard against invalid sizes that may overflow on native side
		int safeW = Math.Max(1, W);
		int safeH = Math.Max(1, H);

		string msg = $" ⟳ Changes detected — press {(char)_reloadKey} to reload ";

		int desired = Math.Max(28, msg.Length);
		int w       = Math.Max(1, Math.Min(Math.Max(1, safeW - 2), desired));
		int x       = 1;
		int y       = Math.Max(0, safeH - 2);

		using Paragraph p = new Paragraph("");
		p.AppendSpan(msg, new Style(fg: Color.LightYellow));
		term.Draw(p, new Rect(x, y, w, 1));
	}

	private bool TryLoadPlugin(out string error) {
		try {
			string            mainDll = Directory.GetFiles(_buildOutputDir, "*.dll").First(p => Path.GetFileName(p).Equals(Path.GetFileNameWithoutExtension(_pluginProject) + ".dll", StringComparison.OrdinalIgnoreCase));
			PluginLoadContext alc     = new PluginLoadContext(mainDll);
			Assembly          asm     = alc.LoadFromAssemblyPath(mainDll);
			Type?             entry   = asm.GetTypes().FirstOrDefault(t => typeof(IReloadableApp).IsAssignableFrom(t) && !t.IsAbstract);
			if (entry == null) {
				error = "No IReloadableApp implementation found";
				return false;
			}
			RatTUI tui = (RatTUI)Activator.CreateInstance(entry)!;
			tui.OnInit();

			_tui  = tui;
			_alc  = alc;
			error = string.Empty;
			return true;
		} catch (Exception ex) {
			error = ex.ToString();
			return false;
		}
	}

	private void ReloadSwap() {
		object? state = _tui.CaptureState();

		RatTUI            oldApp = _tui;
		PluginLoadContext? oldAlc = _alc;

		// Always fallback to HostTUI first
		_tui = _hostTui;
		_alc = null;

		// Dispose old app if it's not the host TUI
		if (oldApp != _hostTui) {
			try { oldApp.Dispose(); } catch { /* ignored */ }
		}

		if (!TryLoadPlugin(out string err)) {
			_buildFailed  = true;
			_lastBuildLog = err + "\n" + _lastBuildLog;
			_hostTui.SetError($"Plugin load failed: {err}");
			// _tui remains as _hostTui
		} else {
			try { _tui.RestoreState(state); } catch { /* ignored */ }
			_lastBuildOkUtc = DateTime.UtcNow;
			_buildFailed = false;
		}

		if (oldAlc != null) {
			_unloadRef = new WeakReference(oldAlc);
			oldAlc.Unload();
			for (int i = 0; i < 5 && _unloadRef.IsAlive; i++) {
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				Thread.Sleep(50);
			}
		}
	}

	public void Dispose() {
		try { _cts.Cancel(); } catch { }
		try { _watcher?.Dispose(); } catch { }
		try { _tui?.Dispose(); } catch { }
		try { _alc?.Unload(); } catch { }
	}
}