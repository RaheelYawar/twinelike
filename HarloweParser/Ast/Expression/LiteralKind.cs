namespace Harlowe.Ast.Expression
{
  /// <summary>The runtime type carried by a <see cref="LiteralNode"/>.</summary>
  public enum LiteralKind
  {
    /// <summary><c>"text"</c> in source.</summary>
    String,
    /// <summary><c>42</c>, <c>3.14</c>, etc.</summary>
    Number,
    /// <summary><c>true</c> or <c>false</c>.</summary>
    Bool,
    /// <summary>
    /// A colour literal — a built-in name (<c>red</c>) or hex form
    /// (<c>#a4e</c>). <see cref="LiteralNode.Value"/> holds the raw lexeme
    /// <see cref="string"/> so printing round-trips verbatim; the evaluator
    /// converts to a <c>ColourValue</c> on evaluation.
    /// </summary>
    Colour,
    /// <summary>
    /// A datatype name — <c>num</c>, <c>string</c>, <c>even</c>.
    /// <see cref="LiteralNode.Value"/> holds the raw lexeme
    /// <see cref="string"/> so printing round-trips the author's spelling
    /// (<c>number</c> stays <c>number</c>); the evaluator canonicalises it into
    /// a <c>DatatypeValue</c>.
    /// </summary>
    Datatype
  }
}
