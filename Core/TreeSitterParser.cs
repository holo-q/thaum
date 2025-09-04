using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using TreeSitter;
using Thaum.Core.Models;
using ThaumPosition = Thaum.Core.Models.Position;

namespace Thaum.Core.Services;

public class TreeSitterParser : IDisposable {
	private readonly ILogger<TreeSitterParser> _logger;
	private readonly Parser                    _parser;
	private readonly Language                  _language;

	public TreeSitterParser(string language, ILogger<TreeSitterParser> logger) {
		_logger   = logger;
		_language = new Language(language);
		_parser   = new Parser(_language);
	}

	public List<CodeSymbol> Parse(string sourceCode, string filePath) {
		var       symbols = new List<CodeSymbol>();
		using var tree    = _parser.Parse(sourceCode);
		var       query   = new Query(_language, TreeSitterQueries.UniversalQuery);
		var       matches = query.Execute(tree.RootNode).Matches.ToList();

		foreach (var match in matches) {
			Node? nameNode = null;
			Node? bodyNode = null;

			foreach (var capture in match.Captures) {
				if (capture.Name.EndsWith(".name")) {
					nameNode = capture.Node;
				} else if (capture.Name.EndsWith(".body")) {
					bodyNode = capture.Node;
				}
			}

			if (nameNode != null && bodyNode != null) {
				var captureName = match.Captures.First(c => c.Name.EndsWith(".name")).Name;
				var symbolKind  = GetSymbolKind(captureName);

				symbols.Add(new CodeSymbol(
					Name: nameNode.Text,
					Kind: symbolKind,
					FilePath: filePath,
					StartPosition: new ThaumPosition((int)nameNode.StartPosition.Row, (int)nameNode.StartPosition.Column),
					EndPosition: new ThaumPosition((int)bodyNode.EndPosition.Row, (int)bodyNode.EndPosition.Column)
				));
			}
		}

		return symbols;
	}

	private SymbolKind GetSymbolKind(string captureName) {
		if (captureName.StartsWith("namespace")) {
			return SymbolKind.Namespace;
		} else if (captureName.StartsWith("function")) {
			return SymbolKind.Function;
		} else if (captureName.StartsWith("method")) {
			return SymbolKind.Method;
		} else if (captureName.StartsWith("constructor")) {
			return SymbolKind.Constructor;
		} else if (captureName.StartsWith("property")) {
			return SymbolKind.Property;
		} else if (captureName.StartsWith("field")) {
			return SymbolKind.Field;
		} else if (captureName.StartsWith("interface")) {
			return SymbolKind.Interface;
		} else if (captureName.StartsWith("class")) {
			return SymbolKind.Class;
		} else if (captureName.StartsWith("enum_member")) {
			return SymbolKind.EnumMember;
		} else if (captureName.StartsWith("enum")) {
			return SymbolKind.Enum;
		} else {
			return SymbolKind.Variable;
		}
	}

	public void Dispose() {
		_parser.Dispose();
	}
}