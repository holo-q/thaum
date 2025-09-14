using Ratatui;

namespace Thaum.App.RatatuiTUI;

public sealed class KeybindManager
{
    private readonly List<Binding> _bindings = new();

    private readonly struct Binding
    {
        public readonly Func<Event, bool> match;
        public readonly Func<Event, ThaumTUI.State, bool> handler;
        public readonly string helpKey;
        public readonly string description;

        public Binding(Func<Event, bool> match, Func<Event, ThaumTUI.State, bool> handler, string helpKey, string description)
        { this.match = match; this.handler = handler; this.helpKey = helpKey; this.description = description; }
    }

    public void Clear() => _bindings.Clear();

    public KeybindManager Register(string helpKey, string description, Func<Event, bool> match, Func<Event, ThaumTUI.State, bool> handler)
    { _bindings.Add(new Binding(match, handler, helpKey, description)); return this; }

    public KeybindManager RegisterChar(char ch, string description, Func<ThaumTUI.State, bool> handler)
        => Register(ch.ToString(), description, ev => ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char } && (char)ev.Key.Char == ch, (ev, app) => handler(app));

    public KeybindManager RegisterKey(KeyCode code, string helpKey, string description, Func<ThaumTUI.State, bool> handler)
        => Register(helpKey, description, ev => ev.Kind == EventKind.Key && ev.Key.CodeEnum == code, (ev, app) => handler(app));

    public bool Handle(Event ev, ThaumTUI.State app)
    {
        foreach (Binding b in _bindings)
            if (b.match(ev)) return b.handler(ev, app);
        return false;
    }

    public IEnumerable<KeyBinding> GetHelp(int max)
    {
        int i = 0;
        foreach (Binding b in _bindings)
        {
            yield return new KeyBinding(b.helpKey, b.description);
            if (++i >= max) yield break;
        }
    }
}
