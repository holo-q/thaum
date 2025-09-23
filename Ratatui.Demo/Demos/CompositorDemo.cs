using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class CompositorDemo : BaseDemo {
	public override string   Name        => "Compositor Demo";
	public override string   Description => "Demonstrates RGBA blending, headless widgets, and sub-compositor overlays";
	public override string[] Tags        => ["blending", "compositor", "overlay", "alpha"];

	public override int Run() {
		return Rat.Run(frame => {
			frame.Clear();
			int w = frame.Width, h = frame.Height;

			using (var para = new Paragraph("")
				       .AppendLine("Ratatui.cs — Compositor Demo", new Style(fg: Colors.LCYAN))
				       .AppendLine(" ")
				       .AppendLine("- RGBA blending (CPU)", new Style(fg: Colors.LIGHTGREEN))
				       .AppendLine("- Headless widgets → cells", new Style(fg: Colors.LIGHTGREEN))
				       .AppendLine("- Sub-compositor overlays", new Style(fg: Colors.LIGHTGREEN))
				       .AppendLine("- Press Q/Esc to exit", new Style(fg: Colors.YELLOW))) {
				frame.Draw(para, new Rect(0, 0, w, h), BlendMode.Replace);
			}

			// Translucent popup in the center
			int pw = Math.Max(30, w / 2), ph = Math.Max(8, h / 2);
			int px = (w - pw) / 2,        py = (h - ph) / 2;
			using (var popup = new Paragraph("")
				       .AppendLine(" Popup Window ", new Style(fg: Colors.WHITE, bg: Colors.BLUE, bold: true))
				       .AppendLine(" ")
				       .AppendLine("This overlay is composed with alpha\nblending over the base content.", new Style(fg: Colors.LYELLOW))) {
				frame.Draw(popup, new Rect(px, py, pw, ph), BlendMode.Over, fgAlpha: 255, bgAlpha: 180);
			}

			frame.Present();
			return true; // keep running; ESC/Q handled by Rat.Run
		}, fps: 60);
	}
}