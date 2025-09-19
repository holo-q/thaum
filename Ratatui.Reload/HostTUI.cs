using Ratatui;
using Ratatui.Reload.Abstractions;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Reload;

/// <summary>
/// Fallback TUI that shows host status, error messages, and loading states.
/// Always available when no plugin TUI can be loaded.
/// </summary>
public class HostTUI : RatTUI, IReloadableApp {
    private string _message = "Initializing...";
    private bool _isError = false;

    public void SetMessage(string message, bool isError = false) {
        _message = message;
        _isError = isError;
        Invalidate();
    }

    public void SetLoading(string operation) {
        _message = $"Loading: {operation}";
        _isError = false;
        Invalidate();
    }

    public void SetError(string error) {
        _message = $"Error: {error}";
        _isError = true;
        Invalidate();
    }

    public override void OnDraw(Terminal term) {
        var (w, h) = term.Size();

        // Center the message
        int msgWidth = Math.Min(_message.Length + 4, w - 2);
        int msgHeight = 3;
        int x = (w - msgWidth) / 2;
        int y = (h - msgHeight) / 2;

        var rect = new Rect(x, y, msgWidth, msgHeight);
        var color = _isError ? Color.LightRed : Color.LightBlue;

        using var para = new Paragraph(_message)
            .Title("Thaum Host", border: true)
            .Style(new Style(fg: color));

        term.Draw(para, rect);
    }

    public override bool OnEvent(Event ev) {
        // Host TUI can handle basic events like quit
        if (ev.Kind == EventKind.Key && ev.Key.CodeEnum == KeyCode.Char) {
            char ch = (char)ev.Key.Char;
            if (ch == 'q' || ch == 'Q') {
                // Signal quit
                return true;
            }
        }
        return false;
    }

    public async Task<bool> RunAsync(Terminal terminal, CancellationToken cancellationToken) {
        await Task.CompletedTask;
        return true;
    }

    public override void Dispose() {
        // Host TUI has no resources to dispose
        base.Dispose();
    }
}