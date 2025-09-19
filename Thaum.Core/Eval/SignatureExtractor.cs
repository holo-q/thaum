using TreeSitter;

namespace Thaum.Core.Eval;

public record MethodSignature(string? Name, string? ReturnType, int ParamCount);

public static class SignatureExtractor {
	public static MethodSignature ExtractCSharp(string source) {
		try {
			using Language lang   = new Language("c-sharp");
			using Parser   parser = new Parser(lang);
			using Tree     tree   = parser.Parse(source)!;
			Node           root   = tree.RootNode;

			// Find first method_declaration in snippet
			Node? method = FindFirst(root, n => n.Type == "method_declaration");
			if (method is null) return new MethodSignature(null, null, 0);

			string? name       = null;
			string? returnType = null;
			int     paramCount = 0;

			// Traverse children
			TreeCursor cursor = method.Walk();
			try {
				if (cursor.GotoFirstChild()) {
					do {
						Node n = cursor.CurrentNode;
						switch (n.Type) {
							case "identifier":
								name ??= n.Text;
								break;
							case "parameter_list":
								// Count parameters (identifiers or commas+1)
								paramCount = CountParams(n);
								break;
							default: {
								if (n.Type == "predefined_type" || n.Type == "qualified_name" || n.Type == "identifier") {
									// heuristically capture return type: first type before identifier (name)
									// we'll refine: check sibling ordering
								}
								break;
							}
						}
					} while (cursor.GotoNextSibling());
				}
			} finally { cursor.Dispose(); }

			// Return type heuristic: search immediate children prior to identifier token
			returnType = FindReturnType(method);

			return new MethodSignature(name, returnType, paramCount);
		} catch {
			return new MethodSignature(null, null, 0);
		}
	}

	private static int CountParams(Node paramList) {
		// parameters node contains parameter elements separated by commas; simple heuristic
		int        count  = 0;
		TreeCursor cursor = paramList.Walk();
		try {
			if (!cursor.GotoFirstChild()) return 0;
			do {
				Node n = cursor.CurrentNode;
				if (n.Type == "parameter") count++;
			} while (cursor.GotoNextSibling());
		} finally { cursor.Dispose(); }
		return count;
	}

	private static string? FindReturnType(Node method) {
		// In csharp grammar, return type appears as a child named type or predefined_type/qualified_name before identifier
		List<Node> children = [];
		TreeCursor cursor   = method.Walk();
		try {
			if (cursor.GotoFirstChild()) {
				do { children.Add(cursor.CurrentNode); } while (cursor.GotoNextSibling());
			}
		} finally { cursor.Dispose(); }

		int idxIdentifier = children.FindIndex(n => n.Type == "identifier");
		if (idxIdentifier > 0) {
			for (int i = idxIdentifier - 1; i >= 0; i--) {
				Node t = children[i];
				if (t.Type is "predefined_type" or "qualified_name" or "identifier" or "generic_name") {
					return t.Text;
				}
				if (t.Type.EndsWith("_list") || t.Type.Contains("attribute") || t.Type.Contains("modifier")) {
					continue;
				}
				// stop if we passed plausible type region
				break;
			}
		}
		return null;
	}

	private static Node? FindFirst(Node node, Func<Node, bool> pred) {
		if (pred(node)) return node;
		TreeCursor cursor = node.Walk();
		try {
			if (!cursor.GotoFirstChild()) return null;
			do {
				Node? found = FindFirst(cursor.CurrentNode, pred);
				if (found != null) return found;
			} while (cursor.GotoNextSibling());
			return null;
		} finally { cursor.Dispose(); }
	}
}