using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Thaum.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class TraceWrapGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Emit attributes: TraceWrap + InterceptsLocation stub (compiler recognizes it at compile-time)
        context.RegisterPostInitializationOutput(spc =>
        {
            spc.AddSource("TraceWrapAttribute.g.cs", SourceText.From(AttributeSource, Encoding.UTF8));
            spc.AddSource("InterceptsLocationAttribute.g.cs", SourceText.From(InterceptsLocationStub, Encoding.UTF8));
        });

        // Gather all methods with [TraceWrap]
        var methods = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                static (ctx, _) =>
                {
                    var method = (MethodDeclarationSyntax)ctx.Node;
                    var model = ctx.SemanticModel;
                    var symbol = model.GetDeclaredSymbol(method) as IMethodSymbol;
                    if (symbol == null) return default(MethodInfo);
                    foreach (var attr in symbol.GetAttributes())
                    {
                        if (attr.AttributeClass?.ToDisplayString() == "Thaum.Meta.TraceWrapAttribute")
                        {
                            return new MethodInfo(symbol);
                        }
                    }
                    return default;
                })
            .Where(m => m.Symbol is not null);

        // Map invocation sites across the compilation. We filter to traced methods later
        var invocations = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (ctx, _) =>
                {
                    var inv = (InvocationExpressionSyntax)ctx.Node;
                    var model = ctx.SemanticModel;
                    var symbol = model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
                    if (symbol == null) return default(InvocationInfo);

                    // Interceptors require the location to refer to the invoked method identifier token,
                    // not the whole invocation span. Compute the name token location if possible.
                    string file = string.Empty;
                    int line = 0, col = 0;

                    SyntaxNode? expr = inv.Expression;
                    Location? nameLoc = expr switch
                    {
                        MemberAccessExpressionSyntax mae => mae.Name.GetLocation(),
                        MemberBindingExpressionSyntax mbe => mbe.Name.GetLocation(),
                        IdentifierNameSyntax id => id.GetLocation(),
                        GenericNameSyntax gen => gen.GetLocation(),
                        _ => inv.GetLocation(), // fallback: may not be interceptable, but keeps behavior
                    };

                    var span = nameLoc.GetLineSpan();
                    file = span.Path ?? string.Empty;
                    line = span.StartLinePosition.Line + 1;   // 1-based
                    col = span.StartLinePosition.Character + 1; // 1-based

                    return new InvocationInfo(symbol, file, line, col);
                })
            .Where(i => i.Target is not null);

        // Combine and generate wrappers
        var combo = methods.Collect().Combine(invocations.Collect()).Combine(context.CompilationProvider);
        context.RegisterSourceOutput(combo, static (spc, data) =>
        {
            var ((methodsCol, invocationsCol), compilation) = data;
            if (methodsCol.IsDefaultOrEmpty || invocationsCol.IsDefaultOrEmpty) return;

            // Build a set of call-target symbols to intercept for traced methods, including base/interface members
            var callTargets = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var m in methodsCol)
            {
                var sym = m.Symbol;
                if (sym is null) continue;

                // Add the traced method itself
                callTargets.Add(sym);

                // Add its overridden base chain (if any)
                var b = sym.OverriddenMethod;
                while (b is not null)
                {
                    callTargets.Add(b);
                    b = b.OverriddenMethod;
                }

                // Add any interface members it implements (explicit or implicit)
                var type = sym.ContainingType;
                foreach (var iface in type.AllInterfaces)
                {
                    foreach (var mem in iface.GetMembers(sym.Name).OfType<IMethodSymbol>())
                    {
                        var impl = type.FindImplementationForInterfaceMember(mem) as IMethodSymbol;
                        if (impl is null) continue;
                        // ReducedFrom handles extension-reduction edge cases; fallback to direct compare
                        var targetImpl = impl.ReducedFrom ?? impl;
                        if (SymbolEqualityComparer.Default.Equals(targetImpl, sym))
                            callTargets.Add(mem);
                    }
                }
            }

            // Only keep invocations whose target is one of the call-targets
            var sites = invocationsCol.Where(i => callTargets.Contains(i.Target!)).ToImmutableArray();
            if (sites.IsDefaultOrEmpty) return;

            // Emit one wrapper class per containing namespace to keep file sizes modest
            var byNs = sites.GroupBy(i => i.Target!.ContainingType.ContainingNamespace?.ToDisplayString() ?? "");
            foreach (var group in byNs)
            {
                var sb = new StringBuilder();
                sb.AppendLine("// <auto-generated/>\n#nullable enable");
                sb.AppendLine("using Microsoft.Extensions.Logging;");
                var ns = group.Key;
                if (!string.IsNullOrEmpty(ns)) sb.Append("namespace ").Append(ns).AppendLine(";");
                sb.AppendLine("internal static partial class __TraceWrap_Generated");
                sb.AppendLine("{");

                int idx = 0;
                foreach (var site in group)
                {
                    try { EmitWrapper(sb, site, ref idx); }
                    catch { /* be resilient */ }
                }

                sb.AppendLine("}");
                var hint = ($"__TraceWrap_{(string.IsNullOrEmpty(ns) ? "global" : ns.Replace('.', '_'))}.g.cs");
                spc.AddSource(hint, SourceText.From(sb.ToString(), Encoding.UTF8));
            }
        });
    }

    private static void EmitWrapper(StringBuilder sb, InvocationInfo site, ref int idx)
    {
        var target = site.Target!;
        var type = target.ContainingType;
        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string methodName = target.Name;
        bool isStatic = target.IsStatic;

        // Build parameter list for wrapper
        var ps = new List<string>();
        var callArgs = new List<string>();
        if (!isStatic)
        {
            ps.Add($"{typeName} __self");
        }
        foreach (var p in target.Parameters)
        {
            string mod = p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", RefKind.In => "in ", _ => string.Empty };
            ps.Add($"{mod}{p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {p.Name}");
            callArgs.Add($"{mod}{p.Name}");
        }
        string paramList = string.Join(", ", ps);
        string argList = string.Join(", ", callArgs);

        // Determine return shape
        var ret = target.ReturnType;
        bool isTask = ret.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat).StartsWith("Task<") || ret.Name == "Task";
        bool isVoid = ret.SpecialType == SpecialType.System_Void;
        string retType = ret.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Unique wrapper name per call site
        string wrapperName = $"{methodName}__wrap_{idx++}";

        // Header with InterceptsLocation
            sb.Append("    [System.Diagnostics.DebuggerStepThrough]\n    [System.Runtime.CompilerServices.InterceptsLocation(\"")
              .Append(Escape(site.FilePath)).Append('\"').Append(", ").Append(site.Line).Append(", ").Append(site.Column).AppendLine(")]");

        if (isTask)
        {
            sb.Append("    internal static async ").Append(retType).Append(' ').Append(wrapperName).Append('(').Append(paramList).AppendLine(")");
            sb.AppendLine("    {");
            sb.AppendLine("        var __lg = global::Thaum.Core.Utils.Logging.Get(\"TraceWrap\");");
            sb.AppendLine("        var __sw = System.Diagnostics.Stopwatch.StartNew();");
            // Log compile-time type/method to keep generated code simple and robust
            sb.Append("        __lg.LogTrace(\"→ ")
              .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine("\");");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            if (ret.Name == "Task")
            {
                sb.Append("            await ").Append(isStatic ? $"{typeName}.{methodName}({argList})" : $"__self.{methodName}({argList})").AppendLine(";");
                sb.AppendLine("            __sw.Stop();");
                sb.Append("            __lg.LogTrace(\"← ")
                  .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine(" in {ms}ms\", __sw.ElapsedMilliseconds);");
                sb.AppendLine("            return;");
            }
            else
            {
                sb.Append("            var __ret = await ").Append(isStatic ? $"{typeName}.{methodName}({argList})" : $"__self.{methodName}({argList})").AppendLine(".ConfigureAwait(false);");
                sb.AppendLine("            __sw.Stop();");
                sb.Append("            __lg.LogTrace(\"← ")
                  .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine(" in {ms}ms\", __sw.ElapsedMilliseconds);");
                sb.AppendLine("            return __ret;");
            }
            sb.AppendLine("        }");
            sb.AppendLine("        catch (System.Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            __sw.Stop();");
            sb.Append("            __lg.LogError(ex, \"✖ ")
              .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine(" in {ms}ms\", __sw.ElapsedMilliseconds);");
            sb.AppendLine("            throw;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
        }
        else
        {
            sb.Append("    internal static ").Append(retType).Append(' ').Append(wrapperName).Append('(').Append(paramList).AppendLine(")");
            sb.AppendLine("    {");
            sb.AppendLine("        var __lg = global::Thaum.Core.Utils.Logging.Get(\"TraceWrap\");");
            sb.AppendLine("        var __sw = System.Diagnostics.Stopwatch.StartNew();");
            sb.Append("        __lg.LogTrace(\"→ ")
              .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine("\");");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            if (!isVoid) sb.Append("            var __ret = "); else sb.Append("            ");
            sb.Append(isStatic ? $"{typeName}.{methodName}({argList});" : $"__self.{methodName}({argList});");
            sb.AppendLine();
            sb.AppendLine("            __sw.Stop();");
            sb.Append("            __lg.LogTrace(\"← ")
                  .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine(" in {ms}ms\", __sw.ElapsedMilliseconds);");
            if (!isVoid) sb.AppendLine("            return __ret;");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (System.Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            __sw.Stop();");
            sb.Append("            __lg.LogError(ex, \"✖ ")
              .Append(Escape(type.Name)).Append('.').Append(Escape(methodName)).AppendLine(" in {ms}ms\", __sw.ElapsedMilliseconds);");
            sb.AppendLine("            throw;");
            sb.AppendLine("        }");
            if (isVoid) sb.AppendLine("        return;");
            sb.AppendLine("    }");
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private readonly struct MethodInfo
    {
        public IMethodSymbol? Symbol { get; }
        public MethodInfo(IMethodSymbol? symbol) { Symbol = symbol; }
    }

    private readonly struct InvocationInfo
    {
        public IMethodSymbol? Target { get; }
        public string FilePath { get; }
        public int Line { get; }
        public int Column { get; }
        public InvocationInfo(IMethodSymbol? target, string filePath, int line, int column)
        { Target = target; FilePath = filePath; Line = line; Column = column; }
    }

    private const string AttributeSource = @"// <auto-generated/>
namespace Thaum.Meta
{
    [System.AttributeUsage(System.AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    internal sealed class TraceWrapAttribute : System.Attribute { }
}
";

    private const string InterceptsLocationStub = @"// <auto-generated/>
namespace System.Runtime.CompilerServices
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
    internal sealed class InterceptsLocationAttribute : global::System.Attribute
    {
        public InterceptsLocationAttribute(string filePath, int line, int column) { }
    }
}
";
}
