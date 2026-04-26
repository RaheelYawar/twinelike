using System.Collections.Generic;

namespace Harlowe.Tokens
{
  public class HarloweTokenizer : ITokenizer
  {
    private string _src;
    private int _pos;
    private int _line;
    private int _col;
    private List<Token> _tokens;

    // Stack of contexts: passage-body emits Text/Variable/MacroOpen/HookOpen/LinkOpen;
    // expression emits literals, identifiers, operators, nested MacroOpen until matching ParenClose.
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

  internal enum TokenizerMode
  {
    Body,
    Expression
  }
}
