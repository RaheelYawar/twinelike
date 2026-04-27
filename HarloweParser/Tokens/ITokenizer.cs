using System.Collections.Generic;

namespace Harlowe.Tokens
{
  /// <summary>
  /// Converts the raw text of a single passage body into a flat sequence of
  /// <see cref="Token"/>s. The tokenizer is responsible for distinguishing
  /// passage-body markup (text, hooks, links) from expression-context syntax
  /// (literals, operators, identifiers) that appears inside a macro's
  /// parentheses.
  /// </summary>
  public interface ITokenizer
  {
    /// <summary>
    /// Tokenize a passage body. The returned list is always terminated by an
    /// <see cref="TokenType.EndOfFile"/> token so consumers can scan without
    /// bounds-checking.
    /// </summary>
    /// <param name="passageBody">The inner text of a single <c>&lt;tw-passagedata&gt;</c> element.</param>
    IReadOnlyList<Token> Tokenize(string passageBody);
  }
}
