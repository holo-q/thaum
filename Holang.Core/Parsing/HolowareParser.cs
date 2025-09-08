using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Holang.Core.Runtime;

namespace Holang.Core.Parsing;

public sealed class HolowareParser {
    private string _code;
    private int _pos;
    private readonly Holoware _ware = new();
    private string? _ego;
    private readonly bool _startWithSystem;

    public HolowareParser(string code, string? ego = null, bool startWithSystem = false) {
        _code = code;
        _pos = 0;
        _ego = ego;
        _startWithSystem = startWithSystem;
    }

    public Holoware Parse() {
        _code = FilterComments(_code);
        while (_pos < _code.Length) {
            var next = FindNextSpanStart();
            if (next == -1) { ParseText(_code[_pos..]); break; }
            if (next > _pos) ParseText(_code[_pos..next]);
            _pos = next;
            var spanText = ReadUntilSpanEnd();
            ParseSpan(spanText);
        }
        if (_startWithSystem) AddImplicitEgoIfNeeded();
        FinalizeTextSpans();
        return _ware;
    }

    private void AddImplicitEgoIfNeeded() {
        if (!string.IsNullOrEmpty(_ego)) return;
        foreach (var s in _ware.Spans) {
            if (s is TextSpan t) { if (!string.IsNullOrWhiteSpace(t.Text)) { _ware.Spans.Insert(0, new EgoSpan { Ego = "system" }); _ego = "system"; } break; }
            if (s is EgoSpan || s is ContextResetSpan) return;
        }
    }

    private void AddSpan(Span span) {
        var last = _ware.Spans.Count > 0 ? _ware.Spans[^1] : null;
        var isText = span is TextSpan;

        if (string.IsNullOrEmpty(_ego) && span is (SampleSpan or ObjSpan or ClassSpan)) {
            AddImplicitEgoIfNeeded();
            if (string.IsNullOrEmpty(_ego)) throw new InvalidOperationException($"Cannot have {span.GetType().Name} before an ego.");
        }

        if (span is EgoSpan eg) {
            if (_ego == eg.Ego) return; // skip duplicate consecutive ego
            _ego = eg.Ego;
        } else if (span is ContextResetSpan) {
            _ego = null;
        }

        if (isText && last is TextSpan lt) { lt.Text += ((TextSpan)span).Text; return; }
        if (isText && last is not TextSpan && last is not null) ((TextSpan)span).Text = ((TextSpan)span).Text.TrimStart();
        if (isText && string.IsNullOrWhiteSpace(((TextSpan)span).Text)) return;
        _ware.Spans.Add(span);
    }

    private void ParseSpan(string spantext) {
        var buf = new List<Span>();
        BuildSpan(buf, spantext);
        foreach (var s in buf) AddSpan(s);

        // If the last span is a ClassSpan, try to parse an indented body block
        var last = _ware.Spans.Count > 0 ? _ware.Spans[^1] : null;
        if (last is ClassSpan cs) {
            var (body, newPos) = ParseIndentedBlock(_code, _pos);
            if (body is not null) { cs.Body = body; _pos = newPos; }
        }
    }

    private void ParseText(string text) {
        if (string.IsNullOrEmpty(text)) return;
        var processed = text.Replace("\\\\", "\\").Replace("\\<|", "<|");
        if (string.IsNullOrEmpty(processed)) return;
        AddSpan(new TextSpan { Text = processed });
    }

    private int FindNextSpanStart() {
        var pos = _pos;
        while (true) {
            var found = _code.IndexOf("<|", pos, StringComparison.Ordinal);
            if (found == -1) return -1;
            var numBackslashes = 0;
            var i = found - 1;
            while (i >= 0 && _code[i] == '\\') { numBackslashes++; i--; }
            if (numBackslashes % 2 == 1) { pos = found + 1; continue; } // escaped
            return found;
        }
    }

    private (Holoware? body, int newPos) ParseIndentedBlock(string code, int startPos) {
        var (raw, endPos) = ReadIndentedBlockContent(code, startPos);
        if (raw is null) return (null, startPos);
        var dedented = Dedent(raw);
        if (string.IsNullOrWhiteSpace(dedented)) return (null, endPos);
        var parser = new HolowareParser(dedented);
        var body = parser.Parse();
        return (body, endPos);
    }

    private static string Dedent(string block) {
        var lines = block.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var indents = lines.Where(l => l.Length > 0).Select(l => l.TakeWhile(c => c == ' ').Count()).DefaultIfEmpty(0).Min();
        var sb = new StringBuilder();
        foreach (var line in lines) sb.AppendLine(line.Length >= indents ? line[indents..] : line);
        return sb.ToString();
    }

    private (string? content, int newPos) ReadIndentedBlockContent(string code, int startPos) {
        var lines = code[startPos..].Split(new[] { "\n" }, StringSplitOptions.None);
        if (lines.Length == 0) return (null, startPos);
        var first = lines[0];
        var curPos = startPos;
        if (string.IsNullOrWhiteSpace(first)) { curPos += first.Length + 1; lines = lines.Skip(1).ToArray(); if (lines.Length == 0) return (null, curPos); first = lines[0]; }
        var indentation = first.TakeWhile(c => c == ' ').Count();
        if (indentation == 0) return (null, startPos);
        var blockLines = new List<string>();
        foreach (var line in lines) {
            var indent = line.TakeWhile(c => c == ' ').Count();
            if (string.IsNullOrWhiteSpace(line)) { blockLines.Add(line); curPos += line.Length + 1; continue; }
            if (indent >= indentation) { blockLines.Add(line); curPos += line.Length + 1; }
            else break;
        }
        if (blockLines.Count == 0) return (null, startPos);
        return (string.Join("\n", blockLines), curPos);
    }

    private string ReadUntilSpanEnd() {
        if (_code.AsSpan(_pos).StartsWith("<|")) {
            var start = _pos + 2;
            var end = _code.IndexOf("|>", start, StringComparison.Ordinal);
            if (end == -1) throw new InvalidOperationException("Unclosed tag");
            _pos = end + 2;
            return _code[start..end];
        }
        throw new InvalidOperationException("Not at start of span");
    }

    private void FinalizeTextSpans() {
        Span? last = null;
        for (int i = 0; i < _ware.Spans.Count; i++) {
            var s = _ware.Spans[i];
            if (s is TextSpan t) {
                if (last is TextSpan lt) { lt.Text += t.Text; _ware.Spans.RemoveAt(i); i--; }
                else last = t;
            } else last = s;
        }
    }

    // Grammar / builders
    public static string FilterComments(string content) {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        return string.Join("\n", lines.Where(l => !l.TrimStart().StartsWith('#')));
    }

    private static bool IsEgoOrSampler(string @base, List<string> kargs, Dictionary<string, string> kwargs)
        => @base is "o_o" or "@_@" or "x_x" || kwargs.ContainsKey("fence") || kwargs.ContainsKey("<>");

    private static bool IsContextReset(string @base, List<string> kargs, Dictionary<string, string> kwargs)
        => new[] { "+++", "===", "---", "^^^", "###", "@@@", "\"\"\"", "***", "%%%" }.Contains(@base);

    private static readonly Dictionary<string, string> EgoMap = new() { ["o_o"] = "user", ["@_@"] = "assistant", ["x_x"] = "system" };

    private static (string Base, List<string> KArgs, Dictionary<string, string> KwArgs) ParseSpanTag(string tag) {
        var kargs = new List<string>();
        var kwargs = new Dictionary<string, string>();
        // simple shell-like split covering quotes
        var parts = Regex.Matches(tag, "\"[^\"]*\"|'[^']*'|\\S+").Select(m => m.Value.Trim()).ToList();
        if (parts.Count == 0) return (string.Empty, kargs, kwargs);
        var baseSpan = parts[0];
        foreach (var part in parts.Skip(1)) {
            if (part.Contains('=')) {
                var idx = part.IndexOf('=');
                var key = part[..idx];
                var val = part[(idx + 1)..].Trim('"', '\'');
                kwargs[key] = val;
            } else if (part.StartsWith("<>")) {
                if (part.Length > 2) kwargs["<>"] = part[2..];
                else throw new ArgumentException("Empty <> attribute");
            } else kargs.Add(part);
        }
        return (baseSpan, kargs, kwargs);
    }

    private static void BuildSpan(List<Span> outList, string spantext) {
        var tag = spantext.Trim();
        if (tag.Length == 0) return;
        var (baseSpan, kargs, kwargs) = ParseSpanTag(tag);

        if (IsEgoOrSampler(baseSpan, kargs, kwargs)) { BuildEgoOrSampler(outList, baseSpan, kargs, kwargs); return; }
        if (IsContextReset(baseSpan, kargs, kwargs)) { BuildContext(outList, train: baseSpan == "+++"); return; }

        if (!string.IsNullOrEmpty(baseSpan)) {
            if (char.IsUpper(baseSpan[0])) BuildClass(outList, baseSpan, kargs, kwargs);
            else BuildObj(outList, baseSpan, kargs, kwargs);
        }
    }

    private static void BuildClass(List<Span> outList, string @base, List<string> kargs, Dictionary<string, string> kwargs) {
        var span = new ClassSpan { ClassName = @base };
        span.WithArgs(kargs, kwargs, new());
        span.Body = null;
        outList.Add(span);
    }

    private static void BuildEgoOrSampler(List<Span> outList, string @base, List<string> kargs, Dictionary<string, string> kwargs) {
        var parts = @base.Split(':', 2);
        var ego = parts[0];
        var spanId = parts.Length > 1 ? parts[1] : string.Empty;
        outList.Add(new EgoSpan { Ego = EgoMap.TryGetValue(ego, out var mapped) ? mapped : ego, Uuid = spanId });

        var samplerKw = kwargs.Where(kv => kv.Key != "<>" && kv.Key != "fence").ToDictionary(kv => kv.Key, kv => kv.Value);
        var fence = kwargs.GetValueOrDefault("<>") ?? kwargs.GetValueOrDefault("fence") ?? string.Empty;
        if (kargs.Count > 0 || samplerKw.Count > 0 || !string.IsNullOrEmpty(fence)) {
            if (!string.IsNullOrEmpty(fence)) {
                outList.Add(new SampleSpan { Uuid = spanId, Id = spanId, KArgs = new List<string>(kargs), KwArgs = samplerKw, Fence = fence });
            }
        }
    }

    private static void BuildContext(List<Span> outList, bool train) { outList.Add(new ContextResetSpan(train)); }

    private static void BuildObj(List<Span> outList, string @base, List<string> kargs, Dictionary<string, string> kwargs) {
        var ids = @base.Split('|').Select(v => v.Trim()).ToList();
        var span = new ObjSpan { VarIds = ids };
        span.WithArgs(kargs, kwargs, new());
        outList.Add(span);
    }
}

