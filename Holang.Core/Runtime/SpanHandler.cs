using System;
using System.Collections.Generic;

namespace Holang.Core.Runtime;

public static class SpanHandler {
    public static void Handle(Holophore phore, Span span) {
        switch (span) {
            case TextSpan t: TextSpan(phore, t); break;
            case ObjSpan o: ObjSpan(phore, o); break;
            case SampleSpan s: SampleSpan(phore, s); break;
            case ContextResetSpan c: ContextResetSpan(phore, c); break;
            case EgoSpan e: EgoSpan(phore, e); break;
            case ClassSpan c: ClassSpan(phore, c); break;
            default: throw new NotSupportedException($"No handler for span {span.GetType().Name}");
        }
    }

    public static void TextSpan(Holophore phore, TextSpan span) {
        phore.AddMasked(span.Text);
    }

    public static void ObjSpan(Holophore phore, ObjSpan span) {
        foreach (var varId in span.VarIds) {
            if (phore.Env.TryGetValue(varId, out var value) && value is not null) {
                phore.AddMasked($"<obj id={varId}>");
                phore.AddMasked(value.ToString() ?? string.Empty);
                phore.AddMasked("</obj>\n");
            }
        }
    }

    public static void SampleSpan(Holophore phore, SampleSpan span) {
        if (!string.IsNullOrEmpty(span.Fence)) phore.AddMasked($"<{span.Fence}>");

        var stops = new List<string>();
        if (!string.IsNullOrEmpty(span.Fence)) stops.Add($"</{span.Fence}>");

        var sample = phore.Sample(stops) ?? string.Empty;
        string text;
        if (!string.IsNullOrEmpty(span.Fence)) {
            var closing = $"</{span.Fence}>";
            text = sample.EndsWith(closing, StringComparison.Ordinal) ? sample : sample + closing;
        } else text = sample;

        phore.AddReinforced(text);

        if (!string.IsNullOrEmpty(span.Id)) {
            var payload = sample;
            if (!string.IsNullOrEmpty(span.Fence)) {
                var open = $"<{span.Fence}>";
                var close = $"</{span.Fence}>";
                if (payload.StartsWith(open, StringComparison.Ordinal) && payload.EndsWith(close, StringComparison.Ordinal))
                    payload = payload.Substring(open.Length, payload.Length - open.Length - close.Length);
                else if (payload.EndsWith(close, StringComparison.Ordinal))
                    payload = payload.Substring(0, payload.Length - close.Length);
                if (payload.StartsWith(open, StringComparison.Ordinal))
                    payload = payload.Substring(open.Length);
            }
            phore.Env[span.Id] = payload.Trim();
        }
    }

    public static void ContextResetSpan(Holophore phore, ContextResetSpan span) {
        phore.NewContext();
        phore.SetEgo("system");
    }

    public static void EgoSpan(Holophore phore, EgoSpan span) {
        phore.SetEgo(span.Ego);
    }

    public static void ClassSpan(Holophore phore, ClassSpan span) {
        var cls = phore.GetClass(span.ClassName);
        if (cls is null) throw new Exception($"Class '{span.ClassName}' not found");

        if (!phore.SpanBindings.TryGetValue(span.Uuid, out var binding) || binding is null) {
            // Instantiate (best-effort parameterless) or use Type for static (__holo__) calls
            if (cls is Type t) {
                var inst = phore.Invoke(t, "__init__", span.KArgs, span.KwArgs, optional: true) as object ??
                           (t.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(t) : t);
                phore.SpanBindings[span.Uuid] = inst;
                binding = inst;
            } else {
                phore.SpanBindings[span.Uuid] = cls; binding = cls;
            }
        }

        // If binding has __holo__, invoke; else if body exists, execute body
        var prevSpan = phore.FindSpan(span.Uuid);
        if (binding is not null) {
            var args = phore.GetHolofuncArgs(prevSpan);
            var result = phore.Invoke(binding, "__holo__", args.Item1, args.Item2, optional: true);
            if (result is string s && s.Length > 0) phore.AddMasked(s);
            else if (span.Body is not null) span.Body.Invoke(phore);
        } else if (span.Body is not null) {
            span.Body.Invoke(phore);
        } else {
            throw new Exception($"Nothing to do for class span {span.ClassName}");
        }
    }
}

