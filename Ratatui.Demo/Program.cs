using System.Reflection;
using Ratatui.Demo;
using Ratatui;
using Spectre.Console.Rendering;
using Thaum.App.RatatuiTUI;
using Thaum.Core.Utils;
using Thaum.Meta;
using static Ratatui.Colors;
using static Thaum.App.RatatuiTUI.Rat;

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
			int     result = Run(tui, fps: 30, output: output);

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
		public readonly  List<IDemo> demos;
		public readonly  HomeScreen  homeScreen;
		private readonly FloatMgr    floats = new();
		private          bool        helpOverlay;

		public DemoTUI(List<IDemo> demos) {
			this.demos    = demos;
			homeScreen    = new HomeScreen(this);
			CurrentScreen = homeScreen;
		}

		public override void OnInit() {
			// Ensure we start on the home screen and run its enter lifecycle
			Navigate(homeScreen, forceReactivate: true);
			// Global overlay keys
			keys.RegisterKey(KeyCode.F2, "overlay/help", _ => {
				ToggleHelp();
				return true;
			});
			keys.RegisterKey(KeyCode.ESC, "overlay/close", _ => {
				if (helpOverlay) {
					ToggleHelp(false);
					return true;
				}
				return false;
			});
		}

		private void ToggleHelp(bool? state = null) {
			helpOverlay = state ?? !helpOverlay;
			if (helpOverlay) {
				var spec = new RectSpec(AnchorSpec.Center, SizeSpec.Pct(0.6, 0.5), Padding.All(1));
				floats.Show(new FloatSpec(
					Id: "Help",
					Bounds: spec,
					Modal: true,
					Z: 100,
					Chrome: new Style(fg: WHITE),
					Draw: (term, rect) => {
						var p = term.NewParagraph("")
							.AppendLine("Ratatui.cs Demo Browser", new Style(fg: LCYAN, bold: true))
							.AppendLine("")
							.AppendLine("Navigate demos with ↑/↓ and Enter.")
							.AppendLine("Type to filter; Backspace to delete; Esc clears.")
							.AppendLine("F2 toggles this help; Esc closes overlays.", new Style(fg: GRAY));
						term.Draw(p, rect);
					}));
			} else {
				floats.Hide("Help");
			}
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
						if (instance is IEmbeddedDemo emb) {
							var screen = emb.Create(this);
							Navigate(screen, forceReactivate: true);
						} else {
							instance.Run();
							info("Running {InstanceName}...", instance.Name);
						}
					} catch (Exception ex) {
						err($"Demo '{chosen.Name}' crashed: {ex}");
						err("Press any key to return to the demo browser...");
						Console.ReadKey(intercept: true);
					}
					// If we ran a blocking demo, reset back to home
					if (!(chosen is IEmbeddedDemo)) {
						Navigate(homeScreen);
						CurrentScreen.OnEnter();
					}
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
			floats.Draw(term, rect);
		}

            public override bool OnEvent(Event ev) {
                // If overlay is visible, trap input except overlay keys
                if (helpOverlay && ev.Kind == EventKind.Key) {
                    var code = (KeyCode)ev.Key.Code;
                    if (code == KeyCode.ESC || code == KeyCode.ENTER || code == KeyCode.F2) {
                        ToggleHelp(false);
                        return true;
                    }
                    return true; // consume all other keys while overlay is displayed
                }
                // First try routed keybinds
                if (ev.Kind == EventKind.Key && keys.Handle(ev)) return true;
                // Focused widget gets next shot
                if (ev.Kind == EventKind.Key && Focus.Dispatch(ev)) return true;
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
	public partial class HomeScreen : Screen<DemoTUI> {
		public IDemo? ChosenDemo { get; set; }

		private readonly RatList<IDemo> _list;

		public HomeScreen(DemoTUI tui) : base(tui) {
			_list = new RatList<IDemo>();
			_list.SetSource(tui.demos, d => d.Name);
			_list.ConfigureSearch(
				d => d.Name,
				d => d.Description,
				d => string.Join(" ", d.Tags));
			_list.FocusSearchEnabled = true;
			_list.FocusOnActivate = d => {
				ChosenDemo = d;
				End();
				return true;
			};
			_list.FocusPageStep = 5;
		}

		public override Task OnEnter() {
			keys.RegisterKey(KeyCode.TAB, "focus/next", _ => {
				Focus.Next();
				return true;
			});
			return Task.CompletedTask;
		}

		public override void Draw(Terminal term, Rect area) {
			int w = area.Width;
			int h = area.Height;

			int headerHeight = Math.Min(5, Math.Max(3, h / 4));
			var headerArea   = new Rect(area.X, area.Y, w, Math.Min(headerHeight, h));
			var rows         = Ui.Rows(headerArea, new[] { Ui.U.Px(1), Ui.U.Px(1), Ui.U.Flex(1) });

			string searchLabel = string.IsNullOrEmpty(_list.Query) ? "(type to search)" : _list.Query + "_";

			term.Draw(Paragraph("Ratatui.cs Demo Suite", LCYAN, BLACK), rows[0]);
			term.Draw(Paragraph("Ratatui.cs Demo Suite", LCYAN), rows[0]);
			term.Draw(Paragraph($"Search: {searchLabel}", LYELLOW), rows[1]);
			term.Draw(Paragraph("Type to filter • ↑/↓ select • Enter run • Backspace delete • Esc clear • Tab focus", GRAY), rows[2]);

			int listTop    = Math.Min(headerHeight, h);
			int listHeight = Math.Max(0, h - listTop - 1);

			Rect rList = new Rect(area.X, area.Y + listTop, w, listHeight);

			if (_list.Count == 0) {
				var empty = term.NewParagraph("").AppendLine("No demos match your search.", new Style(fg: RED, bold: true));
				term.Draw(empty, rList);
			} else {
				_list.DrawChunked3(
					term,
					rList,
					id: "home.list",
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

			// Footer: auto-help from registered keys (show first 6)
			Chrome.StatusHelp(term, new Rect(area.X, area.Y + Math.Max(0, h - 1), w, 1), keys.GetHelp(6));
		}


		// Search and selection handled by RatList
	}
}
