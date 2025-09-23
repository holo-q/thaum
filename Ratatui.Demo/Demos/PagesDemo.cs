using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class PagesDemo : BaseDemo
{
    public override string Name => "Pages Demo";
    public override string Description => "Multi-page demo with pager toolbar (Alt+Left/Right)";
    public override string[] Tags => ["pages", "pager", "toolbar"];

    public override int Run()
    {
        int page = 0;
        const int pageCount = 5;
        return Rat.Run((frame, events) =>
        {
            // Handle navigation in TTY mode (no events in ASCII smoke)
            foreach (var ev in events)
            {
                if (ev.Kind == EventKind.Key && ev.Key.Alt)
                {
                    switch (ev.Key.CodeEnum)
                    {
                        case KeyCode.Left:
                            page = (page - 1 + pageCount) % pageCount;
                            break;
                        case KeyCode.Right:
                            page = (page + 1) % pageCount;
                            break;
                    }
                }
            }

            frame.Clear();
            int w = frame.Width, h = frame.Height;
            var area = new Rect(0, 0, w, h);
            var rows = Ui.Rows(area, new[] { Ui.U.Px(1), Ui.U.Px(1), Ui.U.Flex(1), Ui.U.Px(1) });

            // Menu/pager toolbar
            Chrome.Pager(new Terminal(), rows[0], pageCount, page);
            // Title row
            var title = new Paragraph("").AppendLine($"Page {page + 1} / {pageCount}", new Style(fg: Colors.LCYAN, bold: true));
            frame.Draw(title, rows[1]);

            // Body content
            var body = new Paragraph("")
                .Title("Content", true).WithBlock(BlockAdv.Default)
                .AppendLine("")
                .AppendLine("Use Alt+Left/Alt+Right to switch pages.", new Style(fg: Colors.GRAY))
                .AppendLine("In smoke mode (--ascii), this shows the first page only.", new Style(fg: Colors.GRAY));
            frame.Draw(body, Ui.Pad(rows[2], 2, 1, 2, 1));

            // Status help
            string left = "Alt+← Prev   Alt+→ Next";
            string right = "Pages Demo";
            int spaces = Math.Max(0, w - left.Length - right.Length);
            var status = new Paragraph("").AppendLine(left + new string(' ', spaces) + right, new Style(fg: Colors.GRAY));
            frame.Draw(status, rows[3]);

            frame.Present();
            return true;
        }, fps: 30);
    }
}

