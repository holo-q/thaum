using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class BordersDemo : BaseDemo {
	public override string   Name        => "Borders Demo";
	public override string   Description => "Demonstrates different border styles and layouts";
	public override string[] Tags        => ["borders", "layout", "blocks"];

	public override int Run() {
		return Rat.Run(frame => {
			frame.Clear();
			int w = frame.Width, h = frame.Height;

			// Main content
			using (var main = new Paragraph("")
				       .AppendLine("Borders & Blocks Demo", new Style(fg: Colors.CYAN, bold: true))
				       .AppendLine("")
				       .AppendLine("This demonstrates various border styles", new Style(fg: Colors.WHITE))
				       .AppendLine("and block layouts available in Ratatui.cs", new Style(fg: Colors.WHITE))
				       .AppendLine("")
				       .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.YELLOW))) {
				frame.Draw(main, new Rect(2, 2, w / 2 - 4, h - 4), BlendMode.Replace);
			}

			// Different sections with different content
			using (var section1 = new Paragraph("")
				       .AppendLine("Section 1", new Style(fg: Colors.LIGHTGREEN, bold: true))
				       .AppendLine("Content here")
				       .AppendLine("More text")) {
				frame.Draw(section1, new Rect(w / 2 + 2, 2, w / 2 - 4, h / 3 - 2), BlendMode.Replace);
			}

			using (var section2 = new Paragraph("")
				       .AppendLine("Section 2", new Style(fg: Colors.LBLUE, bold: true))
				       .AppendLine("Different content")
				       .AppendLine("And more...")) {
				frame.Draw(section2, new Rect(w / 2 + 2, h / 3 + 1, w / 2 - 4, h / 3 - 2), BlendMode.Replace);
			}

			using (var section3 = new Paragraph("")
				       .AppendLine("Section 3", new Style(fg: Colors.LMAGENTA, bold: true))
				       .AppendLine("Final section")
				       .AppendLine("Last content")) {
				frame.Draw(section3, new Rect(w / 2 + 2, 2 * h / 3 + 1, w / 2 - 4, h / 3 - 2), BlendMode.Replace);
			}

			frame.Present();
			return true;
		}, fps: 30);
	}
}