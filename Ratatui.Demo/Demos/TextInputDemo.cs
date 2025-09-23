using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class TextInputDemo : BaseDemo
{
    public override string Name => "TextInput Demo";
    public override string Description => "Single-line text input (visual only in smoke mode)";
    public override string[] Tags => ["textinput", "focus", "search"];

    public override int Run()
    {
        return Rat.Run(frame =>
        {
            frame.Clear();
            int w = frame.Width, h = frame.Height;
            var area = new Rect(0, 0, w, h);

            var rows = Thaum.App.RatatuiTUI.Ui.Rows(area, new[] { Thaum.App.RatatuiTUI.Ui.U.Px(2), Thaum.App.RatatuiTUI.Ui.U.Px(3), Thaum.App.RatatuiTUI.Ui.U.Flex(1) });
            var title = new Paragraph("").AppendLine("TextInput Demo", new Style(fg: Colors.LCYAN, bold: true));
            frame.Draw(title, rows[0]);

            // Visual-only input field
            string placeholder = "Type to search…";
            string caret = "█";
            var field = new Paragraph("")
                .AppendLine(placeholder + caret, new Style(fg: Colors.WHITE))
                .WithBlock(new Ratatui.BlockAdv(Ratatui.Borders.All, Ratatui.BorderType.Plain, new Ratatui.Padding(1,0,1,0), Ratatui.Alignment.Left));
            frame.Draw(field, new Rect(2, rows[1].Y, Math.Max(20, w - 4), rows[1].Height));

            var help = new Paragraph("")
                .AppendLine("In interactive mode, keys update the input and trigger filtering.", new Style(fg: Colors.GRAY));
            frame.Draw(help, rows[2]);

            frame.Present();
            return true;
        }, fps: 30);
    }
}
