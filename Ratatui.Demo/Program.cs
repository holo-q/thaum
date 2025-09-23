using System.Reflection;
using Ratatui.Demo;
using Ratatui;
using Thaum.App.RatatuiTUI;
using Thaum.Core.Utils;
using Thaum.Meta;
using static Ratatui.Colors;

[LoggingIntrinsics]
public static partial class Program {
	public static int Main(string[] args) {
		return RunReal(args);
	}

	private static int RunReal(string[] args) {
		// Use TUI logging to avoid writing to stdout while alt-screen is active
		Logging.SetupTUI(init: Logging.SessionInit.Split);
		try {
			var asm   = Assembly.GetExecutingAssembly();
			var demos = new List<IDemo>();

			foreach (var type in asm.GetTypes()) {
				if (type.IsAbstract || type.IsInterface) continue;
				if (!typeof(IDemo).IsAssignableFrom(type)) continue;
				if (type.GetConstructor(Type.EmptyTypes) is null) continue;
				if (Activator.CreateInstance(type) is IDemo demo1)
					demos.Add(demo1);
			}

			demos.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

			info("Discovered {DemosCount} demos:", demos.Count);
			foreach (var demo in demos) {
				info("  - {DemoName}: {DemoDescription}", demo.Name, demo.Description);
			}

			if (demos.Count == 0) {
				err("No demos discovered in Ratatui.Demo assembly.");
				return 1;
			}

			// Parse simple CLI switches: --ascii, --ascii-loop, --run <name>
			RunOutput output  = RunOutput.Terminal;
			string?   runDemo = null;
			foreach (var a in args) {
				if (string.Equals(a, "--ascii", StringComparison.OrdinalIgnoreCase)) output           = RunOutput.AsciiOnce;
				else if (string.Equals(a, "--ascii-loop", StringComparison.OrdinalIgnoreCase)) output = RunOutput.AsciiLoop;
			}
			for (int i = 0; i < args.Length; i++) {
				if (string.Equals(args[i], "--run", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
					runDemo = args[i + 1];
					break;
				}
			}

			// If --run is specified, run that demo directly (env-driven ascii)
			if (!string.IsNullOrEmpty(runDemo)) {
				IDemo? chosen = demos.FirstOrDefault(d => d.Name.Contains(runDemo, StringComparison.OrdinalIgnoreCase))
				                ?? demos.FirstOrDefault(d => string.Equals(d.Name, runDemo, StringComparison.OrdinalIgnoreCase));
				if (chosen is null) {
					err($"Demo not found: {runDemo}");
					return 2;
				}
				// Env override to force ASCII in frame-loop demos if requested
				string? old = Environment.GetEnvironmentVariable("THAUM_TUI_OUTPUT");
				try {
					if (output == RunOutput.AsciiOnce) Environment.SetEnvironmentVariable("THAUM_TUI_OUTPUT", "ascii");
					else if (output == RunOutput.AsciiLoop) Environment.SetEnvironmentVariable("THAUM_TUI_OUTPUT", "ascii-loop");
					return chosen.Run();
				} finally {
					Environment.SetEnvironmentVariable("THAUM_TUI_OUTPUT", old);
				}
			}

			DemoTUI tui    = new(demos);
			int     result = Rat.Run(tui, fps: 30, output: output);

			return result;
		} catch (Exception ex) {
			// Ensure a readable error after alt-screen exits
			Logging.WriteException(ex);
			try {
				err(ex.ToString());
			} catch { }
			return 1;
		}
	}

	[LoggingIntrinsics]
	public partial class DemoTUI : RatTUI<DemoTUI> {
		public readonly List<IDemo> demos;
		public readonly HomeScreen  homeScreen;

		public DemoTUI(List<IDemo> demos) {
			this.demos    = demos;
			homeScreen    = new HomeScreen(this);
			CurrentScreen = homeScreen;
		}

		public override void OnInit() {
			// Ensure we start on the home screen and run its enter lifecycle
			Navigate(homeScreen, forceReactivate: true);
		}

		public static IDemo? TryCreateFreshInstance(IDemo demo) {
			var type = demo.GetType();
			try {
				if (type.GetConstructor(Type.EmptyTypes) is null) return null;
				return Activator.CreateInstance(type) as IDemo;
			} catch {
				return null;
			}
		}

		protected override Screen? GetNextScreen() {
			if (homeScreen) {
				// HOME -> DEMO
				if (homeScreen.ChosenDemo != null) {
					IDemo? chosen = homeScreen.ChosenDemo;
					try {
						IDemo? instance = TryCreateFreshInstance(chosen) ?? chosen;
						info("Running {InstanceName}...", instance.Name);
						instance.Run();
					} catch (Exception ex) {
						err($"Demo '{chosen.Name}' crashed: {ex}");
						err("Press any key to return to the demo browser...");
						Console.ReadKey(intercept: true);
					}
					// Reset the screen for next selection
					Navigate(homeScreen);
					CurrentScreen.OnEnter();
				}
			} else {
				// DEMO -> HOME
				if (CurrentScreen.Done) {
					return homeScreen;
				}
			}

			return null;
		}

		public override void OnTick(TimeSpan dt) {
			CurrentScreen?.Tick(dt);
		}

		public override void OnDraw(Terminal term) {
			var rect = new Rect(0, 0, Size.X, Size.Y);
			CurrentScreen.Draw(term, rect);
		}

		public override bool OnEvent(Event ev) {
			// First try routed keybinds
			if (ev.Kind == EventKind.Key && keys.Handle(ev)) return true;
			// Then let the current screen handle miscellaneous keys
			return CurrentScreen.OnKey(ev);
		}

		public override bool OnKey(Event ev) {
			// TODO bubble up to Screen.HandleKey which invokes this.OnKey and does this if that returned false
			return CurrentScreen.OnKey(ev);
		}

		public override void OnResize(int width, int height) {
			Size = new Vec2(width, height);
			CurrentScreen?.OnResize(width, height);
		}
	}

	[LoggingIntrinsics]
	public partial class HomeScreen : Screen<Program.DemoTUI> {
		public IDemo? ChosenDemo { get; set; }

        private readonly RatList<IDemo> _list = new();

        // Row offset handled internally by RatList.DrawChunked3

		public HomeScreen(Program.DemoTUI tui) : base(tui) { }

		public override void Draw(Terminal term, Rect area) {
			int w = area.Width;
			int h = area.Height;

			int    headerHeight = Math.Min(5, Math.Max(3, h / 4));
        string searchLabel  = string.IsNullOrEmpty(_list.Query) ? "(type to search)" : _list.Query + "_";

			var header = term.NewParagraph("")
				.AppendLine("Ratatui.cs Demo Suite", new Style(fg: LCYAN, bold: true))
            .AppendLine($"Demos: {_list.Count}/{tui.demos.Count}    Search: {searchLabel}", new Style(fg: LYELLOW))
				.AppendLine("Type to filter • ↑/↓ select • Enter run • Backspace delete • Esc exit", new Style(fg: GRAY));
			term.Draw(header, new Rect(area.X, area.Y, w, Math.Min(headerHeight, h)));

			int listTop    = Math.Min(headerHeight, h);
			int listHeight = Math.Max(0, h - listTop - 1);

				var listRect = new Rect(area.X, area.Y + listTop, w, listHeight);

        if (_list.Count == 0) {
            var empty = term.NewParagraph("").AppendLine("No demos match your search.", new Style(fg: RED, bold: true));
            term.Draw(empty, listRect);
        } else {
	            _list.DrawChunked3(
	                term,
	                listRect,
	                t => t.Name,
                t => t.Description,
                t => t.Tags.Length > 0 ? string.Join("  ", t.Tags.Select(tag => $"#{tag}")) : string.Empty,
                highlightStyle: new Style(BLACK, GREEN, bold: true),
                highlightSymbol: "▶ ",
                titleNormal: new Style(WHITE, bold: true),
                descNormal: new Style(GRAY),
                tagsNormal: new Style(CYAN, italic: true),
                descWhenSelected: new Style(WHITE, GREEN),
                tagsWhenSelected: new Style(WHITE, GREEN, italic: true));
        }

			var footer = term.NewParagraph("").AppendLine("Press Esc to exit, Enter to launch a demo", new Style(fg: GRAY));
			term.Draw(footer, new Rect(area.X, area.Y + Math.Max(0, h - 1), w, 1));
		}

        public override Task OnEnter() {
            // Initialize source + search keys; preserve selection by demo name across queries
            _list.SetSource(tui.demos, d => d.Name);
            _list.ConfigureSearch(
                d => d.Name,
                d => d.Description,
                d => string.Join(" ", d.Tags ?? Array.Empty<string>()))
                ;
            _list.SetQuery(_list.Query);
            ConfigureKeys();
            return Task.CompletedTask;
        }

		private void ConfigureKeys() {
            keys.RegisterKey(KeyCode.Up, "nav", _ => { if (_list.Count > 0) _list.Navigate(-1); return true; });

            keys.RegisterKey(KeyCode.Down, "nav", _ => { if (_list.Count > 0) _list.Navigate(+1); return true; });

            keys.RegisterKey(KeyCode.PAGE_UP, "nav", _ => { if (_list.Count > 0) _list.Navigate(-5); return true; });

            keys.RegisterKey(KeyCode.PAGE_DOWN, "nav", _ => { if (_list.Count > 0) _list.Navigate(+5); return true; });

            keys.RegisterKey(KeyCode.Home, "nav", _ => { if (_list.Count > 0) _list.NavigateToFirst(); return true; });

            keys.RegisterKey(KeyCode.End, "nav", _ => { if (_list.Count > 0) _list.NavigateToLast(); return true; });

            keys.RegisterKey(KeyCode.Backspace, "search", _ => { if (_list.Query.Length > 0) { _list.SetQuery(_list.Query[..^1]); } return true; });

            keys.RegisterKey(KeyCode.Delete, "search", _ => { if (_list.Query.Length > 0) { _list.SetQuery(string.Empty); } return true; });

            keys.RegisterKey(KeyCode.ESC, "search/exit", _ => { if (_list.Query.Length > 0) { _list.SetQuery(string.Empty); } else { End(); } return true; });

            keys.RegisterKey(KeyCode.ENTER, "select", _ => { if (_list.SafeSelected is IDemo d) { ChosenDemo = d; End(); } return true; });

            keys.Register("char", "search",
                ev => ev is { Kind: EventKind.Key, Key.Code: (ushort)KeyCode.Char } && ev.Key.Char != 0,
                (ev, _) => { char ch = (char)ev.Key.Char; if ((ch is 'q' or 'Q') && string.IsNullOrEmpty(_list.Query)) { End(); } else if (!char.IsControl(ch)) { _list.SetQuery(_list.Query + ch); } return true; });
		}

        // Search and selection handled by RatList
	}
}
