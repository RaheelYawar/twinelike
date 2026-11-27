using System.Collections.Generic;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A span of collapsing whitespace: <c>{…}</c>, or <c>{=</c> running to the
  /// end of the enclosing hook or passage (reference's <c>collapsed</c> and
  /// <c>unclosedCollapsed</c> tokens). Inside it, runs of whitespace in prose
  /// become single spaces and the span's edges are trimmed. Text printed by a
  /// macro or variable keeps its whitespace.
  /// </summary>
  public class CollapsedNode : IBodyNode
  {
    /// <summary>The body nodes inside the span.</summary>
    public List<IBodyNode> Children;

    /// <summary>The unclosed marker as written (<c>{=</c>, <c>{==</c>, …), or null for a matched <c>{…}</c> pair.</summary>
    public string UnclosedMarker;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
