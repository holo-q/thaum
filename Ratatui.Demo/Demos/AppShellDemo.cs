using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class AppShellDemo : BaseDemo
{
    public override string Name => "AppShell Demo";
    public override string Description => "Pager toolbar + menu + status help";
    public override string[] Tags => ["shell", "pager", "menu", "status"];

    public override int Run()
    {
        int page = 0;
        string[] pages = ["Home", "Browse", "Inspect", "Settings"]; 
        return Rat.Run((frame, events) =>
        {
            foreach (var ev in events)
            {
                if (ev.Kind == EventKind.Key && ev.Key.Alt)
                {
                    if (ev.Key.CodeEnum == KeyCode.Left)  page = (page - 1 + pages.Length) % pages.Length;
                    if (ev.Key.CodeEnum == KeyCode.Right) page = (page + 1) % pages.Length;
                }
            }

            frame.Clear();
            int w = frame.Width, h = frame.Height;
            var area = new Rect(0,0,w,h);
            var rows = Ui.Rows(area, new[] { Ui.U.Px(1), Ui.U.Px(1), Ui.U.Flex(1), Ui.U.Px(1) });

            // Menu Bar
            Chrome.MenuBar(new Terminal(), rows[0], new List<string>{"File","Edit","View","Help"}, selected: 0);
            // Pager toolbar
            Chrome.Pager(new Terminal(), rows[1], pages.Length, page);

            // Body
            var body = new Paragraph("")
                .Title(pages[page], true).WithBlock(BlockAdv.Default)
                .AppendLine($"This is the {pages[page]} page.")
                .AppendLine("Use Alt+Left/Alt+Right to switch pages.", new Style(fg: Colors.GRAY));
            frame.Draw(body, Ui.Pad(rows[2], 2,1,2,1));

            // Status help
            string left = "Alt+← Prev   Alt+→ Next";
            string right = "AppShell Demo";
            int spaces = Math.Max(0, w - left.Length - right.Length);
            var status = new Paragraph("")
                .AppendLine(left + new string(' ', spaces) + right, new Style(fg: Colors.GRAY));
            frame.Draw(status, rows[3]);

            frame.Present();
            return true;
        }, fps: 30);
    }
}

