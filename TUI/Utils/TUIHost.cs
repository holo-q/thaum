using Microsoft.Extensions.Logging;
using static Thaum.Core.Utils.TraceLogger;

namespace Thaum.CLI.Interactive;

public class TUIHost<TView> where TView : TUIView, new() {
	private readonly ILogger _logger;

	public TUIHost(ILogger logger) {
		_logger = logger;
	}

	public async Task RunAsync(TUIConfig config) {
		tracein(parameters: new { config });

		using var scope = trace_scope("InteractiveTuiHost.RunAsync");
		trace("Initializing Terminal.Gui application");
		Terminal.Gui.Application.Init();

		// Use a semaphore to prevent multiple concurrent refresh executions
		var refreshSemaphore = new SemaphoreSlim(1, 1);

		// Thread-safe shared state for UI updates
		var    textLock      = new object();
		string currentText   = "Loading...";
		string currentStatus = "Starting...";

		try {
			trace("Creating Terminal.Gui main view without borders");
			// Use a simple View instead of Window to avoid borders
			var mainView = new Terminal.Gui.View() {
				X        = 0, Y = 0, Width = Terminal.Gui.Dim.Fill(), Height = Terminal.Gui.Dim.Fill(),
				CanFocus = true
			};

			trace("Creating status bar with keyboard shortcuts");
			// Status bar with shortcuts (display only - actual bindings are global)
			var statusBar = new Terminal.Gui.StatusBar([
				new(Terminal.Gui.Key.Space, "~R/SPACE~ Retry", null),
				new(Terminal.Gui.Key.q, "~Q/ESC~ Quit", null),
				new(Terminal.Gui.Key.Null, "~AUTO~ Starting...", null)
			]);

			trace("Creating text view directly with proper colors");
			// Simplified: Just a TextView directly in the main area, no ScrollView
			var textView = new Terminal.Gui.TextView() {
				X        = 0, Y = 0,
				Width    = Terminal.Gui.Dim.Fill(),
				Height   = Terminal.Gui.Dim.Fill() - 1, // Leave room for status bar
				ReadOnly = true,
				Text     = "Loading...",
				WordWrap = true
			};

			// Set normal colors - white text on black background
			trace("Setting proper color scheme for readability");
			textView.ColorScheme = new Terminal.Gui.ColorScheme() {
				Normal    = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Focus     = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotNormal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotFocus  = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Disabled  = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.Gray, Terminal.Gui.Color.Black)
			};

			// Set main view colors too
			mainView.ColorScheme = new Terminal.Gui.ColorScheme() {
				Normal    = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Focus     = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotNormal = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				HotFocus  = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.White, Terminal.Gui.Color.Black),
				Disabled  = Terminal.Gui.Attribute.Make(Terminal.Gui.Color.Gray, Terminal.Gui.Color.Black)
			};

			// Initialize the TUI view
			trace("Initializing TUI view");
			var tuiView = new TView();

			// Pass parameters if the view supports it
			if (tuiView is TryTUI tryView && config.Parameters != null) {
				tryView.SetParameters(config.Parameters);
			}

			tuiView.Initialize(mainView);

			// Helper function to trigger retry
			Func<string, Task> triggerRetry = async (source) => {
				traceop($"Triggering retry from {source}");
				if (refreshSemaphore.CurrentCount > 0) {
					lock (textLock) {
						currentStatus = $"Auto-retry ({source})...";
					}
					_ = Task.Run(async () => {
						await refreshSemaphore.WaitAsync();
						try {
							await tuiView.RefreshAsync(
								(text) => {
									traceop($"UI CALLBACK: Updating currentText to length {text.Length}");
									lock (textLock) {
										currentText = text;
									}
								},
								(status) => {
									traceop($"STATUS CALLBACK: Updating status to {status}");
									lock (textLock) {
										currentStatus = status;
									}
								});
						} finally {
							refreshSemaphore.Release();
						}
					});
				} else {
					traceop($"Refresh already in progress - ignoring {source} trigger");
				}
			};

			// Add global key bindings that work regardless of focus
			trace("Setting up global key bindings");
			Terminal.Gui.Application.RootKeyEvent += (keyEvent) => {
				if (keyEvent.Key is Terminal.Gui.Key.q or Terminal.Gui.Key.Q) {
					traceop("User pressed Q - requesting application stop");
					Terminal.Gui.Application.RequestStop();
					return true;
				} else if (keyEvent.Key is Terminal.Gui.Key.Space or Terminal.Gui.Key.r or Terminal.Gui.Key.R) {
					string keyPressed = keyEvent.Key == Terminal.Gui.Key.Space ? "SPACE" :
						keyEvent.Key == Terminal.Gui.Key.r                     ? "r" : "R";
					traceop($"User pressed {keyPressed} - attempting manual retry");
					_ = triggerRetry($"key:{keyPressed}");
					return true;
				} else if (keyEvent.Key == Terminal.Gui.Key.Esc) {
					traceop("User pressed ESC - requesting application stop");
					Terminal.Gui.Application.RequestStop();
					return true;
				}
				return false;
			};

			trace("Adding components to Terminal.Gui layout - simplified structure");
			// Simplified structure: TextView directly in main view
			mainView.Add(textView);
			Terminal.Gui.Application.Top.Add(mainView);
			Terminal.Gui.Application.Top.Add(statusBar);

			// Setup file watcher for auto-retry if configured
			FileSystemWatcher? fileWatcher = null;
			if (!string.IsNullOrEmpty(config.WatchFilePath)) {
				trace($"Setting up FileSystemWatcher for: {config.WatchFilePath}");

				if (File.Exists(config.WatchFilePath)) {
					fileWatcher = new FileSystemWatcher(Path.GetDirectoryName(config.WatchFilePath)!, Path.GetFileName(config.WatchFilePath)) {
						NotifyFilter        = NotifyFilters.LastWrite | NotifyFilters.Size,
						EnableRaisingEvents = true
					};

					// Debounce file changes to avoid multiple rapid triggers
					DateTime lastChangeTime = DateTime.MinValue;
					fileWatcher.Changed += (sender, e) => {
						var now = DateTime.Now;
						if (now - lastChangeTime > config.RefreshDebounce) {
							lastChangeTime = now;
							traceop($"Watch file changed: {e.FullPath}");
							_ = triggerRetry("file-change");
						}
					};

					trace("FileSystemWatcher configured and enabled for auto-retry");
				} else {
					trace($"Watch file does not exist: {config.WatchFilePath} - auto-retry disabled");
				}
			} else {
				trace("No watch file specified - auto-retry disabled");
			}

			// Set up timer for UI updates - this runs on the main thread
			Terminal.Gui.Application.MainLoop.AddTimeout(config.UpdateInterval, (mainLoop) => {
				string newText, newStatus;
				lock (textLock) {
					newText   = currentText;
					newStatus = currentStatus;
				}

				// Update text content
				if (textView.Text.ToString() != newText) {
					traceop($"TIMER UPDATE: Updating UI text (lengths: {textView.Text.ToString().Length} -> {newText.Length})");
					textView.Text = newText;
					textView.SetNeedsDisplay();
					textView.SetFocus();
				}

				// Update status bar
				string expectedStatusText = $"~AUTO~ {newStatus}";
				if (statusBar.Items[2].Title != expectedStatusText) {
					traceop($"TIMER UPDATE: Updating status bar from '{statusBar.Items[2].Title}' to '{expectedStatusText}'");
					statusBar.Items[2] = new Terminal.Gui.StatusItem(Terminal.Gui.Key.Null, expectedStatusText, null);
					statusBar.SetNeedsDisplay();
				}

				return true; // Continue the timer
			});

			// Schedule initial load after UI starts - run in background
			trace("Scheduling initial content load");
			Terminal.Gui.Application.MainLoop.Invoke(() => {
				trace("Starting initial refresh");
				_ = triggerRetry("initial-load");
			});

			// Run the application
			trace("Starting Terminal.Gui application main loop");
			Terminal.Gui.Application.Run();
			trace("Terminal.Gui application main loop exited");

			// Cleanup
			trace("Disposing FileSystemWatcher");
			fileWatcher?.Dispose();
			tuiView.Dispose();

		} finally {
			trace("Shutting down Terminal.Gui application");
			refreshSemaphore?.Dispose();
			Terminal.Gui.Application.Shutdown();
			traceout();
		}
	}
}