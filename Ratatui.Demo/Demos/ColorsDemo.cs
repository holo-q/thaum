using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class ColorsDemo : BaseDemo {
	public override string   Name        => "Colors Demo";
	public override string   Description => "Showcases all available colors and their combinations";
	public override string[] Tags        => ["colors", "styling", "palette"];

	public override int Run() {
		return Rat.Run(frame => {
			frame.Clear();
			int w = frame.Width, h = frame.Height;

			using (var para = new Paragraph("")
				       .AppendLine("Color Palette Demo", new Style(fg: Colors.WHITE, bold: true))
				       .AppendLine("")
				       .AppendLine("Basic Colors:", new Style(fg: Colors.YELLOW))
				       .AppendLine("■ Black", new Style(fg: Colors.BLACK))
				       .AppendLine("■ Red", new Style(fg: Colors.RED))
				       .AppendLine("■ Green", new Style(fg: Colors.GREEN))
				       .AppendLine("■ Yellow", new Style(fg: Colors.YELLOW))
				       .AppendLine("■ Blue", new Style(fg: Colors.BLUE))
				       .AppendLine("■ Magenta", new Style(fg: Colors.MAGENTA))
				       .AppendLine("■ Cyan", new Style(fg: Colors.CYAN))
				       .AppendLine("■ Gray", new Style(fg: Colors.GRAY))
				       .AppendLine("")
				       .AppendLine("Light Colors:", new Style(fg: Colors.YELLOW))
				       .AppendLine("■ DarkGray", new Style(fg: Colors.DGRAY))
				       .AppendLine("■ LightRed", new Style(fg: Colors.LIGHTRED))
				       .AppendLine("■ LightGreen", new Style(fg: Colors.LIGHTGREEN))
				       .AppendLine("■ LightYellow", new Style(fg: Colors.LYELLOW))
				       .AppendLine("■ LightBlue", new Style(fg: Colors.LBLUE))
				       .AppendLine("■ LightMagenta", new Style(fg: Colors.LMAGENTA))
				       .AppendLine("■ LightCyan", new Style(fg: Colors.LCYAN))
				       .AppendLine("■ White", new Style(fg: Colors.WHITE))
				       .AppendLine("")
				       .AppendLine("Background Examples:", new Style(fg: Colors.YELLOW))
				       .AppendLine(" Red BG ", new Style(bg: Colors.RED, fg: Colors.WHITE))
				       .AppendLine(" Blue BG ", new Style(bg: Colors.BLUE, fg: Colors.WHITE))
				       .AppendLine(" Green BG ", new Style(bg: Colors.GREEN, fg: Colors.BLACK))
				       .AppendLine("")
				       .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.YELLOW))) {
				frame.Draw(para, new Rect(2, 2, w - 4, h - 4), BlendMode.Replace);
			}

			frame.Present();
			return true;
		}, fps: 30);
	}
}