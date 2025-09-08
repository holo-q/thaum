using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Holang.Core.Runtime;

public sealed class Holoware {
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? FilePath { get; set; }
    public List<Span> Spans { get; } = new();

    public IEnumerable<string> ObjIds => Spans.OfType<ObjSpan>().SelectMany(s => s.VarIds);

    public Holoware Invoke(Holophore phore) {
        phore.PushHoloware(this);

        // Lifecycle start
        phore.Invoke(this, "__holo_start__", new List<object?> { phore }, new Dictionary<string, object?>(), optional: true);

        // Bind class instances
        foreach (var span in Spans) {
            if (span is ClassSpan cs) {
                var cls = phore.GetClass(cs.ClassName);
                if (cls is null) throw new Exception($"Class '{cs.ClassName}' not found in environment or registry.");
                object? inst = cls;
                if (cls is Type t) {
                    var args = phore.GetHolofuncArgs(cs);
                    var kargs = cs.KArgs.Cast<object?>().ToList();
                    var kwargs = cs.KwArgs.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
                    inst = phore.Invoke(t, "__init__", kargs, kwargs, optional: true) as object ??
                           (t.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(t) : t);
                    var after = phore.Invoke(inst!, "__holo_init__", args.Item1, args.Item2, optional: true);
                    inst = after ?? inst;
                }
                phore.SpanBindings[cs.Uuid] = inst;
            }
        }

        // Main execution
        for (var i = 0; i < Spans.Count; i++) {
            var span = Spans[i];
            phore.SetSpan(span);
            SpanHandler.Handle(phore, span);
            phore.SetSpan(null);
        }

        // Lifecycle end
        foreach (var kv in phore.SpanBindings) {
            var span = Spans.FirstOrDefault(s => s.Uuid == kv.Key);
            if (span is not null) {
                var args = phore.GetHolofuncArgs(span);
                phore.Invoke(kv.Value!, "__holo_end__", args.Item1, args.Item2, optional: true);
            }
        }

        phore.PopHoloware(this);
        return phore is null ? this : this; // keep signature parity
    }

    public static Holoware Parse(string content) => new Parsing.HolowareParser(content).Parse();

    public static Holoware Load(string filepath) {
        var text = File.ReadAllText(filepath);
        var hw = Parse(text);
        hw.Name = Path.GetFileName(filepath);
        hw.FilePath = filepath;
        return hw;
    }
}
