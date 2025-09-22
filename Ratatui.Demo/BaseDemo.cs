using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo;

public abstract class BaseDemo : IDemo {
    public abstract string Name { get; }
    public abstract string Description { get; }
    public virtual string[] Tags => [];

    public abstract int Run();

    protected static bool IsExitKey(Event ev) {
        if (ev.Kind != EventKind.Key) return false;
        var code = (KeyCode)ev.Key.Code;
        return code == KeyCode.ESC || (code == KeyCode.Char && (char)ev.Key.Char is 'q' or 'Q');
    }
}