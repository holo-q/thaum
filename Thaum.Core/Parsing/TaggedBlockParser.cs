using System.Text.RegularExpressions;

namespace Thaum.Core.Parsing;

/// <summary>
/// Generic, reusable tagged block extractor for LLM outputs and other semi-structured text.
/// Recognizes XML-like blocks with optional attributes and case-insensitive tag names:
///   <TAG key="value"> ... </TAG>
///   <tag> ... </tag>
/// Does not require well-formed XML elsewhere and is resilient to free-form content.
///
/// TODO we could add nested same-tag handling with a small stack if needed.
/// TODO we could add support for <block name="TAG"> ... </block> style.
/// </summary>
public static class TaggedBlockParser {
    private static readonly Regex StartTagRx = new Regex(
        pattern: @"(?is)<\s*([A-Za-z][A-Za-z0-9_-]*)\b([^>]*)>",
        options: RegexOptions.Compiled
    );

    private static readonly Regex EndTagRx = new Regex(
        pattern: @"(?is)</\s*([A-Za-z][A-Za-z0-9_-]*)\s*>",
        options: RegexOptions.Compiled
    );

    private static readonly Regex AttrRx = new Regex(
        pattern: @"(?is)([A-Za-z_][A-Za-z0-9_-]*)\s*=\s*(""([^""]*)""|'([^']*)'|([^\s>]+))",
        options: RegexOptions.Compiled
    );

    public record TaggedBlock(string Name, Dictionary<string, string> Attributes, string Content, int StartIndex, int EndIndex);

    /// <summary>
    /// Extracts all first-level tags from the input text. If allowedNames is provided, only returns those names.
    /// Tag names are matched case-insensitively and normalized to upper case in results.
    /// </summary>
    public static List<TaggedBlock> ExtractAll(string text, IEnumerable<string>? allowedNames = null) {
        var result = new List<TaggedBlock>();
        if (string.IsNullOrEmpty(text)) return result;

        HashSet<string>? allow = null;
        if (allowedNames != null) {
            allow = new HashSet<string>(allowedNames.Select(n => n.ToUpperInvariant()));
        }

        int index = 0;
        while (index < text.Length) {
            var m = StartTagRx.Match(text, index);
            if (!m.Success) break;

            string name = m.Groups[1].Value;
            string nameUp = name.ToUpperInvariant();
            int startTagEnd = m.Index + m.Length;

            // Self-closing tags are ignored for block extraction
            if (text[m.Index..startTagEnd].Contains("/>")) { index = startTagEnd; continue; }

            // Find closing tag of same name (first occurrence). Non-greedy search using regex from startTagEnd.
            var end = EndTagRx.Match(text, startTagEnd);
            while (end.Success && !string.Equals(end.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase)) {
                end = EndTagRx.Match(text, end.Index + end.Length);
            }
            if (!end.Success) { index = startTagEnd; continue; }

            int contentStart = startTagEnd;
            int contentEnd   = end.Index;
            string content   = text.Substring(contentStart, Math.Max(0, contentEnd - contentStart)).Trim();

            // Filter by allowed names if provided
            if (allow is null || allow.Contains(nameUp)) {
                var attrs = ParseAttributes(m.Groups[2].Value);
                result.Add(new TaggedBlock(nameUp, attrs, content, m.Index, end.Index + end.Length));
            }

            index = end.Index + end.Length;
        }

        return result;
    }

    /// <summary>
    /// Extracts blocks by name (case-insensitive). Returns empty list if not found.
    /// </summary>
    public static List<TaggedBlock> ExtractByName(string text, string name) {
        return ExtractAll(text, new[] { name });
    }

    /// <summary>
    /// Tries to extract the first block by name (case-insensitive).
    /// </summary>
    public static bool TryExtractFirst(string text, string name, out TaggedBlock? block) {
        block = ExtractByName(text, name).FirstOrDefault();
        return block is not null;
    }

    private static Dictionary<string, string> ParseAttributes(string raw) {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRx.Matches(raw)) {
            string key = m.Groups[1].Value;
            string val = m.Groups[3].Success ? m.Groups[3].Value
                        : m.Groups[4].Success ? m.Groups[4].Value
                        : m.Groups[5].Success ? m.Groups[5].Value
                        : string.Empty;
            if (!dict.ContainsKey(key)) dict[key] = val;
        }
        return dict;
    }
}
