using System;
using System.Collections.Generic;

namespace Holang.Core.Runtime;

public abstract class Span {
    public string Uuid { get; init; } = Guid.NewGuid().ToString();
    public string Id { get; set; } = string.Empty;                // human-assigned identifier
    public List<string> KArgs { get; set; } = new();              // positional args
    public Dictionary<string, string> KwArgs { get; set; } = new(); // keyword args

    public virtual Span WithArgs(List<string> k, Dictionary<string, string> kw, Dictionary<string, string> kwInject) {
        KArgs = new List<string>(k);
        // copy known fields (none generic here) then stash remaining
        var remaining = new Dictionary<string, string>();
        foreach (var kv in kw) remaining[kv.Key] = kv.Value;
        foreach (var kv in kwInject) remaining[kv.Key] = kv.Value;
        KwArgs = remaining;
        return this;
    }
}

public sealed class ContextResetSpan : Span {
    public bool Train { get; set; }
    public ContextResetSpan(bool train) { Train = train; }
}

public sealed class EgoSpan : Span {
    public string Ego { get; set; } = string.Empty; // system|user|assistant
}

public sealed class SampleSpan : Span {
    public string Fence { get; set; } = string.Empty; // e.g. think/json
}

public sealed class TextSpan : Span {
    public string Text { get; set; } = string.Empty;
}

public sealed class ObjSpan : Span {
    public List<string> VarIds { get; set; } = new();
}

public sealed class ClassSpan : Span {
    public string ClassName { get; set; } = string.Empty;
    public Holoware? Body { get; set; }
}

