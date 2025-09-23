using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class FloatingScreensDemo : BaseDemo, IEmbeddedDemo
{
    public override string Name => "Floating Screens Demo";
    public override string Description => "Form with overlay (center anchored, percent size)";
    public override string[] Tags => ["floating", "overlay", "modal"];

    public override int Run()
    {
        // Smoke: render base form + overlay in first frame
        return Rat.Run(frame =>
        {
            frame.Clear();
            int  w = frame.Width, h = frame.Height;
            var  area = new Rect(0, 0, w, h);
            var  rows = Ui.Rows(area, new[] { Ui.U.Px(1), Ui.U.Flex(1), Ui.U.Px(1) });

            // Title
            frame.Draw(new Paragraph("").AppendLine("Floating Screens Demo", new Style(fg: Colors.LCYAN, bold: true)), rows[0]);

            // Base form content
            var form = new Paragraph("")
                .Title("Main Form", true).WithBlock(BlockAdv.Default)
                .AppendLine("")
                .AppendLine("Name: _____________")
                .AppendLine("Age:  __")
                .AppendLine("Country: __________")
                .AppendLine("")
                .AppendLine("Press F2 to toggle Help (TTY). In smoke mode, the overlay is shown by default.", new Style(fg: Colors.GRAY));
            frame.Draw(form, Ui.Pad(rows[1], 2,1,2,1));

            // Overlay (center 60% x 50%)
            var rectSpec = RectSpec.CenterPct(0.6, 0.5);
            var overlayRect = rectSpec.Compute(area);
            var overlay = new Paragraph("")
                .Title("Help", true).WithBlock(BlockAdv.Default)
                .AppendLine("This is a floating overlay.")
                .AppendLine("Anchored at center (0.5,0.5), sized in %.")
                .AppendLine("Use percent anchors/sizes for responsive UIs.", new Style(fg: Colors.GRAY));
            frame.Draw(overlay, overlayRect);

            // Footer
            var footer = new Paragraph("")
                .AppendLine("F2: toggle overlay   Esc: close overlay", new Style(fg: Colors.GRAY));
            frame.Draw(footer, rows[2]);

            frame.Present();
            return true;
        }, fps: 30);
    }

public Thaum.App.RatatuiTUI.Screen Create(Program.DemoTUI app)
    {
        return new FloatDemoScreen(app);
    }

private sealed class FloatDemoScreen : Thaum.App.RatatuiTUI.Screen<Program.DemoTUI>
    {
        public FloatDemoScreen(Program.DemoTUI tui) : base(tui) { }
        public override void Draw(Terminal term, Rect area)
        {
            var rows = Ui.Rows(area, new[] { Ui.U.Px(1), Ui.U.Flex(1), Ui.U.Px(1) });
            term.Draw(term.NewParagraph("").AppendLine("Floating Screens Demo", new Style(fg: Colors.LCYAN, bold: true)), rows[0]);
            var form = term.NewParagraph("").Title("Main Form", true).WithBlock(BlockAdv.Default)
                .AppendLine("")
                .AppendLine("Name: _____________")
                .AppendLine("Age:  __")
                .AppendLine("Country: __________")
                .AppendLine("")
                .AppendLine("Press F2 to toggle Help; Esc closes overlays.", new Style(fg: Colors.GRAY));
            term.Draw(form, Ui.Pad(rows[1],2,1,2,1));
            var overlayRect = new RectSpec(AnchorSpec.Center, SizeSpec.Pct(0.6,0.5), Padding.All(1)).Compute(area);
            var overlay = term.NewParagraph("").Title("Help", true).WithBlock(BlockAdv.Default)
                .AppendLine("This is a floating overlay.")
                .AppendLine("Anchored at center (0.5,0.5), sized in %.")
                .AppendLine("Use percent anchors/sizes for responsive UIs.", new Style(fg: Colors.GRAY));
            term.Draw(overlay, overlayRect);
            Chrome.StatusHelp(term, rows[2], new [] { new KeyBinding("F2","Toggle Help"), new KeyBinding("Esc","Close") });
        }
    }
}
