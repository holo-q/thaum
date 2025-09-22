using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class AnimationDemo : BaseDemo {
    public override string Name => "Animation Demo";
    public override string Description => "Shows animated elements with rotating colors and moving text";
    public override string[] Tags => ["animation", "colors", "movement"];

    private int _frameCount = 0;

    public override int Run() {
        _frameCount = 0;
        return Rat.Run(frame => {
            frame.Clear();
            int w = frame.Width, h = frame.Height;
            _frameCount++;

            // Animated rainbow colors
            var colors = new[] { Colors.Red, Colors.Yellow, Colors.Green, Colors.Cyan, Colors.Blue, Colors.Magenta };
            var currentColor = colors[(_frameCount / 10) % colors.Length];

            // Bouncing position
            int bounceX = (int)(Math.Sin(_frameCount * 0.1) * 10) + w / 2 - 10;
            int bounceY = (int)(Math.Abs(Math.Sin(_frameCount * 0.15)) * 5) + h / 2;

            using (var para = new Paragraph("")
                     .AppendLine("Animation Demo", new Style(fg: currentColor, bold: true))
                     .AppendLine("")
                     .AppendLine($"Frame: {_frameCount}", new Style(fg: Colors.Gray))
                     .AppendLine("")
                     .AppendLine("Watch the colors cycle through the rainbow!", new Style(fg: currentColor))
                     .AppendLine("")
                     .AppendLine("■ ■ ■ ■ ■", new Style(fg: colors[(_frameCount / 5) % colors.Length]))
                     .AppendLine("■ ■ ■ ■ ■", new Style(fg: colors[(_frameCount / 5 + 1) % colors.Length]))
                     .AppendLine("■ ■ ■ ■ ■", new Style(fg: colors[(_frameCount / 5 + 2) % colors.Length]))
                     .AppendLine("")
                     .AppendLine("Press Q/Esc to exit", new Style(fg: Colors.Yellow))) {
                frame.Draw(para, new Rect(2, 2, w - 4, h - 4), BlendMode.Replace);
            }

            // Bouncing text
            if (bounceX >= 0 && bounceX < w - 20 && bounceY >= 0 && bounceY < h - 1) {
                using (var bouncing = new Paragraph("")
                         .AppendLine("● BOUNCING! ●", new Style(fg: Colors.LightYellow, bold: true))) {
                    frame.Draw(bouncing, new Rect(bounceX, bounceY, 20, 1), BlendMode.Over);
                }
            }

            frame.Present();
            return true;
        }, fps: 15);
    }
}
