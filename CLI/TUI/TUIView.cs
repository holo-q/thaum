namespace Thaum.CLI.Interactive;

public interface TUIView : IDisposable {
	/// <summary>
	/// Initialize the TUI view with the provided container
	/// </summary>
	/// <param name="container">The Terminal.Gui container view</param>
	void Initialize(Terminal.Gui.View container);

	/// <summary>
	/// Refresh the view content asynchronously
	/// </summary>
	/// <param name="textCallback">Callback to update text content</param>
	/// <param name="statusCallback">Callback to update status message</param>
	Task RefreshAsync(Action<string> textCallback, Action<string> statusCallback);
}