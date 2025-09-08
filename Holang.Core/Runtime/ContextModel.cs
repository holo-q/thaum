using System;
using System.Collections.Generic;
using System.Linq;

namespace Holang.Core.Runtime;

public enum FragType { Frozen, Reinforce }

public sealed class Frag {
    public required string Text { get; init; }
    public string? Ego { get; init; }
    public required FragType Type { get; init; }
}

public sealed class FragList : List<Frag> {
    public static readonly FragList Empty = new();
    public string String => string.Concat(this.Select(f => f.Text));
    public int Length => Count;
}

public enum AutoMask { FreezeAll, ReinforceAll, ReinforceUser, ReinforceAssistant }

public sealed class Context {
    public string Text { get; set; } = string.Empty; // optional raw backing text
    public FragList Fragments { get; } = new();

    public Frag AddFrag(string? ego, string text, FragType type) {
        var frag = new Frag { Ego = ego, Text = text, Type = type };
        Fragments.Add(frag);
        return frag;
    }

    public Frag AddFrozen(string? ego, string text) => AddFrag(ego, text, FragType.Frozen);
    public Frag AddReinforced(string? ego, string text) => AddFrag(ego, text, FragType.Reinforce);

    public List<Dictionary<string, string>> ToApiMessages(bool renderDry = false) {
        static string NormalizeRole(string? raw, bool isFirst) => raw switch {
            "system" => "system",
            "user" => "user",
            "assistant" => "assistant",
            null when isFirst => "system",
            _ => "user"
        };

        var messages = new List<Dictionary<string, string>>();
        var texts = new List<string>();
        string? currentRole = null;

        for (int i = 0; i < Fragments.Count; i++) {
            var frag = Fragments[i];
            var norm = NormalizeRole(frag.Ego, isFirst: i == 0);
            if (currentRole is null) {
                currentRole = norm; texts = new List<string> { frag.Text };
            } else if (norm == currentRole) {
                texts.Add(frag.Text);
            } else {
                var s = string.Concat(texts);
                if (s.Length > 0 || renderDry)
                    messages.Add(new() { ["role"] = currentRole!, ["content"] = s.Trim() });
                currentRole = norm; texts = new List<string> { frag.Text };
            }
        }

        if (currentRole is not null) {
            var s = string.Concat(texts);
            if (s.Length > 0 || renderDry)
                messages.Add(new() { ["role"] = currentRole, ["content"] = s });
        }

        return messages;
    }

    public string ToApiString() {
        var msgs = ToApiMessages();
        var parts = msgs.Select(m => $"<|im_start|>{m.GetValueOrDefault("role", "user")}\n{m.GetValueOrDefault("content", string.Empty)}\n<|im_end|>");
        var text = string.Join("\n", parts);

        if (Fragments.Count > 0) {
            var last = Fragments[^1];
            var lastRole = last.Ego ?? (Fragments.Count == 1 ? "system" : "user");
            if (lastRole == "assistant" && string.IsNullOrEmpty(last.Text))
                return text + (text.Length > 0 ? "\n" : string.Empty) + "<|im_start|>assistant";
        }
        return text;
    }
}

public sealed class Rollout {
    public List<Context> Contexts { get; } = new();
    public Context ActiveContext => Contexts.Count > 0 ? Contexts[^1] : NewContext();

    public Context NewContext() { var c = new Context(); Contexts.Add(c); return c; }
    public void EnsureContext() { if (Contexts.Count == 0) NewContext(); }

    public Frag AddFrag(string? ego, FragType type, string text) {
        EnsureContext();
        return type == FragType.Frozen ? ActiveContext.AddFrozen(ego, text) : ActiveContext.AddReinforced(ego, text);
    }
}

