using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Holang.Core.Parsing;

namespace Holang.Core.Runtime;

public sealed class HolowareLoader {
    private readonly List<string> _searchPaths;
    private readonly Dictionary<string, Holoware> _cache = new();

    public HolowareLoader(IEnumerable<string>? searchPaths = null) {
        _searchPaths = (searchPaths ?? new[] { "prompts", "hol" }).ToList();
    }

    public string? FindHolowarePath(string filename) {
        if (Path.IsPathRooted(filename) && File.Exists(filename)) return filename;
        if (filename.Contains(Path.DirectorySeparatorChar) && File.Exists(filename)) return filename;
        foreach (var dir in _searchPaths) {
            var full = Path.Combine(dir, filename);
            if (File.Exists(full)) return full;
        }
        return null;
    }

    public Holoware LoadHoloware(string filename) {
        if (_cache.TryGetValue(filename, out var cached)) return cached;
        var path = FindHolowarePath(filename) ?? throw new FileNotFoundException($"Holoware file not found: {filename}");
        var text = File.ReadAllText(path);
        text = HolowareParser.FilterComments(text);
        var tpl = Holoware.Parse(text);
        tpl.Name = Path.GetFileName(path);
        tpl.FilePath = path;
        _cache[filename] = tpl;
        return tpl;
    }

    public void ClearCache() => _cache.Clear();
    public List<string> ListPrompts() {
        var ret = new List<string>();
        foreach (var dir in _searchPaths) {
            try { ret.AddRange(Directory.GetFiles(dir, "*.hol").Select(Path.GetFileName)!); } catch { /* ignore */ }
        }
        return ret.Distinct().ToList();
    }
}

public static class DefaultHolowareLoader {
    private static HolowareLoader? _default;
    public static HolowareLoader Get(IEnumerable<string>? searchPaths = null) {
        if (_default is null || (searchPaths is not null && !searchPaths.SequenceEqual(_defaultPaths)))
            _default = new HolowareLoader(searchPaths);
        return _default;
    }
    private static IEnumerable<string>? _defaultPaths => new[] { "prompts", "hol" };
}

