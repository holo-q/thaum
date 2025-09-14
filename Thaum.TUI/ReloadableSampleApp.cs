using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Ratatui;
using Ratatui.Reload.Abstractions;

namespace Thaum.TUI;

public sealed class ReloadableSampleApp : IReloadableApp
{
    private IReloadContext _ctx = default!;
    private int _counter;
    private DateTime _start = DateTime.UtcNow;
    private (int w, int h) _size;

    public void Init(IReloadContext ctx)
    {
        _ctx = ctx;
        _ctx.Logger.LogInformation("Sample app initialized at {Time}", DateTime.UtcNow);
    }

    public void OnResize(int width, int height)
    {
        _size = (width, height);
    }

    public bool HandleEvent(Event ev)
    {
        if (ev.Kind == EventKind.Key && ev.Key.CodeEnum == KeyCode.Char)
        {
            char ch = (char)ev.Key.Char;
            if (ch == '+') { _counter++; return true; }
            if (ch == '-') { _counter--; return true; }
        }
        if (ev.Kind == EventKind.Key && ev.Key.CodeEnum == KeyCode.Char && (char)ev.Key.Char == 'q')
        {
            // Allow host to quit
            (_ctx as IDisposable)?.Dispose();
            return true;
        }
        return false;
    }

    public void Update(TimeSpan dt)
    {
        // no-op
    }

    public void Draw(Terminal term)
    {
        var rect = new Rect(0, 0, Math.Max(1, _size.w), Math.Max(1, _size.h));
        using var p = new Paragraph("").Title("Ratatui Hot Reload Demo", border: true);
        var sb = new StringBuilder();
        sb.AppendLine($"Now: {DateTime.UtcNow:HH:mm:ss}");
        sb.AppendLine($"Uptime: {(DateTime.UtcNow - _start):hh\:mm\:ss}");
        sb.AppendLine($"Counter (+/-): {_counter}");
        sb.AppendLine($"Project: {_ctx.ProjectPath}");
        sb.AppendLine("Edit this file and save to see reload!");
        p.AppendSpan(sb.ToString());
        term.Draw(p, rect);
    }

    public object? CaptureState() => _counter;

    public void RestoreState(object? state)
    {
        if (state is int i) _counter = i;
    }

    public void Dispose()
    {
    }
}

