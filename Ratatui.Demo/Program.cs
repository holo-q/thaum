using System.Reflection;
using Ratatui.Demo;
using Ratatui;
using Serilog;
using Thaum.App.RatatuiTUI;
using Thaum.Core.Utils;
using Thaum.Meta;

[LoggingIntrinsics]
public static partial class Program {
    public static int Main(string[] args) {
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

            DemoTUI tui    = new DemoTUI(demos);
            int     result = Rat.Run(tui, fps: 30);
            return result;
        } catch (Exception ex) {
            // Ensure a readable error after alt-screen exits
            Logging.WriteException(ex);
            try { Console.Error.WriteLine(ex.ToString()); } catch { }
            return 1;
        }
    }


	[LoggingIntrinsics]
	public partial class DemoTUI : RatTUI<DemoTUI> {
		public readonly List<IDemo> demos;
		public readonly HomeScreen  homeScreen;


        public DemoTUI(List<IDemo> demos) {
            this.demos = demos;
            homeScreen = new HomeScreen(this);
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

		private readonly List<IDemo> _filtered = [];

		private string _search        = string.Empty;
		private int    _selectedIndex = 0;

		public HomeScreen(Program.DemoTUI tui) : base(tui) { }

		[TraceWrap]
		public override void Draw(Terminal term, Rect area) {
			info("Draw::enter");

			int w = area.Width;
			int h = area.Height;

			int    headerHeight = Math.Min(5, Math.Max(3, h / 4));
			string searchLabel  = string.IsNullOrEmpty(_search) ? "(type to search)" : _search + "_";

			using (var header = new Paragraph("")
				       .AppendLine("Ratatui.cs Demo Suite", new Style(fg: Colors.LightCyan, bold: true))
				       .AppendLine($"Demos: {((IReadOnlyList<IDemo>)_filtered).Count}/{tui.demos.Count}    Search: {searchLabel}", new Style(fg: Colors.LightYellow))
				       .AppendLine("Type to filter • ↑/↓ select • Enter run • Backspace delete • Esc exit", new Style(fg: Colors.Gray))) {
				term.Draw(header, new Rect(area.X, area.Y, w, Math.Min(headerHeight, h)));
			}

			int listTop    = Math.Min(headerHeight, h);
			int listHeight = Math.Max(0, h - listTop - 1);

			using (var list = new Paragraph("")) {
				if (((IReadOnlyList<IDemo>)_filtered).Count == 0) {
					list.AppendLine("No demos match your search.", new Style(fg: Colors.Red, bold: true));
				} else {
					for (int i = 0; i < ((IReadOnlyList<IDemo>)_filtered).Count; i++) {
						var  demo     = ((IReadOnlyList<IDemo>)_filtered)[i];
						bool selected = i == _selectedIndex;
						var nameStyle = selected
							? new Style(fg: Colors.Black, bg: Colors.Green, bold: true)
							: new Style(fg: Colors.White, bold: true);
						var descStyle = selected
							? new Style(fg: Colors.White, bg: Colors.Green)
							: new Style(fg: Colors.Gray);

						list.AppendLine($"{(selected ? "▶" : " ")} {demo.Name}", nameStyle);
						list.AppendLine($"    {demo.Description}", descStyle);

						if (demo.Tags.Length > 0) {
							var tagLine = string.Join("  ", demo.Tags.Select(tag => $"#{tag}"));
							var tagStyle = selected
								? new Style(fg: Colors.White, bg: Colors.Green, italic: true)
								: new Style(fg: Colors.Cyan, italic: true);
							list.AppendLine($"    {tagLine}", tagStyle);
						}

						list.AppendLine(string.Empty);
					}
				}

				term.Draw(list, new Rect(area.X, area.Y + listTop, w, listHeight));
			}

			using (var footer = new Paragraph("").AppendLine("Press Esc to exit, Enter to launch a demo", new Style(fg: Colors.Gray))) {
				term.Draw(footer, new Rect(area.X, area.Y + Math.Max(0, h - 1), w, 1));
			}

			info("Draw::exit");
		}

		public override Task OnEnter() {
			ApplyFilter();
			ConfigureKeys();
			return Task.CompletedTask;
		}

		private void ConfigureKeys() {
			keys.RegisterKey(KeyCode.Up, "↑", "nav", _ => {
				if (_filtered.Count > 0)
					_selectedIndex = Math.Max(0, _selectedIndex - 1);
				return true;
			});

			keys.RegisterKey(KeyCode.Down, "↓", "nav", _ => {
				if (_filtered.Count > 0)
					_selectedIndex = Math.Min(_filtered.Count - 1, _selectedIndex + 1);
				return true;
			});

			keys.RegisterKey(KeyCode.PAGE_UP, "PgUp", "nav", _ => {
				if (_filtered.Count > 0)
					_selectedIndex = Math.Max(0, _selectedIndex - 5);
				return true;
			});

			keys.RegisterKey(KeyCode.PAGE_DOWN, "PgDn", "nav", _ => {
				if (_filtered.Count > 0)
					_selectedIndex = Math.Min(_filtered.Count - 1, _selectedIndex + 5);
				return true;
			});

			keys.RegisterKey(KeyCode.Home, "Home", "nav", _ => {
				if (_filtered.Count > 0)
					_selectedIndex = 0;
				return true;
			});

			keys.RegisterKey(KeyCode.End, "End", "nav", _ => {
				if (_filtered.Count > 0)
					_selectedIndex = _filtered.Count - 1;
				return true;
			});

			keys.RegisterKey(KeyCode.Backspace, "Bksp", "search", _ => {
				if (_search.Length > 0) {
					_search = _search[..^1];
					ApplyFilter();
				}
				return true;
			});

			keys.RegisterKey(KeyCode.Delete, "Del", "search", _ => {
				if (_search.Length > 0) {
					_search = string.Empty;
					ApplyFilter();
				}
				return true;
			});

			keys.RegisterKey(KeyCode.ESC, "Esc", "search/exit", _ => {
				if (_search.Length > 0) {
					_search = string.Empty;
					ApplyFilter();
				} else {
					tui.Quit();
				}
				return true;
			});

			keys.RegisterKey(KeyCode.ENTER, "Enter", "select", _ => {
				if (_selectedIndex >= 0 && _selectedIndex < _filtered.Count) {
					ChosenDemo = _filtered[_selectedIndex];
					tui.Quit();
				}
				return true;
			});

			keys.Register("char", "search",
				ev => ev is { Kind: EventKind.Key, Key.Code: (ushort)KeyCode.Char } && ev.Key.Char != 0,
				(ev, _) => {
					char ch = (char)ev.Key.Char;
					if ((ch is 'q' or 'Q') && string.IsNullOrEmpty(_search)) {
						tui.Quit();
					} else if (!char.IsControl(ch)) {
						_search += ch;
						ApplyFilter();
					}
					return true;
				});
		}

		private void ApplyFilter() {
			if (string.IsNullOrWhiteSpace(_search)) {
				_filtered.Clear();
				_filtered.AddRange(tui.demos);
			} else {
				_filtered.Clear();
				_filtered.AddRange(tui.demos
					.Where(demo => Matches(demo, _search))
					.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase));
			}

			if (_filtered.Count == 0)
				_selectedIndex = -1;
			else if (_selectedIndex < 0)
				_selectedIndex = 0;
			else
				_selectedIndex = Math.Clamp(_selectedIndex, 0, _filtered.Count - 1);
		}


		private static bool Matches(IDemo demo, string query) {
			const StringComparison COMPARISON = StringComparison.OrdinalIgnoreCase;

			if (string.IsNullOrWhiteSpace(query)) return true;
			if (demo.Name.Contains(query, COMPARISON)) return true;
			if (demo.Description.Contains(query, COMPARISON)) return true;
			return demo.Tags.Any(tag => tag.Contains(query, COMPARISON));
		}
	}
}
