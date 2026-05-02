using System.Collections.Generic;
using Harlowe.Ast.Expression;

namespace Harlowe.Parsing
{
  /// <summary>
  /// Parses tokens from a shared <see cref="TokenCursor"/> into an
  /// <see cref="IExpressionNode"/> tree. Called by the body parser whenever a
  /// macro's argument list needs to be consumed; can also be driven directly
  /// by tests that want to assert on a single expression.
  /// </summary>
  public interface IExpressionParser
  {
    /// <summary>
    /// Parse one expression at the lowest precedence level (operator order 16,
    /// <c>to</c>/<c>into</c>). Stops at the first token that does not extend
    /// the current expression — typically a <c>,</c>, <c>)</c>, or end-of-file.
    /// The cursor is left pointing at that terminator.
    /// </summary>
    IExpressionNode ParseExpression(TokenCursor cursor);

    /// <summary>
    /// Parse a comma-separated list of expressions terminated by
    /// <see cref="Tokens.TokenType.MacroClose"/>. Used by the body parser at
    /// every <see cref="Tokens.TokenType.MacroOpen"/> to fill
    /// <see cref="Ast.Body.MacroNode.Arguments"/> (and by
    /// <see cref="MacroCallNode.Arguments"/> for nested macro calls). Consumes
    /// the terminating <c>MacroClose</c>; the cursor is left just past it.
    /// </summary>
    List<IExpressionNode> ParseArgumentList(TokenCursor cursor);
  }
}
