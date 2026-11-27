using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe.Ast.Expression
{
  /// <summary>
  /// A code hook: markup between <c>[</c> and <c>]</c> in macro-argument
  /// position, as in <c>(macro: [(output-data: 1)])</c>. Reference's
  /// <c>ts/datatypes/codehook.ts</c>. The contents are ordinary body nodes, and
  /// <see cref="Source"/> keeps the text including both brackets, which is what
  /// a code hook compares and saves by.
  /// </summary>
  public class CodeHookNode : IExpressionNode
  {
    public List<IBodyNode> Children = new List<IBodyNode>();

    /// <summary>The hook as written, brackets included. Null when the parser had no source text.</summary>
    public string Source;

    public void Accept(IExpressionVisitor visitor) => visitor.Visit(this);
  }
}
