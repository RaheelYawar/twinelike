using System.Collections.Generic;
using Harlowe.Tokens;

namespace Harlowe.Parsing
{
  /// <summary>
  /// A forward-moving cursor over a flat token stream. Shared by the body and
  /// expression parsers so they can hand control back and forth (e.g. the body
  /// parser sees <see cref="TokenType.MacroOpen"/> and asks the expression
  /// parser to consume tokens until <see cref="TokenType.MacroClose"/>).
  ///
  /// <para>
  /// Both parsers operate on the same cursor instance: <see cref="Current"/>
  /// always reflects the next unconsumed token; <see cref="Advance"/> returns
  /// it and steps forward. The stream is expected to terminate in
  /// <see cref="TokenType.EndOfFile"/> so consumers can inspect <c>Current</c>
  /// without bounds-checking.
  /// </para>
  /// </summary>
  public class TokenCursor
  {
    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _source;
    private int _pos;

    public TokenCursor(IReadOnlyList<Token> tokens) : this(tokens, null) { }

    /// <summary>
    /// Constructs a cursor with the original source text the tokens were
    /// produced from. The source is consulted by <see cref="SliceFrom"/> so
    /// parser-level recovery can capture the broken sub-source for a failed
    /// node. Pass null when the source isn't available — slicing then returns
    /// null, matching the legacy behaviour.
    /// </summary>
    public TokenCursor(IReadOnlyList<Token> tokens, string source)
    {
      _tokens = tokens;
      _source = source;
      _pos = 0;
    }

    /// <summary>The next unconsumed token. Returns the EOF token at end-of-stream.</summary>
    public Token Current => _tokens[_pos];

    /// <summary>True if the cursor is at the end-of-file sentinel.</summary>
    public bool IsAtEnd => _tokens[_pos].Type == TokenType.EndOfFile;

    /// <summary>
    /// Returns the token <paramref name="offset"/> positions ahead without moving
    /// the cursor. Returns the EOF token if the offset is past the end.
    /// </summary>
    public Token Peek(int offset = 1)
    {
      int i = _pos + offset;
      return i < _tokens.Count ? _tokens[i] : _tokens[_tokens.Count - 1];
    }

    /// <summary>Returns the current token and advances the cursor.</summary>
    public Token Advance()
    {
      var t = _tokens[_pos];
      if (_pos < _tokens.Count - 1) _pos++;
      return t;
    }

    /// <summary>
    /// If the current token's type matches <paramref name="type"/>, advances
    /// past it and returns true. Otherwise leaves the cursor in place and
    /// returns false.
    /// </summary>
    public bool Match(TokenType type)
    {
      if (_tokens[_pos].Type != type) return false;
      Advance();
      return true;
    }

    /// <summary>
    /// If the current token has type <paramref name="type"/> and the given
    /// <paramref name="value"/>, advances past it and returns true.
    /// </summary>
    public bool Match(TokenType type, string value)
    {
      var t = _tokens[_pos];
      if (t.Type != type || t.Value != value) return false;
      Advance();
      return true;
    }

    /// <summary>
    /// Slice the original source from <paramref name="startPos"/> (a token's
    /// <see cref="Token.Position"/>) up to the current cursor token's
    /// position. Returns null when the cursor was constructed without a
    /// source string, or when the range is out of bounds. Used by per-node
    /// parse-error recovery to capture the broken sub-source for AST
    /// round-tripping — tokens alone can't reconstruct it because the
    /// tokenizer drops whitespace and punctuation that don't surface in any
    /// <see cref="Token.Value"/>.
    /// </summary>
    public string SliceFrom(int startPos)
    {
      if (_source == null) return null;
      int endPos = _tokens[_pos].Position;
      if (startPos < 0 || endPos <= startPos || endPos > _source.Length) return null;
      return _source.Substring(startPos, endPos - startPos);
    }
  }
}
