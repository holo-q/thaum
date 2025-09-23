using Thaum.App.RatatuiTUI;
using Ratatui;

namespace Ratatui.Demo.Demos;

public class InspectorDemo : BaseDemo
{
    public override string Name => "Inspector Demo";
    public override string Description => "Runtime reflection inspector with attributes";
    public override string[] Tags => ["inspector", "attributes", "drawers"];

    private sealed class Sample
    {
        [Display(Name = "ID")]
        public int Id { get; set; } = 42;

        [Display(Name = "Title")]
        public string Title { get; set; } = "Example Object";

        [ReadOnly]
        public bool IsEnabled { get; set; } = true;

        [Range(0, 100, 0.5)]
        public double Progress { get; set; } = 73.2;

        [Multiline]
        public string Notes { get; set; } = "This is a multi-line\nexample of notes.";

        [HideInInspector]
        public string Secret { get; set; } = "don't show";
    }

    public override int Run()
    {
        return Rat.Run(frame =>
        {
            frame.Clear();
            int w = frame.Width, h = frame.Height;
            var obj = new Sample();

            var p = new Paragraph("")
                .Title("Inspector Demo", true)
                .AppendLine("")
                .AppendLine($"ID: {obj.Id}")
                .AppendLine($"Title: {obj.Title}")
                .AppendLine($"IsEnabled: {obj.IsEnabled}")
                .AppendLine($"Progress: {obj.Progress} [Range: 0..100 step 0.5]")
                .AppendLine("Notes:")
                .AppendLine(obj.Notes, new Style(fg: Colors.GRAY));
            frame.Draw(p, new Rect(2, 1, Math.Max(20, w - 4), Math.Max(6, h - 2)));
            frame.Present();
            return true;
        }, fps: 30);
    }
}
