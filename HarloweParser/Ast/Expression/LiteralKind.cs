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
    Bool
  }
}
