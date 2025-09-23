using TreeSitter;

namespace Thaum.Core.Eval;

public record AstSignals(int AwaitCount, int BranchCount, int CallCount, int BlockCount, int ElseCount);

public static class TreeSitterGates {
	public static AstSignals AnalyzeFunctionSource(string language, string sourceCode) {
		try {
			// Only C# high-quality for now; fallback returns zeros
			if (language.ToLowerInvariant() == "c-sharp") {
				using Language lang   = new Language("c-sharp");
				using Parser   parser = new Parser(lang);
				using Tree     tree   = parser.Parse(sourceCode)!;
				Node           root   = tree.RootNode;

				int awaits   = Count(root, n => n.Type == "await_expression");
				int branches = Count(root, n => n.Type is "if_statement" or "switch_statement" or "for_statement" or "while_statement" or "foreach_statement" or "do_statement");
				int calls    = Count(root, n => n.Type is "invocation_expression");
				int blocks   = Count(root, n => n.Type == "block");
				int elses    = Count(root, n => n.Type == "else_clause");

				return new AstSignals(awaits, branches, calls, blocks, elses);
			}
		} catch {
			// ignore and fall through
		}
		return new AstSignals(0, 0, 0, 0, 0);
	}

	private static int Count(Node node, Func<Node, bool> pred) {
		int        count  = 0;
		TreeCursor cursor = node.Walk();
		try {
			if (pred(node)) count++;
			if (!cursor.GotoFirstChild()) return count;
			do {
				count += Count(cursor.CurrentNode, pred);
			} while (cursor.GotoNextSibling());
			return count;
		} finally {
			cursor.Dispose();
		}
	}
}