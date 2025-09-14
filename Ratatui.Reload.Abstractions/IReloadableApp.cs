using System;
using Ratatui;

namespace Ratatui.Reload.Abstractions;

public interface IReloadableApp : IDisposable
{
    void Init(IReloadContext ctx);

    // Called when terminal resizes
    void OnResize(int width, int height);

    // Return true if event consumed
    bool HandleEvent(Event ev);

    // Per-frame update
    void Update(TimeSpan dt);

    // Draw current frame
    void Draw(Terminal term);

    // Optional state capture/restore for seamless reloads
    object? CaptureState();
    void RestoreState(object? state);
}

