using Terminal.Gui;
using Terminal.Gui.ViewBase;

namespace Thaum.CLI.Interactive;

public abstract class TUIView  {
	/// <summary>
	/// Initialize the TUI view with the provided container
	/// </summary>
	/// <param name="container">The Terminal.Gui container view</param>
	public abstract void Initialize(View container);

	/// <summary>
	/// Refresh the view content asynchronously
	/// </summary>
	/// <param name="textCallback">Callback to update text content</param>
	/// <param name="statusCallback">Callback to update status message</param>
	public abstract Task RefreshAsync(Action<string> textCallback, Action<string> statusCallback);
}