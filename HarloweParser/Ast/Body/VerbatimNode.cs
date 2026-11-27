namespace Harlowe.Ast.Body
{
  /// <summary>
  /// A <c>`…`</c> span, whose contents print exactly as written with no markup
  /// read inside them (reference's <c>verbatim</c> token). Inside <c>{…}</c>
  /// its whitespace is kept.
  /// </summary>
  public class VerbatimNode : IBodyNode
  {
    /// <summary>The characters between the delimiters.</summary>
    public string Text;

    /// <summary>The run of graves on each side, kept so the span can be written back out.</summary>
    public string Delimiter = "`";

    public void Accept(IBodyVisitor visitor) => visitor.Visit(this);
  }
}
