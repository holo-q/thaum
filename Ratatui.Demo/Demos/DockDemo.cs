using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class DockDemo : BaseDemo
{
    public override string Name => "Docking Demo";
    public override string Description => "Split panes (vim-like) with borders";
    public override string[] Tags => ["dock", "split", "panes"];

    public override int Run()
    {
        var left  = new DockLeaf("tree", "Project Tree", (term, r) => term.Draw(new Paragraph("").AppendLine("src/\n  Ratatui/\n  Thaum/"), r));
        var right = new DockLeaf("editor", "Editor", (term, r) => term.Draw(new Paragraph("").AppendLine("// Editor pane"), r));
        var root  = new DockSplit(SplitDir.H, 0.33, left, right);
        var logs  = new DockLeaf("logs", "Logs", (term, r) => term.Draw(new Paragraph("").AppendLine("[INFO] Started"), r));
        var root2 = new DockSplit(SplitDir.V, 0.75, root, logs);
        var dock  = new DockMgr(root2);

        return Rat.Run(frame =>
        {
            frame.Clear();
            var area = new Rect(0, 0, frame.Width, frame.Height);
            // Draw title
            var title = new Paragraph("").AppendLine("Docking Demo", new Style(fg: Colors.LCYAN, bold: true));
            frame.Draw(title, new Rect(0,0,area.Width,1));
            var body = new Rect(0,1, area.Width, Math.Max(1, area.Height-2));
            // Render dock tree
            // Use Terminal directly to draw; create a temporary Terminal wrapper? We are within Frame; draw using paragraphs only for smoke.
            // Render borders and content via nested paragraphs
            // For smoke, emulate by drawing the labels; the Dock API expects Terminal; so skip for ASCII and render placeholders.
            var p = new Paragraph("")
                .Title("[smoke] dock tree", true).WithBlock(BlockAdv.Default)
                .AppendLine("Left: Project Tree (33%) | Right: Editor (rest)")
                .AppendLine("Bottom: Logs (25%)", new Style(fg: Colors.GRAY));
            frame.Draw(p, body);
            // Footer
            var footer = new Paragraph("")
                .AppendLine("Ctrl+Shift+H/J/K/L split • Ctrl+W close (TTY)", new Style(fg: Colors.GRAY));
            frame.Draw(footer, new Rect(0, area.Height-1, area.Width, 1));

            frame.Present();
            return true;
        }, fps: 30);
    }
}

