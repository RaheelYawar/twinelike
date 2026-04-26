namespace Harlowe.Tokens
{
  public class Token
  {
    public TokenType Type;
    public string Value;
    public int Position;
    public int Line;
    public int Column;

    public Token(TokenType type, string value, int position, int line, int column)
    {
      Type = type;
      Value = value;
      Position = position;
      Line = line;
      Column = column;
    }

    public override string ToString() => $"{Type}({Value}) @ {Line}:{Column}";
  }
}
