using Ratatui;
using Ratatui.Sugar;
using Thaum.App.RatatuiTUI;

namespace Ratatui.Demo.Demos;

public class ModalDemo : BaseDemo
{
    public override string Name => "Modal Demo";
    public override string Description => "Centered modal with title and content";
    public override string[] Tags => ["modal", "dialog", "overlay"];

    public override int Run()
    {
        return Rat.Run(frame =>
        {
            frame.Clear();
            int w = frame.Width, h = frame.Height;
            var area = new Rect(0, 0, w, h);

            // Backdrop title
            var title = new Paragraph("").AppendLine("Modal Demo", new Style(fg: Colors.LCYAN, bold: true));
            frame.Draw(title, new Rect(0, 0, w, 1));

            // Center modal
            int mw = Math.Max(30, w/2);
            int mh = Math.Max(8,  h/3);
            var modalRect = new Rect(area.X + (w - mw)/2, area.Y + (h - mh)/2, mw, mh);
            var modal = new Paragraph("").Title("Confirm", true).WithBlock(BlockAdv.Default)
                .AppendLine("Are you sure you want to continue?", new Style(fg: Colors.WHITE))
                .AppendLine("")
                .AppendLine("[ OK ]   [ Cancel ]", new Style(fg: Colors.GRAY));
            frame.Draw(modal, modalRect);

            frame.Present();
            return true;
        }, fps: 30);
    }
}
