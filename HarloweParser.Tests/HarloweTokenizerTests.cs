using System.Collections.Generic;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests
{
  public class HarloweTokenizerTests
  {
    /// <summary>
    /// Convenience wrapper: instantiates a fresh <see cref="HarloweTokenizer"/>
    /// per call so tests don't share state.
    /// </summary>
    private static IReadOnlyList<Token> Tokenize(string src)
    {
      return new HarloweTokenizer().Tokenize(src);
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> contains exactly the
    /// (type, value) pairs in <paramref name="expected"/>, in order. Compares
    /// only Type and Value — line and column are checked separately by the
    /// position-tracking tests.
    /// </summary>
    private static void AssertSequence(IReadOnlyList<Token> actual, params (TokenType Type, string Value)[] expected)
    {
      Assert.Equal(expected.Length, actual.Count);
      for (int i = 0; i < expected.Length; i++)
      {
        Assert.Equal(expected[i].Type, actual[i].Type);
        Assert.Equal(expected[i].Value, actual[i].Value);
      }
    }

    [Fact]
    public void EmptyInput_EmitsOnlyEof()
    {
      var tokens = Tokenize("");
      Assert.Single(tokens);
      Assert.Equal(TokenType.EndOfFile, tokens[0].Type);
    }

    [Fact]
    public void NullInput_EmitsOnlyEof()
    {
      var tokens = Tokenize(null);
      Assert.Single(tokens);
      Assert.Equal(TokenType.EndOfFile, tokens[0].Type);
    }

    [Fact]
    public void PlainText_EmitsSingleTextToken()
    {
      AssertSequence(Tokenize("hello world"),
        (TokenType.Text, "hello world"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Newline_EmitsNewlineToken()
    {
      AssertSequence(Tokenize("a\nb"),
        (TokenType.Text, "a"),
        (TokenType.Newline, "\n"),
        (TokenType.Text, "b"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Variable_StripsLeadingDollar()
    {
      AssertSequence(Tokenize("$hp"),
        (TokenType.Variable, "hp"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void TempVariable_StripsLeadingUnderscore()
    {
      AssertSequence(Tokenize("_loop"),
        (TokenType.TempVariable, "loop"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void DollarWithoutLetter_FallsBackToText()
    {
      AssertSequence(Tokenize("$5"),
        (TokenType.Text, "$5"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void BareUnderscore_FallsBackToText()
    {
      AssertSequence(Tokenize("_ ok"),
        (TokenType.Text, "_ ok"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Link_WithRightArrow_TokenizesTextDestinationAndArrow()
    {
      AssertSequence(Tokenize("[[Continue->Next]]"),
        (TokenType.LinkOpen, "[["),
        (TokenType.Text, "Continue"),
        (TokenType.LinkArrowRight, "->"),
        (TokenType.Text, "Next"),
        (TokenType.LinkClose, "]]"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Link_WithLeftArrow_TokenizesArrowAsLinkArrowLeft()
    {
      AssertSequence(Tokenize("[[Next<-Continue]]"),
        (TokenType.LinkOpen, "[["),
        (TokenType.Text, "Next"),
        (TokenType.LinkArrowLeft, "<-"),
        (TokenType.Text, "Continue"),
        (TokenType.LinkClose, "]]"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Link_BareTarget_HasNoArrow()
    {
      AssertSequence(Tokenize("[[Next]]"),
        (TokenType.LinkOpen, "[["),
        (TokenType.Text, "Next"),
        (TokenType.LinkClose, "]]"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Hook_OpenAndClose_TokenizedAsHookBrackets()
    {
      AssertSequence(Tokenize("[hello]"),
        (TokenType.HookOpen, "["),
        (TokenType.Text, "hello"),
        (TokenType.HookClose, "]"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HookNameLeft_RecognisedAsSingleToken()
    {
      AssertSequence(Tokenize("<name|"),
        (TokenType.HookNameLeft, "name"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HookNameRight_RecognisedAsSingleToken()
    {
      AssertSequence(Tokenize("|name>"),
        (TokenType.HookNameRight, "name"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HtmlTag_PassesThroughVerbatim()
    {
      AssertSequence(Tokenize("<br/>"),
        (TokenType.HtmlTag, "<br/>"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HtmlTag_WithAttributes_PreservesEntireTag()
    {
      AssertSequence(Tokenize("<a href=\"x\">"),
        (TokenType.HtmlTag, "<a href=\"x\">"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HtmlTag_ClosingTag_PreservesEntireTag()
    {
      AssertSequence(Tokenize("</p>"),
        (TokenType.HtmlTag, "</p>"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_BasicSet_PushesIntoExpressionMode()
    {
      AssertSequence(Tokenize("(set: $x to 1)"),
        (TokenType.MacroOpen, "set"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_DoubleQuotedString_TokenizedAsStringLiteral()
    {
      AssertSequence(Tokenize("(print: \"hello\")"),
        (TokenType.MacroOpen, "print"),
        (TokenType.StringLiteral, "hello"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_SingleQuotedString_TokenizedAsStringLiteral()
    {
      AssertSequence(Tokenize("(print: 'hi')"),
        (TokenType.MacroOpen, "print"),
        (TokenType.StringLiteral, "hi"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_DecimalNumberLiteral_PreservesDecimalPoint()
    {
      AssertSequence(Tokenize("(set: $x to 1.5)"),
        (TokenType.MacroOpen, "set"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.NumberLiteral, "1.5"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_TrueLiteral_TokenizedAsBool()
    {
      AssertSequence(Tokenize("(if: true)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.BoolLiteral, "true"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_FalseLiteral_TokenizedAsBool()
    {
      AssertSequence(Tokenize("(if: false)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.BoolLiteral, "false"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_CommaSeparatesArguments()
    {
      AssertSequence(Tokenize("(print: 1, 2)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Comma, ","),
        (TokenType.NumberLiteral, "2"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_NestedGroupingParen_DoesNotPopFrame()
    {
      AssertSequence(Tokenize("(set: $x to (1 + 2))"),
        (TokenType.MacroOpen, "set"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.ParenOpen, "("),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Operator, "+"),
        (TokenType.NumberLiteral, "2"),
        (TokenType.ParenClose, ")"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_NestedMacroCall_PushesAnotherExpressionFrame()
    {
      AssertSequence(Tokenize("(set: $x to (random: 1, 6))"),
        (TokenType.MacroOpen, "set"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.MacroOpen, "random"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Comma, ","),
        (TokenType.NumberLiteral, "6"),
        (TokenType.MacroClose, ")"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_WordOperators_TokenizedAsOperator()
    {
      AssertSequence(Tokenize("(if: $a is 1 and $b)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Operator, "and"),
        (TokenType.Variable, "b"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_TwoCharSymbolOperator_TokenizedAsSingleToken()
    {
      AssertSequence(Tokenize("(if: $a >= 1)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, ">="),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_BracketsInExpression_TokenizedAsBracketTokens()
    {
      AssertSequence(Tokenize("(print: $a[1])"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "a"),
        (TokenType.BracketOpen, "["),
        (TokenType.NumberLiteral, "1"),
        (TokenType.BracketClose, "]"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_TempVariableInExpression_TokenizedAsTempVariable()
    {
      AssertSequence(Tokenize("(set: _x to 1)"),
        (TokenType.MacroOpen, "set"),
        (TokenType.TempVariable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Macro_BareIdentifier_TokenizedAsIdentifier()
    {
      AssertSequence(Tokenize("(print: time)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Identifier, "time"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void TextSurroundingMacro_EmitsTextAndMacroTokens()
    {
      AssertSequence(Tokenize("Hello (set: $x to 1) world"),
        (TokenType.Text, "Hello "),
        (TokenType.MacroOpen, "set"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.Text, " world"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void OpenParenWithoutMacroName_TreatedAsText()
    {
      AssertSequence(Tokenize("(hello)"),
        (TokenType.Text, "(hello)"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Position_TracksLineAndColumnAcrossNewline()
    {
      var tokens = Tokenize("ab\ncd");
      Assert.Equal(1, tokens[0].Line);
      Assert.Equal(1, tokens[0].Column);
      Assert.Equal(1, tokens[1].Line);
      Assert.Equal(3, tokens[1].Column);
      Assert.Equal(2, tokens[2].Line);
      Assert.Equal(1, tokens[2].Column);
    }

    [Fact]
    public void HyphenatedMacroName_AllowedInMacroOpen()
    {
      AssertSequence(Tokenize("(for-each: 1)"),
        (TokenType.MacroOpen, "for-each"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void MacroInsideHook_TokenizedNormally()
    {
      AssertSequence(Tokenize("[hi (set: $x to 1)]"),
        (TokenType.HookOpen, "["),
        (TokenType.Text, "hi "),
        (TokenType.MacroOpen, "set"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.HookClose, "]"),
        (TokenType.EndOfFile, ""));
    }
  }
}
