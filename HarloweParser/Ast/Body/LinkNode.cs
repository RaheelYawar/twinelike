namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A passage-to-passage link written with double-bracket syntax:
  /// <c>[[Just target]]</c>, <c>[[display text-&gt;target]]</c>, or
  /// <c>[[target&lt;-display text]]</c>. When <see cref="Text"/> is omitted in
  /// source, the parser sets it equal to <see cref="Target"/>.
  /// </summary>
  public class LinkNode : IBodyNode
  {
    /// <summary>The visible label shown to the player.</summary>
    public string Text;

    /// <summary>The name of the passage to navigate to when clicked.</summary>
    public string Target;

    /// <summary>
    /// 1-based line of the opening <c>[[</c> in the passage body, or <c>0</c>
    /// when unknown (a hand-built AST). Carried so a diagnostic can point the
    /// author at the link — see <see cref="Harlowe.GetBrokenLinks"/>.
    /// </summary>
    public int Line;

    /// <summary>1-based column of the opening <c>[[</c>; <c>0</c> when unknown.</summary>
    public int Column;

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);

    /// <summary>
    /// True when <paramref name="target"/> is exactly a variable reference —
    /// <c>$name</c> or <c>_name</c> — with the sigil and name split out.
    /// Reference evaluates markup in a link's passage-name position
    /// (<c>[[Go-&gt;$next]]</c>; <c>ts/macrolib/links.ts</c> renders the name and
    /// takes its text), so such a target is <em>computed</em>: the renderer
    /// resolves it through the store, and static link checks skip it.
    /// </summary>
    public static bool TryParseVariableTarget(string target, out bool isTemporary, out string name)
    {
      isTemporary = false;
      name = null;
      if (target == null || target.Length < 2) return false;
      char sigil = target[0];
      if (sigil != '$' && sigil != '_') return false;
      if (!char.IsLetter(target[1])) return false;
      for (int i = 2; i < target.Length; i++)
        if (!char.IsLetterOrDigit(target[i]) && target[i] != '_') return false;
      isTemporary = sigil == '_';
      name = target.Substring(1);
      return true;
    }
  }
}
