using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Tokens;

namespace Harlowe.Parsing
{
  /// <summary>
  /// Turns a flat token stream from <see cref="HarloweTokenizer"/> into a
  /// <see cref="PassageBody"/> tree. Implementations delegate to an
  /// <see cref="IExpressionParser"/> at every <see cref="TokenType.MacroOpen"/>
  /// so macro arguments come back as proper <see cref="Ast.Expression.IExpressionNode"/>
  /// trees rather than raw tokens.
  /// </summary>
  public interface IBodyParser
  {
    /// <summary>
    /// Build a <see cref="PassageBody"/> from a tokenized passage. The token
    /// list is expected to be terminated by <see cref="TokenType.EndOfFile"/>.
    /// </summary>
    PassageBody Parse(IReadOnlyList<Token> tokens);
  }
}
