namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A bare variable reference appearing inline in passage prose, e.g.
  /// <c>Hello $name.</c> The renderer looks up the variable's current value
  /// in the runtime store and prints its string form.
  /// </summary>
  public class VariableNode : IBodyNode
  {
    /// <summary>The variable name without its sigil (no leading <c>$</c> or <c>_</c>).</summary>
    public string Name;

    /// <summary>
    /// True if this is a temporary (passage-scoped) variable — written
    /// <c>_name</c> in source. Temporaries are reset between passages;
    /// non-temporaries persist for the whole story.
    /// </summary>
    public bool IsTemporary;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
