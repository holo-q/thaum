using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class ControlsDemo : BaseDemo
{
    public override string Name => "Controls Demo";
    public override string Description => "Buttons, toggles, modal, status (paragraph-based)";
    public override string[] Tags => ["controls", "button", "toggle", "modal", "status"];

    public override int Run()
    {
        return Rat.Run(frame =>
        {
            frame.Clear();
            int w = frame.Width, h = frame.Height;
            var area = new Rect(0, 0, w, h);

            var rows = Ui.Rows(area, new[] { Ui.U.Px(1), Ui.U.Flex(1), Ui.U.Px(1) });

            var title = new Paragraph("").AppendLine("Controls Demo", new Style(fg: Colors.LCYAN, bold: true));
            frame.Draw(title, rows[0]);

            var cols = Ui.Cols(rows[1], new[] { Ui.U.Flex(1), Ui.U.Flex(1) }, gap: 2);

            // Left column: buttons
            var left = Ui.Rows(cols[0], new[] { Ui.U.Px(3), Ui.U.Px(3), Ui.U.Px(3), Ui.U.Flex(1) }, gap: 1);
            frame.Draw(new Paragraph("").Title("Buttons", true).WithBlock(BlockAdv.Default), left[0]);
            frame.Draw(new Paragraph("").AppendLine("[ OK ]", new Style(fg: Colors.BLACK, bg: Colors.LIGHTGREEN, bold: true)).WithBlock(BlockAdv.Default), left[1]);
            frame.Draw(new Paragraph("").AppendLine("[ Cancel ]", new Style(fg: Colors.WHITE, bg: Colors.DGRAY)).WithBlock(BlockAdv.Default), left[2]);

            // Right column: toggles + modal
            var right = Ui.Rows(cols[1], new[] { Ui.U.Px(3), Ui.U.Px(3), Ui.U.Flex(1) }, gap: 1);
            frame.Draw(new Paragraph("").Title("Toggles", true).WithBlock(BlockAdv.Default), right[0]);
            frame.Draw(new Paragraph("").AppendLine("[x] Enable Feature", new Style(fg: Colors.WHITE)).WithBlock(BlockAdv.Default), right[1]);

            // Modal preview (centered-ish)
            int mw = Math.Max(20, w/3);
            int mh = 7;
            var modalRect = new Rect(area.X + (w - mw)/2, area.Y + (h - mh)/2, mw, mh);
            var modal = new Paragraph("").Title("Modal", true).WithBlock(BlockAdv.Default)
                .AppendLine("Press Enter to accept", new Style(fg: Colors.GRAY));
            frame.Draw(modal, modalRect);

            // Status bar
            string leftText = "F1 Help  |  Tab Next";
            string rightText = "v0.1";
            int spaces = Math.Max(0, w - leftText.Length - rightText.Length);
            var status = new Paragraph("").AppendLine(leftText + new string(' ', spaces) + rightText, new Style(fg: Colors.GRAY));
            frame.Draw(status, rows[2]);

            frame.Present();
            return true;
        }, fps: 30);
    }
}

