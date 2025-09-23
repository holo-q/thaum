using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Ratatui.Reload.Abstractions;
using Thaum.App.RatatuiTUI;
using Thaum.Meta;
using Spectre.Console;

namespace Ratatui.Reload;

/// <summary>
/// Host runner for a RatTUI.
///
/// HES A RAT!!!!!!!
/// - bossman jack
/// </summary>
[SuppressMessage("ReSharper", "RedundantVerbatimPrefix")]
[LoggingIntrinsics]
public partial class RatHost : IDisposable {
	private enum TuiLaunchMode { Embedded, ExternalTerminal }

	private enum RebuildPolicy { Never, OnChanges, Always }

	private readonly IServiceProvider _services;
	private readonly string           _pluginProject;
	private readonly string           _configuration;
	private readonly TimeSpan         _debounce;
	private readonly string           _buildOutputDir;
	private readonly bool             _manualReload;    // if true, don't rebuild automatically; wait for key
	private readonly bool             _showReloadHint;  // if true, draw bottom-left hint when changes pending
	private readonly ConsoleKey       _reloadKey;       // key used to trigger reload
	private readonly bool             _allowUI;         // show dev host UI wrapper
	private readonly bool             _allowLiveReload; // file watching + autobuild
	private readonly RebuildPolicy    _rebuildPolicy;

	private const int ExternalPollDelayMs = 50;

	private volatile bool _buildRequested;
	private volatile bool _isPendingChanges; // set by watcher when changes detected (debounced)
	private volatile bool _hasFailedBuild;
	private volatile bool _isBuilding;
	private volatile bool _isPendingSwap;

	private readonly IHotReloadUi _ui = new DevHostUi();

	private Task?     _buildTask;
	private string    _lastBuildLog = string.Empty;
	private DateTime? _lastBuildOkUtc;

	private FileSystemWatcher? _watcher;
	private Task?              _watcherTask;

	private PluginLoadContext? _alc;
	private IRatTUI            _tui;
	private HostTUI            _hostTUI;
	private WeakReference?     _unloadRef;

	// External terminal support
	private string?  _term;
	private Process? _termproc;

	private readonly CancellationTokenSource _cts = new();
	private readonly bool                    _skipInitialBuild;
	private          bool                    _initialSeedDone;

#region Core

	public bool IsTerm      => _term != null;
	public bool IsTermAlive => _termproc is { HasExited: false }; // TODO dont know if this is right

	public IServiceProvider  Services        => _services;
	public CancellationToken AppCancellation => _cts.Token;
	public string            ProjectPath     => Path.GetFullPath(Path.GetDirectoryName(_pluginProject)!);

	public RatHost(string    pluginProject,
	               string    configuration  = "Debug",
	               TimeSpan? debounce       = null,
	               string?   buildOutputDir = null) {
#if DEBUG
		const bool ALLOW_AUTO_RELOAD = true;
		const bool ALLOW_DEV_UI      = true;
#else
		const bool ENABLE_WATCH = false; // gracefully deactivate in consumer builds
		const bool DEV_UI = false;        // hide dev UI
#endif


		_hostTUI = new HostTUI(); // guaranteed fallback
		_tui     = _hostTUI;      // Start with host TUI until plugin loads
		info2(_skipInitialBuild
			? "Loading existing build..."
			: "Starting Thaum Host...");

		_pluginProject    = pluginProject;
		_configuration    = configuration;
		_debounce         = debounce ?? TimeSpan.FromMilliseconds(300);
		_buildOutputDir   = buildOutputDir ?? Path.Combine(Path.GetTempPath(), "thaum-reload", Guid.NewGuid().ToString("n"));
		_manualReload     = false;
		_reloadKey        = ConsoleKey.R;
		_allowUI          = ALLOW_DEV_UI;
		_allowLiveReload  = ALLOW_AUTO_RELOAD;
		_skipInitialBuild = true;
		_term             = TermUtil.DetectPreferredTerminal();
		_services         = new ServiceCollection().BuildServiceProvider();

		_rebuildPolicy = ParseRebuildPolicy(Environment.GetEnvironmentVariable("THAUM_RELOAD_POLICY"));
		if (Environment.GetEnvironmentVariable("THAUM_FORCE_INITIAL_BUILD") == "1")
			_skipInitialBuild = false;
		if (Environment.GetEnvironmentVariable("THAUM_SKIP_INITIAL_BUILD") == "1")
			_skipInitialBuild = true;
		Directory.CreateDirectory(_buildOutputDir);
	}

	/// <summary>
	/// State management methods for cleaner state transitions
	/// </summary>
	private void SetBuilding(string message) {
		_buildRequested   = false;
		_isPendingChanges = false;
		_isBuilding       = true;
		_hostTUI.SetLoading(message);
	}

	private void SetStandby() {
		_isBuilding = false;
	}

	private void SetSwapping() {
		_isPendingSwap = true;
	}

	private void SetBuildSuccess() {
		_hasFailedBuild = false;
		_isPendingSwap  = true;
		_isBuilding     = false;
	}

	private void SetBuildFailed() {
		_hasFailedBuild = true;
		_isBuilding     = false;
	}

	private void info2(string msg) {
		// TODO this should be a general message popup/component feature that is draw directly on top of the user-space tui as a system-space floating window, cuz rn it only displays these on the fallback TUI if the user TUI is crashed out / not compiling etc.
		if (IsTerm)
			_hostTUI.SetMessage(msg);
		info(msg);
	}

	private void err2(string msg) {
		if (IsTerm)
			_hostTUI.SetMessage(msg); // TODO this should be an error popup with an icon or something
		err(msg);
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

			void EnqueueIfRelevant(string path) {
				if (ShouldIgnorePath(path)) return;
				queue.Enqueue(DateTime.UtcNow);
			}

			_watcher.Changed += (_, e) => EnqueueIfRelevant(e.FullPath);
			_watcher.Created += (_, e) => EnqueueIfRelevant(e.FullPath);
			_watcher.Deleted += (_, e) => EnqueueIfRelevant(e.FullPath);
			_watcher.Renamed += (_, e) => EnqueueIfRelevant(e.FullPath);

			// TODO refactor this to use a Set* state function instead of accessing fields, which cleans up the API
			_watcherTask = Task.Run(async () => {
				DateTime last = DateTime.MinValue;
				while (!_cts.IsCancellationRequested) {
					if (queue.TryDequeue(out DateTime t)) {
						last = t;
					}
					if (last != DateTime.MinValue && (DateTime.UtcNow - last) > _debounce) {
						if (_manualReload) {
							_isPendingChanges = true;
						} else {
							_buildRequested = true;
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

#endregion

	private bool TrySeed() {
		if (_initialSeedDone)
			return Directory.Exists(_buildOutputDir) && Directory.EnumerateFiles(_buildOutputDir, "*.dll").Any();

		try {
			string projectDir = Path.GetDirectoryName(_pluginProject)!;
			string configDir  = Path.Combine(projectDir, "bin", _configuration);
			if (!Directory.Exists(configDir)) return false;

			string? sourceDir = Directory.GetDirectories(configDir)
				.OrderByDescending(Directory.GetLastWriteTimeUtc)
				.FirstOrDefault();
			if (sourceDir is null) return false;

			if (Directory.Exists(_buildOutputDir)) {
				Directory.Delete(_buildOutputDir, recursive: true);
			}
			Directory.CreateDirectory(_buildOutputDir);
			CopyDirectory(sourceDir, _buildOutputDir);
			_initialSeedDone = true;
			string expectedDll = Path.Combine(_buildOutputDir, Path.GetFileNameWithoutExtension(_pluginProject) + ".dll");
			return File.Exists(expectedDll);
		} catch (Exception ex) {
			err(ex, "Failed to seed existing build output");
			return false;
		}
	}

	private async Task<bool> PrepareInitialBuildAsync(CancellationToken token) {
		bool seeded = TrySeed();
		if (seeded)
			_isPendingSwap = true; // TODO this was not in the external branch, not sure why if this is required

		info2(seeded
			? "Loaded existing build artifacts."
			: "Preparing initial build...");

		bool needBuild = !_skipInitialBuild || !seeded;
		if (!needBuild) {
			// TODO
			// SetBuilding(seeded ? "Refreshing build..." : "Initial build...");
			// _buildTask = Task.Run(async () => {
			// 	bool ok = await BuildPluginAsync(cancel);
			// 	if (ok)
			// 		SetBuildSuccess();
			// 	else
			// 		SetBuildFailed();
			// }, cancel);
			_isPendingSwap = true;
			return true;
		}
		// TODO
		// else SetStandby();

		info2(seeded ? "Refreshing plugin build..." : "Building plugin...");

		_isBuilding = true;
		bool ok = await BuildPluginAsync(token);
		_isBuilding = false;
		if (!ok) {
			err2($"Build failed: {_lastBuildLog}");
			return false;
		}

		_isPendingSwap = true;
		info2("Build ready.");

		return true;
	}

	private static RebuildPolicy ParseRebuildPolicy(string? raw)
		=> raw?.Trim().ToLowerInvariant() switch {
			"never"  => RebuildPolicy.Never,
			"always" => RebuildPolicy.Always,
			_        => RebuildPolicy.OnChanges
		};

	public async Task<int> RunAsync() {
		using CancellationTokenSource cts = new CancellationTokenSource();
		Console.CancelKeyPress += (s, e) => {
			e.Cancel = true;
			cts.Cancel();
		};

		CancellationToken cancel = cts.Token;

		if (!await PrepareInitialBuildAsync(_cts.Token)) {
			return 1;
		}
		if (_allowLiveReload) {
			StartWatcher();
			info("File watching started - TUI will rebuild automatically on changes.");
		}

		if (IsTerm)
			// We launch the TUI in an external terminal so that we can keep this terminal for stdout logs, easier debugging
			return await RunExternalAsync();

		using Terminal term = new Terminal().Raw().AltScreen().ShowCursor(false);

		(int w, int h) = term.Size();
		_tui.OnResize(w, h);
		_ui.OnResize(w, h);

		if (_isPendingSwap) {
			_isPendingSwap = false;
			ReloadSwap();
			_tui.OnResize(w, h);
		}

		Stopwatch sw = new Stopwatch();
		sw.Start();
		TimeSpan last = sw.Elapsed;

		while (!cancel.IsCancellationRequested && !AppCancellation.IsCancellationRequested) {
			// Handle host-level keys first (e.g., manual reload)
			if (term.NextEvent(TimeSpan.FromMilliseconds(10), out Event e)) {
				if (HandleHostKeys(e)) {
					/* consumed */
				} else if (!_ui.HandleEvent(e)) {
					DispatchToApp(e, term);
				}
			}

			// Handle rebuild requests (kick off background build)
			if (_allowLiveReload && _buildRequested && !_isBuilding) {
				SetBuilding("Building plugin...");
				_buildTask = Task.Run(async () => {
					bool ok = await BuildPluginAsync(cancel);
					if (ok) {
						SetBuildSuccess();
					} else {
						SetBuildFailed();
					}
				}, cancel);
			}

			// Perform swap after successful build
			if (_isPendingSwap) {
				_isPendingSwap = false;
				ReloadSwap();
				_tui?.OnResize(w, h);
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
					DispatchToApp(ev, term);
				}
			}

			_tui?.Tick(dt);
			_tui?.Draw(term);
		}

		return 0;
	}

	/// <summary>
	/// Runs TUI in external terminal with log monitoring in current terminal.
	/// </summary>
	private async Task<int> RunExternalAsync() {
		if (_term is null) return 1;

		// External TUI monitoring mode - logs and build output in current terminal
		PrintExternalModeHeader();
		info($"TUI launching in {_term}. This terminal will show logs/build output.");

		string               repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_pluginProject)!, ".."));
		using TerminalBridge bridge   = new TerminalBridge("Thaum — TUI", 120, 36, repoRoot, _term);
		if (!bridge.Start()) {
			err("Failed to start external terminal bridge");
			return 1;
		}

		using Terminal term = new Terminal();
		term.Viewport = new Rect(0, 0, 120, 36);
		term.FrameSink = span => {
			DrawCommand[] arr  = span.ToArray();
			string        ansi = Testing.Headless.RenderFrame(120, 36, arr);
			bridge.WriteAnsi(ansi);
		};

		// First plugin load/swap
		if (_isPendingSwap) {
			_isPendingSwap = false;
			ReloadSwap();
			_tui?.OnResize(120, 36);
		}

		Stopwatch sw   = Stopwatch.StartNew();
		TimeSpan  last = sw.Elapsed;
		while (!_cts.IsCancellationRequested) {
			// Handle rebuild requests
			if (_allowLiveReload && _buildRequested && !_isBuilding) {
				SetBuilding("Rebuilding for external TUI...");
				bool ok = await BuildPluginAsync(_cts.Token);
				if (ok) SetBuildSuccess();
				else SetBuildFailed();
			}

			// Swap after successful build
			if (_isPendingSwap) {
				_isPendingSwap = false;
				ReloadSwap();
				_tui?.OnResize(120, 36);
			}

			// Input from bridge
			while (bridge.TryDequeue(out Event ev)) {
				if (!HandleHostKeys(ev) && !_ui.HandleEvent(ev))
					DispatchToApp(ev, term);
			}

			TimeSpan now = sw.Elapsed;
			TimeSpan dt  = now - last;
			last = now;
			_tui?.Tick(dt);
			_tui?.Draw(term);

			await Task.Delay(16, _cts.Token);
		}

		return 0;
	}


	/// <summary>
	/// Attempts to launch the TUI in an external terminal.
	/// Returns true if successful, false if should fallback to current terminal.
	/// </summary>
	private bool LaunchTerm() {
		string GetExecPath() {
			string repoRoot = GetRepositoryRoot();
			string script   = Path.Combine(repoRoot, "run.sh");
			return $"\"{script}\"";
		}

		string GetRepositoryRoot()
			=> Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_pluginProject)!, ".."));

		try {
			// Build command to launch TUI executable in external terminal
			// TODO this must not be a different process
			string execPath = GetExecPath();
			string args     = GetTerminalArgs(execPath);

			ProcessStartInfo psi = new(_term!, args) {
				UseShellExecute  = false,
				CreateNoWindow   = false,
				WorkingDirectory = GetRepositoryRoot(),
				Environment = {
					["THAUM_NO_EXTERNAL_TERMINAL"] = "1",
					["THAUM_SKIP_INITIAL_BUILD"]   = "1"
				}
			};

			_termproc = Process.Start(psi);
			if (_termproc == null) {
				err("Failed to start external terminal process");
				return false;
			}

			info("External TUI process started with PID {Pid}", _termproc.Id);
			return true;
		} catch (Exception ex) {
			err(ex, "Failed to launch external TUI");
			return false;
		}
	}

	/// <summary>
	/// Builds terminal-specific arguments to launch the TUI.
	/// </summary>
	private string GetTerminalArgs(string command) {
		// Add project path using the correct TUI command syntax
		string fullCommand = $"{command} tui --path \"{ProjectPath}\"";

		return _term switch {
			"kitty"          => $"-e sh -c \"{fullCommand}\"",
			"alacritty"      => $"-e sh -c \"{fullCommand}\"",
			"wezterm"        => $"start --cwd \"{ProjectPath}\" -- sh -c \"{fullCommand}\"",
			"gnome-terminal" => $"-- sh -c \"{fullCommand}\"",
			"konsole"        => $"-e sh -c \"{fullCommand}\"",
			"xterm"          => $"-e sh -c \"{fullCommand}\"",
			_                => $"-e sh -c \"{fullCommand}\""
		};
	}


	private static void CopyDirectory(string source, string destination) {
		foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
			string targetDir = dir.Replace(source, destination, StringComparison.OrdinalIgnoreCase);
			Directory.CreateDirectory(targetDir);
		}

		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
			string targetFile = file.Replace(source, destination, StringComparison.OrdinalIgnoreCase);
			Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
			File.Copy(file, targetFile, overwrite: true);
		}
	}

#region UI

	private void DrawBuildOverlay(Terminal term, string log) {
		(int w, int h)  size   = term.Size();
		int             width  = Math.Max(20, Math.Min(120, size.w - 4));
		int             height = Math.Max(6, Math.Min(30, size.h - 4));
		Rect            rect   = new Rect(2, 1, width, height);
		using Paragraph para   = new Paragraph("").Title("Build Error", border: true);
		string          tail   = string.Join('\n', log.Split('\n').TakeLast(height - 4));
		para.AppendSpan(tail, new Style(fg: Colors.LIGHTRED));
		term.Draw(para, rect);
	}

	private void DrawBuildingOverlay(Terminal term) {
		(int w, int h) = term.Size();

		// Lightweight scrim: just draw a border instead of filling the area
		// Simple border scrim - could be refactored into Rat utility method
		int scrimW = Math.Max(1, w - 2);
		int scrimH = Math.Max(1, h - 2);
		using (Paragraph border = new Paragraph("")) {
			border.Bordered();
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

#endregion

	private bool HandleHostKeys(Event ev) {
		// Map ConsoleKey from Ratatui key
		if (ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char }) {
			char ch = (char)ev.Key.Char;
			if (char.ToUpperInvariant(ch) == char.ToUpperInvariant((char)_reloadKey) && _manualReload && _isPendingChanges && !_isBuilding) {
				_buildRequested = true;
				return true;
			}
		}
		return false;
	}

	private void DispatchToApp(Event ev, Terminal term) {
		(int w, int h) = term.Size();
		if (!(_tui?.OnEvent(ev) ?? false)) {
			if (ev.Kind == EventKind.Resize) {
				_tui?.OnResize(w, h);
			}
		}
	}

	/// <summary>
	/// Spectre.Console formatting for external terminal mode
	/// </summary>
	private void PrintExternalModeHeader() {
		AnsiConsole.WriteLine();
		var rule = new Rule("[green]Thaum External TUI Mode[/]")
			.RuleStyle("grey")
			.LeftJustified();
		AnsiConsole.Write(rule);

		AnsiConsole.MarkupLine("[dim]External TUI running in {0}[/]", _term);
		AnsiConsole.MarkupLine("[dim]Logs and build output will appear below[/]");
		AnsiConsole.WriteLine();

		var logRule = new Rule("[yellow]Build & Log Output[/]")
			.RuleStyle("yellow")
			.LeftJustified();
		AnsiConsole.Write(logRule);
	}

	private void PrintBuildSection(string status, bool success) {
		AnsiConsole.WriteLine();
		var color = success ? "green" : "red";
		var rule = new Rule($"[{color}]{status}[/]")
			.RuleStyle(color)
			.LeftJustified();
		AnsiConsole.Write(rule);
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


	private bool ShouldIgnorePath(string path) {
		if (string.IsNullOrEmpty(path)) return false;
		try {
			string full     = Path.GetFullPath(path);
			string buildDir = Path.GetFullPath(_buildOutputDir);
			if (full.StartsWith(buildDir, StringComparison.OrdinalIgnoreCase)) return true;
			if (full.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
			if (full.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
		} catch {
			// ignore
		}
		return false;
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
		p.AppendSpan(msg, new Style(fg: Colors.LYELLOW));
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
			IRatTUI tui = (IRatTUI)Activator.CreateInstance(entry)!;
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

		IRatTUI            oldApp = _tui;
		PluginLoadContext? oldAlc = _alc;

		// Always fallback to HostTUI first
		_tui = _hostTUI;
		_alc = null;

		// Dispose old app if it's not the host TUI
		if (oldApp != _hostTUI) {
			try { oldApp.Dispose(); } catch { /* ignored */
			}
		}

		if (!TryLoadPlugin(out string err)) {
			_hasFailedBuild = true;
			_lastBuildLog   = err + "\n" + _lastBuildLog;
			_hostTUI.SetError($"Plugin load failed: {err}");
			// _tui remains as _hostTui
		} else {
			try {
				_tui.RestoreState(state);
			} catch { /* ignored */
			}
			_lastBuildOkUtc = DateTime.UtcNow;
			_hasFailedBuild = false;
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
		try {
			if (_termproc is { HasExited: false }) {
				_termproc.Kill();
				_termproc.Dispose();
			}
		} catch { }
	}
}


// Legacy commented code removed during refactoring

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