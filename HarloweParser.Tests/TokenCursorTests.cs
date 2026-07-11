using Harlowe.Parsing;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Tests for <see cref="TokenCursor"/>'s conditional-advance helpers. The
  /// parsers drive the cursor through Current/Advance/Peek (covered by the
  /// parser suites); Match is the public convenience surface for external
  /// consumers walking a token stream.
  /// </summary>
  public class TokenCursorTests
  {
    private static TokenCursor Cursor(string source)
      => new TokenCursor(new HarloweTokenizer().Tokenize(source));

    [Fact]
    public void Match_TypeOnly_AdvancesOnlyOnMatch()
    {
      var c = Cursor("hello");
      Assert.False(c.Match(TokenType.MacroOpen)); // mismatch leaves the cursor put
      Assert.False(c.IsAtEnd);
      Assert.True(c.Match(TokenType.Text));       // consumes "hello"
      Assert.True(c.IsAtEnd);
    }

    [Fact]
    public void Match_TypeAndValue_RequiresBoth()
    {
      var c = Cursor("hello");
      Assert.False(c.Match(TokenType.Text, "nope")); // right type, wrong value
      Assert.False(c.IsAtEnd);
      Assert.True(c.Match(TokenType.Text, "hello"));
      Assert.True(c.IsAtEnd);
    }

    [Fact]
    public void Match_AtEndOfFile_IsFalse()
    {
      var c = Cursor("");
      Assert.True(c.IsAtEnd);
      Assert.False(c.Match(TokenType.Text));
      Assert.False(c.Match(TokenType.Text, "x"));
    }
  }
}
