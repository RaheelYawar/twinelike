using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;

namespace Harlowe
{
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
  internal class BranchCollector : IBodyVisitor
  {
    public readonly List<Branch> Branches = new List<Branch>();

    public void Visit(TextNode node) { }
    public void Visit(NewlineNode node) { }
    public void Visit(VariableNode node) { }
    public void Visit(Ast.Body.HtmlNode node) { }
    public void Visit(ParseErrorNode node) { }
    public void Visit(LinkNode node) => Branches.Add(new Branch { Text = node.Text, Name = node.Target });

    public void Visit(MacroNode node)
    {
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    public void Visit(HookNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public void Visit(FormatNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public void Visit(ChangerChainNode node)
    {
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    /// <summary>One-shot helper: walks <paramref name="ast"/> and returns its branches.</summary>
    public static List<Branch> Collect(PassageBody ast)
    {
      var visitor = new BranchCollector();
      foreach (var child in ast.Children) child.Accept(visitor);
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
  /// idiom, and its target is just as absent.</para>
  ///
  /// <para><b>Only literals.</b> A computed target (<c>(goto: $next)</c>) is
  /// beyond any static check and is skipped rather than guessed at.</para>
  /// </summary>
  internal class ReferenceCollector : IBodyVisitor, IExpressionVisitor
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
    /// which argument holds the passage name.
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

    // The macro currently being walked, so a reference found down in its
    // argument expressions can report the position of the call that holds it.
    private MacroNode _enclosing;

    public void Visit(TextNode node) { }
    public void Visit(NewlineNode node) { }
    public void Visit(VariableNode node) { }
    public void Visit(Ast.Body.HtmlNode node) { }
    public void Visit(ParseErrorNode node) { }

    public void Visit(LinkNode node) => References.Add(new Reference
    {
      Target = node.Target,
      LinkText = node.Text,
      Line = node.Line,
      Column = node.Column
    });

    public void Visit(MacroNode node)
    {
      AddIfNavigation(node.Name, node.Arguments, node.Line, node.Column);

      // Arguments may hold nested calls — (set: $x to (display: "P")).
      var priorEnclosing = _enclosing;
      _enclosing = node;
      VisitArguments(node.Arguments);
      _enclosing = priorEnclosing;

      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    public void Visit(HookNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public void Visit(FormatNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public void Visit(ChangerChainNode node)
    {
      node.Expression?.Accept(this);
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    // --- expression side: find navigation macros nested in arguments ---

    public void Visit(LiteralNode node) { }
    public void Visit(IdentifierNode node) { }
    public void Visit(VariableRefNode node) { }
    public void Visit(LambdaNode node) { }
    public void Visit(HookRefNode node) { }

    public void Visit(BinaryOpNode node)
    {
      node.Left?.Accept(this);
      node.Right?.Accept(this);
    }

    public void Visit(UnaryOpNode node) => node.Operand?.Accept(this);

    public void Visit(MacroCallNode node)
    {
      // An expression-position macro has no source position of its own; report
      // the body macro that encloses it, which is what the author would look for.
      AddIfNavigation(node.Name, node.Arguments,
                      _enclosing?.Line ?? 0, _enclosing?.Column ?? 0);
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

      References.Add(new Reference
      {
        Target = (string)literal.Value,
        MacroName = macroName,
        Line = line,
        Column = column
      });
    }

    /// <summary>One-shot helper: walks <paramref name="ast"/> and returns every passage reference in it.</summary>
    public static List<Reference> Collect(PassageBody ast)
    {
      var visitor = new ReferenceCollector();
      if (ast?.Children != null)
        foreach (var child in ast.Children) child.Accept(visitor);
      return visitor.References;
    }
  }
}
