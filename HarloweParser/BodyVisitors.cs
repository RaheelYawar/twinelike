using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;

namespace Harlowe
{
  /// <summary>
  /// Base traversal for <see cref="IBodyVisitor"/> collectors: leaves are
  /// no-ops, containers recurse (hooks — including hooks attached to macros and
  /// changer chains — and inline-format spans). Collectors override just the
  /// node they collect; a new container node added here descends for every
  /// collector at once, instead of each hand-rolled copy having to remember to.
  /// </summary>
  internal abstract class BodyWalker : IBodyVisitor
  {
    public virtual void Visit(TextNode node) { }
    public virtual void Visit(NewlineNode node) { }
    public virtual void Visit(VariableNode node) { }
    public virtual void Visit(Ast.Body.HtmlNode node) { }
    public virtual void Visit(LinkNode node) { }
    public virtual void Visit(ParseErrorNode node) { }

    public virtual void Visit(MacroNode node)
    {
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    public virtual void Visit(HookNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public virtual void Visit(FormatNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public virtual void Visit(ChangerChainNode node)
    {
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    /// <summary>Visits every top-level child of <paramref name="ast"/>; null-safe.</summary>
    public void Walk(PassageBody ast)
    {
      if (ast?.Children == null) return;
      foreach (var child in ast.Children) child.Accept(this);
    }
  }

  /// <summary>
  /// Collects every <see cref="LinkNode"/> in a body AST into a flat list of
  /// <see cref="Branch"/>es, preserving source order. Recurses into hooks —
  /// including hooks attached to <see cref="MacroNode"/>s and
  /// <see cref="ChangerChainNode"/>s — so links inside conditional or
  /// changer-styled blocks are still collected.
  ///
  /// <para>Used by both the HTML loader (<see cref="Harlowe"/>) and the Twee
  /// loader (<see cref="Twee.TweeReader"/>) to derive
  /// <see cref="HarlowePassage.Branches"/> from the AST. Lives in the root
  /// namespace as an <c>internal</c> helper because it is an implementation
  /// detail of how the public passage model gets populated, not part of the
  /// engine-facing surface.</para>
  /// </summary>
  internal class BranchCollector : BodyWalker
  {
    public readonly List<Branch> Branches = new List<Branch>();

    public override void Visit(LinkNode node) => Branches.Add(new Branch { Text = node.Text, Name = node.Target });

    /// <summary>One-shot helper: walks <paramref name="ast"/> and returns its branches.</summary>
    public static List<Branch> Collect(PassageBody ast)
    {
      var visitor = new BranchCollector();
      visitor.Walk(ast);
      return visitor.Branches;
    }
  }

  /// <summary>
  /// Collects every passage <em>reference</em> in a body AST, in source order —
  /// both the <c>[[…]]</c> syntax and a literal passage name handed to a
  /// navigation macro (<see cref="NavigationMacros"/>). Backs
  /// <see cref="Harlowe.GetBrokenLinks"/>, which keeps the ones that point at
  /// nothing.
  ///
  /// <para>Covering the macros matters: an engine that reports "your story's
  /// navigation is clean" while a <c>(goto: "Typo")</c> waits inside an
  /// <c>(if:)</c> branch is worse than one that says nothing. So this walks the
  /// expression trees too — <c>(set: $x to (display: "Missing"))</c> is a real
  /// idiom, and its target is just as absent — including lambda clauses
  /// (<c>(altered: via (display: "X"), …)</c>).</para>
  ///
  /// <para><b>Only literals.</b> A computed target — <c>(goto: $next)</c>, or a
  /// variable link target <c>[[Go-&gt;$next]]</c> — is beyond any static check
  /// and is skipped rather than guessed at.</para>
  /// </summary>
  internal class ReferenceCollector : BodyWalker, IExpressionVisitor
  {
    /// <summary>One collected reference: the target, plus enough to point at it.</summary>
    internal struct Reference
    {
      public string Target;
      public string LinkText;   // null for a macro reference
      public string MacroName;  // null for a [[…]] link
      public int Line;
      public int Column;
    }

    /// <summary>
    /// Navigation macros keyed by <see cref="MacroNames.Normalize"/>d name, and
    /// which argument holds the passage name. Must cover every navigation macro
    /// <c>Runtime.Macros.StandardMacros.RegisterAll</c> wires up —
    /// <c>BrokenLinkTests</c> asserts the two can't drift.
    ///
    /// <para><c>link-goto</c> is the odd one: <c>(link-goto: "Cellar")</c> uses
    /// its <em>text</em> as the target, so the index means "argument 1 if there
    /// is one, else argument 0". Encoded as <see cref="TargetArg.SecondOrFirst"/>
    /// rather than special-cased at the use site.</para>
    /// </summary>
    private enum TargetArg { First, Second, SecondOrFirst }

    private static readonly Dictionary<string, TargetArg> NavigationMacros =
      new Dictionary<string, TargetArg>
      {
        { "goto",          TargetArg.First },          // (goto:) / (go-to:) normalize alike
        { "display",       TargetArg.First },
        { "linkgoto",      TargetArg.SecondOrFirst },
        { "clickgoto",     TargetArg.Second },
        { "mouseovergoto", TargetArg.Second },
        { "mouseoutgoto",  TargetArg.Second },
      };

    public readonly List<Reference> References = new List<Reference>();

    public override void Visit(LinkNode node)
    {
      // A variable target ([[Go->$next]]) is computed — resolved by the
      // renderer at play time — so it's as undecidable here as (goto: $next).
      if (LinkNode.TryParseVariableTarget(node.Target, out _, out _)) return;
      References.Add(new Reference
      {
        Target = node.Target,
        LinkText = node.Text,
        Line = node.Line,
        Column = node.Column
      });
    }

    public override void Visit(MacroNode node)
    {
      AddIfNavigation(node.Name, node.Arguments, node.Line, node.Column);
      // Arguments may hold nested calls — (set: $x to (display: "P")).
      VisitArguments(node.Arguments);
      base.Visit(node);
    }

    public override void Visit(ChangerChainNode node)
    {
      node.Expression?.Accept(this);
      base.Visit(node);
    }

    // --- expression side: find navigation macros nested in arguments ---

    public void Visit(LiteralNode node) { }
    public void Visit(IdentifierNode node) { }
    public void Visit(VariableRefNode node) { }
    public void Visit(HookRefNode node) { }

    public void Visit(LambdaNode node)
    {
      node.WhereClause?.Accept(this);
      node.ViaClause?.Accept(this);
      node.WhenClause?.Accept(this);
    }

    public void Visit(BinaryOpNode node)
    {
      node.Left?.Accept(this);
      node.Right?.Accept(this);
    }

    public void Visit(UnaryOpNode node) => node.Operand?.Accept(this);

    public void Visit(MacroCallNode node)
    {
      AddIfNavigation(node.Name, node.Arguments, node.Line, node.Column);
      VisitArguments(node.Arguments);
    }

    private void VisitArguments(List<IExpressionNode> arguments)
    {
      if (arguments == null) return;
      for (int i = 0; i < arguments.Count; i++) arguments[i]?.Accept(this);
    }

    /// <summary>Record a reference if this is a navigation macro whose target argument is a literal string.</summary>
    private void AddIfNavigation(string macroName, List<IExpressionNode> arguments, int line, int column)
    {
      if (macroName == null || arguments == null) return;
      if (!NavigationMacros.TryGetValue(MacroNames.Normalize(macroName), out var which)) return;

      int index;
      switch (which)
      {
        case TargetArg.First: index = 0; break;
        case TargetArg.Second: index = 1; break;
        default: index = arguments.Count > 1 ? 1 : 0; break;   // SecondOrFirst
      }
      if (index >= arguments.Count) return;

      if (!(arguments[index] is LiteralNode literal)) return;   // computed target — unknowable
      if (literal.Kind != LiteralKind.String) return;
      // `as` rather than a cast: a hand-built AST can pair Kind = String with a
      // non-string Value, and GetBrokenLinks promises never to throw.
      if (!(literal.Value is string target)) return;

      References.Add(new Reference
      {
        Target = target,
        MacroName = macroName,
        Line = line,
        Column = column
      });
    }

    /// <summary>One-shot helper: walks <paramref name="ast"/> and returns every passage reference in it.</summary>
    public static List<Reference> Collect(PassageBody ast)
    {
      var visitor = new ReferenceCollector();
      visitor.Walk(ast);
      return visitor.References;
    }
  }

  /// <summary>
  /// Collects every <see cref="ParseErrorNode"/> in a body AST, in source order.
  /// Backs <see cref="Harlowe.GetParseErrors"/> and the loader's passage-name
  /// decoration pass.
  ///
  /// <para>Recurses into hooks (including hooks attached to macros and changer
  /// chains) because per-node recovery can leave an error deep inside an
  /// otherwise-fine passage — <c>(if: $x)[(for: each)]</c> parses down to the
  /// hook and fails there, and an engine still needs to be told.</para>
  /// </summary>
  internal class ParseErrorCollector : BodyWalker
  {
    public readonly List<ParseErrorNode> Errors = new List<ParseErrorNode>();

    public override void Visit(ParseErrorNode node) => Errors.Add(node);

    /// <summary>One-shot helper: walks <paramref name="ast"/> and returns every parse-error node in it.</summary>
    public static List<ParseErrorNode> Collect(PassageBody ast)
    {
      var visitor = new ParseErrorCollector();
      visitor.Walk(ast);
      return visitor.Errors;
    }
  }
}
