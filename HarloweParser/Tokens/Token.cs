namespace Harlowe.Tokens
{
  /// <summary>
  /// A single lexical unit produced by an <see cref="ITokenizer"/>. Carries
  /// source position information so the parser can report diagnostics that
  /// point back to the original passage text.
  /// </summary>
  public class Token
  {
    /// <summary>The category of this token.</summary>
    public TokenType Type;

    /// <summary>
    /// The raw text or extracted value for this token. For markers like
    /// <see cref="TokenType.HookOpen"/> this may be empty; for
    /// <see cref="TokenType.Variable"/> it is the name without the leading sigil.
    /// </summary>
    public string Value;

    /// <summary>Zero-based character offset into the original passage body.</summary>
    public int Position;

    /// <summary>One-based line number where this token starts.</summary>
    public int Line;

    /// <summary>One-based column number where this token starts.</summary>
    public int Column;

    /// <summary>
    /// Constructs a token with its category, value, and source position. All
    /// fields are public for direct mutation, but tokenizers should treat
    /// emitted tokens as immutable.
    /// </summary>
    public Token(TokenType type, string value, int position, int line, int column)
    {
      Type = type;
      Value = value;
      Position = position;
      Line = line;
      Column = column;
    }

    /// <summary>
    /// Debug-friendly representation of the form <c>Type(Value) @ Line:Column</c>.
    /// Intended for diagnostics, not serialisation.
    /// </summary>
    public override string ToString() => $"{Type}({Value}) @ {Line}:{Column}";
  }
}
