using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class ColorsDemo : BaseDemo {
    public override string Name => "Colors Demo";
    public override string Description => "Showcases all available colors and their combinations";
    public override string[] Tags => ["colors", "styling", "palette"];

    public override int Run() {
        return Rat.Run(frame => {
            frame.Clear();
            int w = frame.Width, h = frame.Height;

            using (var para = new Paragraph("")
                     .AppendLine("Color Palette Demo", new Style(fg: Colors.White, bold: true))
                     .AppendLine("")
                     .AppendLine("Basic Colors:", new Style(fg: Colors.Yellow))
                     .AppendLine("■ Black", new Style(fg: Colors.Black))
                     .AppendLine("■ Red", new Style(fg: Colors.Red))
                     .AppendLine("■ Green", new Style(fg: Colors.Green))
                     .AppendLine("■ Yellow", new Style(fg: Colors.Yellow))
                     .AppendLine("■ Blue", new Style(fg: Colors.Blue))
                     .AppendLine("■ Magenta", new Style(fg: Colors.Magenta))
                     .AppendLine("■ Cyan", new Style(fg: Colors.Cyan))
                     .AppendLine("■ Gray", new Style(fg: Colors.Gray))
                     .AppendLine("")
                     .AppendLine("Light Colors:", new Style(fg: Colors.Yellow))
                     .AppendLine("■ DarkGray", new Style(fg: Colors.DarkGray))
                     .AppendLine("■ LightRed", new Style(fg: Colors.LightRed))
                     .AppendLine("■ LightGreen", new Style(fg: Colors.LightGreen))
                     .AppendLine("■ LightYellow", new Style(fg: Colors.LightYellow))
                     .AppendLine("■ LightBlue", new Style(fg: Colors.LightBlue))
                     .AppendLine("■ LightMagenta", new Style(fg: Colors.LightMagenta))
                     .AppendLine("■ LightCyan", new Style(fg: Colors.LightCyan))
                     .AppendLine("■ White", new Style(fg: Colors.White))
                     .AppendLine("")
                     .AppendLine("Background Examples:", new Style(fg: Colors.Yellow))
                     .AppendLine(" Red BG ", new Style(bg: Colors.Red, fg: Colors.White))
                     .AppendLine(" Blue BG ", new Style(bg: Colors.Blue, fg: Colors.White))
                     .AppendLine(" Green BG ", new Style(bg: Colors.Green, fg: Colors.Black))
                     .AppendLine("")
                     .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.Yellow))) {
                frame.Draw(para, new Rect(2, 2, w - 4, h - 4), BlendMode.Replace);
            }

            frame.Present();
            return true;
        }, fps: 30);
    }
}