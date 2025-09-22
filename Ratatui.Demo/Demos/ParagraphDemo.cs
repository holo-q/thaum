using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class ParagraphDemo : BaseDemo {
    public override string Name => "Paragraph Demo";
    public override string Description => "Demonstrates text rendering with different styles and formatting";
    public override string[] Tags => ["text", "paragraph", "styling", "basic"];

    public override int Run() {
        return Rat.Run(frame => {
            frame.Clear();
            int w = frame.Width, h = frame.Height;

            using (var para = new Paragraph("")
                     .AppendLine("Paragraph Demo", new Style(fg: Colors.Cyan, bold: true))
                     .AppendLine("")
                     .AppendLine("This demonstrates basic text rendering with various styles:")
                     .AppendLine("")
                     .AppendLine("• Bold text", new Style(bold: true))
                     .AppendLine("• Italic text", new Style(italic: true))
                     .AppendLine("• Underlined text", new Style(underline: true))
                     .AppendLine("• Colored text", new Style(fg: Colors.Green))
                     .AppendLine("• Background color", new Style(bg: Colors.Blue, fg: Colors.White))
                     .AppendLine("")
                     .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.Yellow))) {
                frame.Draw(para, new Rect(2, 2, w - 4, h - 4), BlendMode.Replace);
            }

            frame.Present();
            return true;
        }, fps: 30);
    }
}