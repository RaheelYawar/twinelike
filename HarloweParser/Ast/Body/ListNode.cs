using System.Collections.Generic;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A bulleted (<c>* item</c>) or numbered (<c>0. item</c>) list: reference's
  /// <c>bulleted</c> and <c>numbered</c> markup, which its renderer gathers
  /// into one <c>&lt;ul&gt;</c> or <c>&lt;ol&gt;</c> while consecutive lines
  /// carry the same kind of marker.
  /// </summary>
  public class ListNode : IBodyNode
  {
    /// <summary>True for a numbered list, false for a bulleted one.</summary>
    public bool Ordered;

    /// <summary>The items in order. Each carries its own depth.</summary>
    public List<ListItemNode> Items;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }

  /// <summary>One item of a <see cref="ListNode"/>.</summary>
  public class ListItemNode
  {
    /// <summary>How deeply the item nests: the number of <c>*</c>s, or of <c>0.</c>s.</summary>
    public int Depth;

    /// <summary>The marker as written, with the whitespace around it. Null on a hand-built item.</summary>
    public string Marker;

    /// <summary>What the item holds.</summary>
    public List<IBodyNode> Children;

    /// <summary>True when the line break that ends the item belongs to the list. It never renders.</summary>
    public bool EndsLine;
  }
}
