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
        double ratioH = 0.33;
        double ratioV = 0.75;

        return Rat.Run((frame, events) =>
        {
            foreach (var ev in events)
            {
                if (ev.Kind != EventKind.Key) continue;
                // Adjust ratios with Alt+Left/Right (H split) and Alt+Up/Down (V split)
                if (ev.Key.Alt)
                {
                    switch (ev.Key.CodeEnum)
                    {
                        case KeyCode.Left:  ratioH = Math.Max(0.1, ratioH - 0.05); break;
                        case KeyCode.Right: ratioH = Math.Min(0.9,  ratioH + 0.05); break;
                        case KeyCode.Up:    ratioV = Math.Min(0.9,  ratioV + 0.05); break;
                        case KeyCode.Down:  ratioV = Math.Max(0.1,  ratioV - 0.05); break;
                    }
                    root.Ratio  = ratioH;
                    root2.Ratio = ratioV;
                }
            }

            frame.Clear();
            var area = new Rect(0, 0, frame.Width, frame.Height);
            var title = new Paragraph("").AppendLine("Docking Demo", new Style(fg: Colors.LCYAN, bold: true));
            frame.Draw(title, new Rect(0,0,area.Width,1));
            var body = new Rect(0,1, area.Width, Math.Max(1, area.Height-2));

            // Smoke: textual description
            var p = new Paragraph("")
                .Title("dock tree", true).WithBlock(BlockAdv.Default)
                .AppendLine($"Left: Project Tree ({(int)(ratioH*100)}%) | Right: Editor")
                .AppendLine($"Bottom: Logs ({(int)((1-ratioV)*100)}%)", new Style(fg: Colors.GRAY));
            frame.Draw(p, body);

            var footer = new Paragraph("")
                .AppendLine("Alt+←/→ adjust H split • Alt+↑/↓ adjust V split", new Style(fg: Colors.GRAY));
            frame.Draw(footer, new Rect(0, area.Height-1, area.Width, 1));
            frame.Present();
            return true;
        }, fps: 30);
    }
}
