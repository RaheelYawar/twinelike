using System.Collections.Generic;
using Harlowe.Ast.Body;

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
}
