using Ratatui;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging;
using Thaum.Core.Utils;
using Thaum.Meta;

namespace Thaum.App.RatatuiTUI;

[LoggingIntrinsics]
public partial class KeybindManager {
	private readonly List<Binding>    _bindings   = [];
	private readonly List<KeyBinding> _helpBuffer = [];

	private readonly struct Binding {
		public readonly Func<Event, bool>                 match;
		public readonly Func<Event, ThaumTUI.State, bool> handler;
		public readonly string                            helpKey;
		public readonly string                            description;

		public Binding(Func<Event, bool> match, Func<Event, ThaumTUI.State, bool> handler, string helpKey, string description) {
			this.match       = match;
			this.handler     = handler;
			this.helpKey     = helpKey;
			this.description = description;
		}
	}

	public void Clear() => _bindings.Clear();

	public KeybindManager Register(string helpKey, string description, Func<Event, bool> match, Func<Event, ThaumTUI.State, bool> handler) {
		_bindings.Add(new Binding(match, handler, helpKey, description));
		trace("Registered binding: {Key} — {Desc}", helpKey, description);
		return this;
	}

	public KeybindManager RegisterChar(char ch, string description, Func<ThaumTUI.State, bool> handler)
		=> Register(ch.ToString(), description, ev => ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char } && (char)ev.Key.Char == ch, (ev, app) => handler(app));

	public KeybindManager RegisterKey(KeyCode code, string helpKey, string description, Func<ThaumTUI.State, bool> handler)
		=> Register(helpKey, description, ev => ev.Kind == EventKind.Key && ev.Key.CodeEnum == code, (ev, app) => handler(app));

	public bool Handle(Event ev, ThaumTUI.State app) {
		bool any = false;
		foreach (Binding b in _bindings) {
			bool m = false;
			try {
				m = b.match(ev);
			} catch (Exception ex) { err(ex, "Key match failed for {Key}", b.helpKey); }
			if (m) {
				trace("Matched key '{Key}' ({Desc})", b.helpKey, b.description);
				bool ok = false;
				try {
					ok = b.handler(ev, app);
				} catch (Exception ex) {
					err(ex, "Key handler threw for {Key}", b.helpKey);
				}
				return ok;
			}
			any = true;
		}
		trace("No binding matched: code={Code} char='{Char}' ctrl={Ctrl} alt={Alt} shift={Shift}", ev.Key.CodeEnum, (char)ev.Key.Char, ev.Key.Ctrl, ev.Key.Alt, ev.Key.Shift);
		return false;
	}

	public IReadOnlyList<KeyBinding> GetHelp(int max) {
		_helpBuffer.Clear();
		int count = Math.Min(max, _bindings.Count);
		for (int i = 0; i < count; i++) {
			Binding b = _bindings[i];
			_helpBuffer.Add(new KeyBinding(b.helpKey, b.description));
		}
		return _helpBuffer;
	}

	public void DumpBindings(string context) {
		try {
			trace("[{Context}] {Count} key bindings registered", context, _bindings.Count);
			foreach (Binding b in _bindings)
				trace("[{Context}]  {Key} — {Desc}", context, b.helpKey, b.description);
		} catch { /* best-effort */
		}
	}

	// Fluent API extensions
	public KeybindManager RegisterNavigation(KeyCode                    up,
	                                         KeyCode                    down,
	                                         string                     description,
	                                         Func<ThaumTUI.State, bool> upHandler,
	                                         Func<ThaumTUI.State, bool> downHandler) {
		RegisterKey(up, "↑", $"{description} up", upHandler);
		RegisterKey(down, "↓", $"{description} down", downHandler);
		return this;
	}

	public KeybindManager RegisterVimNavigation(string                     description,
	                                            Func<ThaumTUI.State, bool> upHandler,
	                                            Func<ThaumTUI.State, bool> downHandler) {
		RegisterChar('k', $"↑ {description}", upHandler);
		RegisterChar('j', $"↓ {description}", downHandler);
		return this;
	}

	public KeybindManager RegisterPageNavigation(string                     description,
	                                             Func<ThaumTUI.State, bool> pageUpHandler,
	                                             Func<ThaumTUI.State, bool> pageDownHandler) {
		RegisterKey(KeyCode.PAGE_UP, "PgUp", $"{description} page up", pageUpHandler);
		RegisterKey(KeyCode.PAGE_DOWN, "PgDn", $"{description} page down", pageDownHandler);
		return this;
	}

	public KeybindManager RegisterCommonActions(
		Func<ThaumTUI.State, bool>? enterHandler  = null,
		Func<ThaumTUI.State, bool>? escapeHandler = null,
		Func<ThaumTUI.State, bool>? tabHandler    = null) {
		if (enterHandler != null) RegisterKey(KeyCode.ENTER, "Enter", "activate", enterHandler);
		if (escapeHandler != null) RegisterKey(KeyCode.ESC, "Esc", "cancel", escapeHandler);
		if (tabHandler != null) RegisterKey(KeyCode.TAB, "Tab", "switch", tabHandler);
		return this;
	}

	// Additional fluent utilities for building keybind sets
	public KeybindManager RegisterQuickNavigation(string                      context,
	                                              Func<ThaumTUI.State, bool>  upHandler,
	                                              Func<ThaumTUI.State, bool>  downHandler,
	                                              Func<ThaumTUI.State, bool>? pageUpHandler   = null,
	                                              Func<ThaumTUI.State, bool>? pageDownHandler = null) {
		RegisterKey(KeyCode.Up, "↑", $"{context} up", upHandler);
		RegisterKey(KeyCode.Down, "↓", $"{context} down", downHandler);
		RegisterChar('k', "↑", upHandler);
		RegisterChar('j', "↓", downHandler);

		if (pageUpHandler != null) RegisterKey(KeyCode.PAGE_UP, "PgUp", $"{context} page up", pageUpHandler);
		if (pageDownHandler != null) RegisterKey(KeyCode.PAGE_DOWN, "PgDn", $"{context} page down", pageDownHandler);

		return this;
	}

	public KeybindManager RegisterFilter(string           filterDescription,
	                                     Action           clearFilter,
	                                     Action<char>     addChar,
	                                     Action           backspace,
	                                     Func<char, bool> isValidChar) {
		RegisterChar('/', $"clear {filterDescription}", _ => {
			clearFilter();
			return true;
		});
		RegisterKey(KeyCode.Delete, "Backspace", $"edit {filterDescription}", _ => {
			backspace();
			return true;
		});
		Register("char", $"type {filterDescription}",
			ev => ev is { Kind: EventKind.Key, Key.CodeEnum: KeyCode.Char } && isValidChar((char)ev.Key.Char),
			(ev, _) => {
				addChar((char)ev.Key.Char);
				return true;
			});

		return this;
	}

	public KeybindManager RegisterGlobalActions(
		Func<ThaumTUI.State, bool>? quitHandler    = null,
		Func<ThaumTUI.State, bool>? helpHandler    = null,
		Func<ThaumTUI.State, bool>? refreshHandler = null) {
		if (quitHandler != null) {
			RegisterChar('q', "quit", quitHandler);
			RegisterKey(KeyCode.ESC, "Esc", "quit", quitHandler);
		}
		if (helpHandler != null) RegisterChar('?', "help", helpHandler);
		if (refreshHandler != null) RegisterKey(KeyCode.F5, "F5", "refresh", refreshHandler);

		return this;
	}
}