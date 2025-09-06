namespace Thaum.CLI.Interactive;

// TODO make this fields
// TODO eww dictionary for parameters wtf

public class TUIConfig {
	/// <summary>
	/// Optional file path to watch for changes that trigger auto-refresh
	/// </summary>
	public string? WatchFilePath { get; set; }

	/// <summary>
	/// Debounce time for file change detection
	/// </summary>
	public TimeSpan RefreshDebounce { get; set; } = TimeSpan.FromMilliseconds(500);

	/// <summary>
	/// Additional parameters to pass to the TUI view
	/// </summary>
	public Dictionary<string, object>? Parameters { get; set; }

	/// <summary>
	/// Timer interval for UI updates (default 100ms)
	/// </summary>
	public TimeSpan UpdateInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}