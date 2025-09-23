using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class LayoutDemo : BaseDemo
{
    public override string Name => "Layout Demo";
    public override string Description => "Rows/Cols/Splits/Grid using Ui helpers";
    public override string[] Tags => ["layout", "rows", "cols", "grid"];

    public override int Run()
    {
        return Rat.Run(frame =>
        {
            frame.Clear();
            var area = new Rect(0, 0, frame.Width, frame.Height);

            var header = new Paragraph("")
                .AppendLine("Layout Demo", new Style(fg: Colors.LCYAN, bold: true))
                .AppendLine("")
                .AppendLine("Ui.Rows / Ui.Cols / Ui.Split / Ui.Grid", new Style(fg: Colors.GRAY));
            frame.Draw(header, Ui.Pad(area, 2, 1, 2, Math.Max(0, frame.Height - 3)));

            var body = new Rect(0, 3, frame.Width, Math.Max(0, frame.Height - 4));
            var cols = Ui.Cols(body, new[] { Ui.U.Flex(1), Ui.U.Flex(1) }, gap: 1);

            var leftRows  = Ui.Rows(cols[0], new[] { Ui.U.Px(cols[0].Height/3), Ui.U.Flex(1) }, gap: 1);
            var rightRows = Ui.Rows(cols[1], new[] { Ui.U.Px(cols[1].Height/3), Ui.U.Flex(1) }, gap: 1);

            // Left top: Vertical split
            var (LT, LB) = Ui.SplitV(leftRows[0], 1, 2, gap: 1);
            var paraLT = new Paragraph("").Title("Top (1/3)", true).WithBlock(BlockAdv.Default);
            var paraLB = new Paragraph("").Title("Bottom (2/3)", true).WithBlock(BlockAdv.Default);
            frame.Draw(paraLT, LT);
            frame.Draw(paraLB, LB);

            // Left bottom: Grid
            var grid = Ui.Grid(leftRows[1], cols: 3, rows: 2, gapX: 1, gapY: 1);
            for (int i = 0; i < grid.Length; i++)
            {
                var p = new Paragraph("").Title($"Cell {i+1}", true).WithBlock(BlockAdv.Default);
                frame.Draw(p, grid[i]);
            }

            // Right: Horizontal split
            var (RLeft, RRight) = Ui.SplitH(rightRows[0], 2, 1, gap: 1);
            var pRL = new Paragraph("").Title("Left (2/3)", true).WithBlock(BlockAdv.Default);
            var pRR = new Paragraph("").Title("Right (1/3)", true).WithBlock(BlockAdv.Default);
            frame.Draw(pRL, RLeft);
            frame.Draw(pRR, RRight);

            // Right bottom: Free paragraph
            var info = new Paragraph("")
                .AppendLine("This demo uses only Paragraphs with borders")
                .AppendLine("to visualize Ui layout splits and grids.", new Style(fg: Colors.GRAY));
            frame.Draw(info, Ui.Pad(rightRows[1], 1, 1, 1, 1));

            frame.Present();
            return true;
        }, fps: 30);
    }
}

