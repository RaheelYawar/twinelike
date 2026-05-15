using System.Collections.Generic;

namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A hook reference — <c>?name</c> — appearing as a value inside a macro
  /// argument (<c>(replace: ?cake)</c>, <c>(set: $x to ?cake)</c>). Distinct
  /// from a hook <em>declaration</em> (<c>|name&gt;[...]</c> /
  /// <c>[...]&lt;name|</c>), which is body markup: a <see cref="HookRefNode"/>
  /// is a query the runtime resolves against rendered content.
  ///
  /// <para>
  /// <see cref="Steps"/> carries the ordinal narrowing chained with <c>'s</c> —
  /// <c>?cake's 1st</c>, <c>?cake's last</c>. It is empty for a bare
  /// <c>?name</c>. Built-in names (<c>?page</c>, <c>?passage</c>, <c>?link</c>)
  /// are ordinary names at this layer; resolution decides what they select.
  /// </para>
  /// </summary>
  public class HookRefNode : IExpressionNode
  {
    /// <summary>The hook name without the leading <c>?</c>.</summary>
    public string Name;

    /// <summary>Ordinal narrowing steps from chained <c>'s</c> accessors. Never null; empty for a bare reference.</summary>
    public List<HookRefStep> Steps = new List<HookRefStep>();

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }

  /// <summary>
  /// One narrowing step applied to a hook reference — the ordinal accessors
  /// authors chain with <c>'s</c> (<c>?cake's 1st</c>, <c>?cake's last</c>,
  /// <c>?cake's 2ndlast</c>). <see cref="Index"/> is 1-based; when
  /// <see cref="FromEnd"/> is true the index counts back from the last match
  /// (<c>last</c> is <c>Index = 1, FromEnd = true</c>).
  /// </summary>
  public class HookRefStep
  {
    public int Index;
    public bool FromEnd;

    public override bool Equals(object obj)
      => obj is HookRefStep other && other.Index == Index && other.FromEnd == FromEnd;

    public override int GetHashCode() => (Index * 397) ^ (FromEnd ? 1 : 0);
  }
}
