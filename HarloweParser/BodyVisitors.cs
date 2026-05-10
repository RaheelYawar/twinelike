using System.Collections.Generic;
using System.Text;
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

  /// <summary>
  /// Renders a body AST back to a plain-prose string with link markup stripped
  /// and macros omitted. Variables are re-emitted with their sigil so prose
  /// like "Hello $name." round-trips. Used by both loaders to derive
  /// <see cref="HarlowePassage.Body"/> from the AST so the legacy string API
  /// keeps working alongside <see cref="HarlowePassage.Ast"/>.
  ///
  /// <para>Internal for the same reason as
  /// <see cref="BranchCollector"/> — implementation detail of populating the
  /// passage model from AST, not engine-facing.</para>
  /// </summary>
  internal class BodyTextRenderer : IBodyVisitor
  {
    private readonly StringBuilder _sb = new StringBuilder();

    public string Result => _sb.ToString();

    public void Visit(TextNode node) => _sb.Append(node.Content);
    public void Visit(NewlineNode node) => _sb.Append('\n');
    public void Visit(VariableNode node) => _sb.Append(node.IsTemporary ? '_' : '$').Append(node.Name);
    public void Visit(Ast.Body.HtmlNode node) => _sb.Append(node.RawHtml);
    public void Visit(LinkNode node) { }
    public void Visit(MacroNode node) { }

    public void Visit(HookNode node)
    {
      if (node.Children == null) return;
      foreach (var child in node.Children) child.Accept(this);
    }

    public void Visit(ChangerChainNode node)
    {
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    /// <summary>One-shot helper: walks <paramref name="ast"/> and returns the rendered text.</summary>
    public static string Render(PassageBody ast)
    {
      var visitor = new BodyTextRenderer();
      foreach (var child in ast.Children) child.Accept(visitor);
      return visitor.Result;
    }
  }
}
