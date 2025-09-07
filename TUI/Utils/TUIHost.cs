using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Terminal.Gui.App;
using Terminal.Gui.Views;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using static Thaum.Core.Utils.Tracer;

namespace Thaum.CLI.Interactive;

public class TUIHost<TView> where TView : TUIView, new() {
	private readonly ILogger _logger;

	public TUIHost(ILogger logger) {
		_logger = logger;
	}

	[RequiresUnreferencedCode("Uses reflection for TUI initialization")]
	public async Task RunAsync(TUIConfig config) {
		tracein(parameters: new { config });

		using var scope = trace_scope("InteractiveTuiHost.RunAsync");
		trace("Initializing Terminal.Gui application");
		Application.Init();

		// Use a semaphore to prevent multiple concurrent refresh executions
		var refreshSemaphore = new SemaphoreSlim(1, 1);

		// Thread-safe shared state for UI updates
		var    textLock      = new object();
		string currentText   = "Loading...";
		string currentStatus = "Starting...";

		try {
			trace("Creating Terminal.Gui main view without borders");
			// Use a simple View instead of Window to avoid borders
			var mainView = new View() {
				X        = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(),
				CanFocus = true
			};

			trace("Creating status bar with keyboard shortcuts");
			// Status bar with shortcuts (display only - actual bindings are global)
			var statusBar = new StatusBar([
				new(Key.Space, "~R/SPACE~ Retry", null),
				new(Key.Q, "~Q/ESC~ Quit", null),
				new(Key.Empty, "~AUTO~ Starting...", null)
			]);

			trace("Creating text view directly with proper colors");
			// Simplified: Just a TextView directly in the main area, no ScrollView
			var textView = new TextView() {
				X        = 0, Y = 0,
				Width    = Dim.Fill(),
				Height   = Dim.Fill() - 1, // Leave room for status bar
				ReadOnly = true,
				Text     = "Loading...",
				WordWrap = true
			};

			// v2: Color schemes are set automatically - terminal's native colors will be used

			// Initialize the TUI view
			trace("Initializing TUI view");
			var tuiView = new TView();

			// Pass parameters if the view supports it
			if (tuiView is TryTUI tryView && config.Parameters != null) {
				tryView.SetParameters(config.Parameters);
			}

			tuiView.Initialize(mainView);

			// Helper function to trigger retry
			async Task TriggerRetry(string source) {
				traceop($"Triggering retry from {source}");
				if (refreshSemaphore.CurrentCount > 0) {
					lock (textLock) {
						currentStatus = $"Auto-retry ({source})...";
					}
					_ = Task.Run(async () => {
						await refreshSemaphore.WaitAsync();
						try {
							await tuiView.RefreshAsync((text) => {
								traceop($"UI CALLBACK: Updating currentText to length {text.Length}");
								lock (textLock) {
									currentText = text;
								}
							}, (status) => {
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
			}

			// Global key bindings are handled through the mainView's KeyBindings in v2
			trace("Setting up global key bindings");
			mainView.KeyBindings.Add(Key.Q, Command.Quit);
			mainView.KeyBindings.Add(Key.Esc, Command.Quit);
			mainView.KeyBindings.Add(Key.Space, () => {
				traceop("User pressed SPACE - attempting manual retry");
				_ = TriggerRetry("key:SPACE");
				return true;
			});
			mainView.KeyBindings.Add(Key.R, () => {
				traceop("User pressed R - attempting manual retry");
				_ = TriggerRetry("key:R");
				return true;
			});

			trace("Adding components to Terminal.Gui layout - simplified structure");
			// Simplified structure: TextView directly in main view
			mainView.Add(textView);
			Application.Top.Add(mainView);
			Application.Top.Add(statusBar);

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
							_ = TriggerRetry("file-change");
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
			Application.AddTimeout(config.UpdateInterval, () => {
				string newText, newStatus;
				lock (textLock) {
					newText   = currentText;
					newStatus = currentStatus;
				}

				// Update text content
				if (textView.Text.ToString() != newText) {
					traceop($"TIMER UPDATE: Updating UI text (lengths: {textView.Text.ToString().Length} -> {newText.Length})");
					textView.Text = newText;
					textView.SetNeedsDraw();
					textView.SetFocus();
				}

				// Update status bar
				string expectedStatusText = $"~AUTO~ {newStatus}";
				if (((Shortcut)statusBar.SubViews.ElementAt(2)).Title != expectedStatusText) {
					traceop($"TIMER UPDATE: Updating status bar from '{((Shortcut)statusBar.SubViews.ElementAt(2)).Title}' to '{expectedStatusText}'");
					((Shortcut)statusBar.SubViews.ElementAt(2)).Title = expectedStatusText;
					statusBar.SetNeedsDraw();
				}

				return true; // Continue the timer
			});

			// Schedule initial load after UI starts - run in background
			trace("Scheduling initial content load");
			Application.Invoke(() => {
				trace("Starting initial refresh");
				_ = TriggerRetry("initial-load");
			});

			// Run the application
			trace("Starting Terminal.Gui application main loop");
			Application.Run();
			trace("Terminal.Gui application main loop exited");

			// Cleanup
			trace("Disposing FileSystemWatcher");
			fileWatcher?.Dispose();
		} finally {
			trace("Shutting down Terminal.Gui application");
			refreshSemaphore?.Dispose();
			Application.Shutdown();
			traceout();
		}
	}
}