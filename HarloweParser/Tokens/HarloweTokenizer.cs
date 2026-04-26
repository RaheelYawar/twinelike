using System.Collections.Generic;

namespace Harlowe.Tokens
{
  /// <summary>
  /// Default <see cref="ITokenizer"/> implementation for Harlowe passage bodies.
  /// Operates as a mode stack: the outer mode is <see cref="TokenizerMode.Body"/>
  /// (prose-and-markup), and encountering <c>(name:</c> pushes
  /// <see cref="TokenizerMode.Expression"/> until the matching <c>)</c>. Macros
  /// can nest inside macro arguments, so the stack — not a single flag —
  /// tracks the current context.
  /// </summary>
  public class HarloweTokenizer : ITokenizer
  {
    private string _src;
    private int _pos;
    private int _line;
    private int _col;
    private List<Token> _tokens;

    private Stack<TokenizerMode> _modes;

    public IList<Token> Tokenize(string passageBody)
    {
      _src = passageBody ?? string.Empty;
      _pos = 0;
      _line = 1;
      _col = 1;
      _tokens = new List<Token>();
      _modes = new Stack<TokenizerMode>();
      _modes.Push(TokenizerMode.Body);

      // Implementation deferred — interface and shape only.
      _tokens.Add(new Token(TokenType.EndOfFile, string.Empty, _pos, _line, _col));
      return _tokens;
    }
  }

  /// <summary>
  /// Internal lexer state. Body mode emits prose/markup tokens; Expression
  /// mode emits literals/operators/etc. and is entered between a macro's
  /// opening colon and matching close paren.
  /// </summary>
  internal enum TokenizerMode
  {
    Body,
    Expression
  }
}
