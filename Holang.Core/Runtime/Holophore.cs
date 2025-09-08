using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Holang.Core.Runtime;

public interface IHolangSampler {
    string? Sample(Rollout rollout, IReadOnlyList<string> stopSequences);
}

public sealed class Holophore {
    private readonly IHolangSampler _sampler;
    private readonly object _loom; // opaque; allows delegation later if needed
    private readonly Rollout _rollout;

    public Dictionary<string, object?> Env { get; }
    public Dictionary<string, object?> SpanBindings { get; } = new();
    public Dictionary<string, FragList> SpanFragments { get; } = new();

    private readonly List<Holoware> _holowares = new();
    private Span? _span;
    private string _ego = "system";
    public int Errors { get; private set; }

    public Holophore(object loom, Rollout rollout, Dictionary<string, object?>? env, IHolangSampler sampler) {
        _loom = loom;
        _rollout = rollout;
        _sampler = sampler;
        Env = env ?? new();
    }

    public object? GetClass(string className) {
        // Lookup from Env first; else try to resolve in current AppDomain by simple name
        if (Env.TryGetValue(className, out var val) && val is not null) return val;
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.Name == className);
        return type;
    }

    public Span FindSpan(string uid) {
        foreach (var w in _holowares)
            foreach (var s in w.Spans)
                if (s.Uuid == uid) return s;
        throw new ArgumentException($"Span not found: {uid}");
    }

    public void NewContext() => _rollout.NewContext();
    public void EnsureContext() => _rollout.EnsureContext();

    public Frag AddFrag(FragType type, string text) {
        var frag = _rollout.AddFrag(_ego, type, text);
        if (_span != null) {
            if (!SpanFragments.TryGetValue(_span.Uuid, out var list)) {
                list = new FragList();
                SpanFragments[_span.Uuid] = list;
            }
            list.Add(frag);
        }
        return frag;
    }

    public void AddReinforced(string content) => AddFrag(FragType.Reinforce, content);
    public void AddMasked(string content) => AddFrag(FragType.Frozen, content);

    public string? Sample(IReadOnlyList<string> stopSequences) => _sampler.Sample(_rollout, stopSequences);

    public object? Invoke(object target, string funcName, IList<object?> args, IDictionary<string, object?> kwargs, bool optional = true, bool filterMissing = true) {
        // __init__ special-case for constructors
        if (funcName == "__init__") {
            if (target is not Type type)
                throw new ArgumentException("Target for __init__ must be a Type");
            try {
                // Try match by arg count with string parameters; simple best-effort
                var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                foreach (var ctor in ctors.OrderByDescending(c => c.GetParameters().Length)) {
                    var ps = ctor.GetParameters();
                    if (ps.Length == args.Count && ps.All(p => p.ParameterType == typeof(string))) {
                        return ctor.Invoke(args.ToArray());
                    }
                    if (ps.Length == 0 && args.Count == 0) return ctor.Invoke(null);
                }
                // fallback: parameterless
                var def = type.GetConstructor(Type.EmptyTypes);
                return def is null ? null : def.Invoke(null);
            } catch when (optional) { return null; }
        }

        var implType = target is Type t ? t : target.GetType();
        // Search MRO: type and base types
        Type? cur = implType;
        while (cur != null) {
            var method = cur.GetMethod(funcName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (method != null) {
                var finalArgs = new List<object?>();
                finalArgs.Add(target is Type ? null : target); // instance for non-static is target

                // Build positional argument list matching (Holophore, Span) if present
                var ps = method.GetParameters();
                var supplied = new List<object?>();
                foreach (var p in ps) {
                    if (p.ParameterType == typeof(Holophore)) supplied.Add(this);
                    else if (typeof(Span).IsAssignableFrom(p.ParameterType)) supplied.Add(_span);
                    else if (args.Count > 0) { supplied.Add(args[0]); args.RemoveAt(0); }
                    else if (kwargs.TryGetValue(p.Name!, out var v)) supplied.Add(v);
                    else if (p.HasDefaultValue) supplied.Add(p.DefaultValue);
                    else supplied.Add(null);
                }

                try {
                    return method.IsStatic
                        ? method.Invoke(null, supplied.ToArray())
                        : method.Invoke(target, supplied.ToArray());
                } catch when (optional) { return null; }
            }
            cur = cur.BaseType;
        }
        if (!optional) throw new MissingMethodException($"No {funcName} method found on {implType}");
        return null;
    }

    public (IList<object?>, IDictionary<string, object?>) GetHolofuncArgs(Span span) => (new List<object?> { this, span }, new Dictionary<string, object?>());

    // Internal plumbing used by Holoware execution
    internal void PushHoloware(Holoware w) => _holowares.Add(w);
    internal void PopHoloware(Holoware w) => _holowares.Remove(w);
    internal void SetSpan(Span? s) => _span = s;
    internal void SetEgo(string e) => _ego = e;
}

