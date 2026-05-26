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
    public void HtmlTag_AttributeWithLiteralGreaterThan_StaysInsideQuotes()
    {
      // A `>` inside a quoted attribute value isn't the tag close. The
      // scanner must track quote state so `<a title="1 > 0">x</a>` parses as
      // a single tag followed by prose and a closing tag.
      AssertSequence(Tokenize("<a title=\"1 > 0\">x</a>"),
        (TokenType.HtmlTag, "<a title=\"1 > 0\">"),
        (TokenType.Text, "x"),
        (TokenType.HtmlTag, "</a>"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HtmlTag_AttributeWithSingleQuotedGreaterThan_StaysInsideQuotes()
    {
      AssertSequence(Tokenize("<a title='2 > 1'>"),
        (TokenType.HtmlTag, "<a title='2 > 1'>"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HtmlTag_NonAsciiLead_DoesNotMatch_TreatedAsProse()
    {
      // WHATWG HTML restricts tag names to ASCII alpha. char.IsLetter would
      // also accept CJK / Cyrillic / Greek, which would silently scoop up
      // non-English prose like `<日本 ...>` as a malformed HtmlTag token.
      var tokens = Tokenize("<日本>x");
      // First token should be a `<` text fragment, not an HtmlTag.
      Assert.NotEqual(TokenType.HtmlTag, tokens[0].Type);
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
    public void Macro_EqualsSignAliasForTo()
    {
      // Reference Harlowe patterns.ts:1034: `to = either('to'+wb, '=')`.
      // A bare `=` lexes as the same `to` operator used by (set:)/(put:),
      // so `(set: $x = 1)` is identical to `(set: $x to 1)`.
      AssertSequence(Tokenize("(set: $x = 1)"),
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

    [Theory]
    [InlineData("(print: \"a\\nb\")", "a\nb")]
    [InlineData("(print: \"a\\tb\")", "a\tb")]
    [InlineData("(print: \"a\\rb\")", "a\rb")]
    [InlineData("(print: \"a\\\\b\")", "a\\b")]
    [InlineData("(print: \"a\\\"b\")", "a\"b")]
    [InlineData("(print: 'a\\'b')", "a'b")]
    [InlineData("(print: \"a\\0b\")", "a\0b")]
    [InlineData("(print: \"a\\bb\")", "a\bb")]
    public void StringLiteral_NamedEscape_Decoded(string source, string expectedValue)
    {
      // Reference Harlowe inherits the JS string-literal escape set via its
      // JS evaluator; we decode the same set in the tokenizer so the runtime
      // sees the cooked value.
      var tokens = Tokenize(source);
      Assert.Equal(TokenType.StringLiteral, tokens[1].Type);
      Assert.Equal(expectedValue, tokens[1].Value);
    }

    [Theory]
    [InlineData("(print: \"a\\x41b\")", "aAb")]
    [InlineData("(print: \"a\\x4Ab\")", "aJb")]
    [InlineData("(print: \"\\x00\")", "\0")]
    [InlineData("(print: \"\\u00e9\")", "é")]
    [InlineData("(print: \"\\u00E9\")", "é")]
    [InlineData("(print: \"\\uABCD\")", "ꯍ")]
    [InlineData("(print: \"\\u2603\")", "☃")]
    public void StringLiteral_HexAndUnicodeEscape_Decoded(string source, string expectedValue)
    {
      var tokens = Tokenize(source);
      Assert.Equal(TokenType.StringLiteral, tokens[1].Type);
      Assert.Equal(expectedValue, tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_UnknownEscape_PreservesBackslash()
    {
      // \q isn't a recognized escape. We keep the backslash verbatim so
      // legacy Twee source with literal backslashes (Windows paths, JSON-like
      // blobs) survives a read-trip.
      var tokens = Tokenize("(print: \"a\\qb\")");
      Assert.Equal("a\\qb", tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_MalformedHexEscape_PreservesBackslash()
    {
      // \xZZ — Z isn't a hex digit. The fallback now keeps the backslash so
      // the source representation round-trips intact.
      var tokens = Tokenize("(print: \"\\xZZ\")");
      Assert.Equal("\\xZZ", tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_BothQuoteTypesViaEscape_RoundTrips()
    {
      // The combo previously was unprintable; now it lexes cleanly because
      // the inner double quote is backslash-escaped.
      var tokens = Tokenize("(print: \"a\\\"b'c\")");
      Assert.Equal("a\"b'c", tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_HexEscape_InsufficientDigitsBeforeQuote_FallsBack()
    {
      // Only one hex digit before the closing quote — TryDecodeHex's length
      // check rejects, then the unknown-escape fallback keeps the backslash
      // and the `x`. The lone digit then tokenizes as a regular char.
      var tokens = Tokenize("(print: \"\\x4\")");
      Assert.Equal("\\x4", tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_UnicodeEscape_InsufficientDigitsBeforeQuote_FallsBack()
    {
      var tokens = Tokenize("(print: \"\\u12\")");
      Assert.Equal("\\u12", tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_TrailingBackslashAtEof_Preserved()
    {
      // Unterminated string ending on a bare backslash. DecodeEscape's
      // EOF guard now preserves the backslash; the unterminated string then
      // closes silently per ScanStringLiteral's policy.
      var tokens = Tokenize("(print: \"abc\\");
      Assert.Equal("abc\\", tokens[1].Value);
    }

    [Fact]
    public void StringLiteral_HexEscape_WhitespaceInDigits_FallsBack()
    {
      // \x followed by space + hex digit must NOT be parsed as a hex escape:
      // NumberStyles.HexNumber would accept the leading whitespace, which is
      // not the documented escape shape. TryDecodeHex uses AllowHexSpecifier
      // only, so this falls back to the verbatim-escape rule.
      var tokens = Tokenize("(print: \"\\x F\")");
      Assert.Equal("\\x F", tokens[1].Value);
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

    // --- Step 1 (tokenizer extension): operator surface coverage ---

    [Fact]
    public void PercentInBody_TreatedAsPlainText()
    {
      // Harlowe has no `%` operator (modulo is the (modulo:) macro). In body mode
      // it's just prose.
      AssertSequence(Tokenize("50% chance"),
        (TokenType.Text, "50% chance"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void PercentInExpression_NotEmittedAsOperator()
    {
      // `%` is no longer in the symbol-operator set; it's silently skipped in
      // expression mode like any other unrecognised char. The surrounding tokens
      // should still parse correctly.
      AssertSequence(Tokenize("(print: 5 % 2)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.NumberLiteral, "5"),
        (TokenType.NumberLiteral, "2"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Spread_TokenizedAsSingleOperator()
    {
      AssertSequence(Tokenize("(a: ...$xs)"),
        (TokenType.MacroOpen, "a"),
        (TokenType.Operator, "..."),
        (TokenType.Variable, "xs"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Theory]
    [InlineData("in")]
    [InlineData("matches")]
    [InlineData("where")]
    [InlineData("when")]
    [InlineData("via")]
    [InlineData("making")]
    [InlineData("each")]
    [InlineData("its")]
    [InlineData("bind")]
    public void NewSingleWordOperators_TokenizedAsOperator(string op)
    {
      AssertSequence(Tokenize("(print: " + op + ")"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Operator, op),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsNot_FusedAsSingleOperator()
    {
      AssertSequence(Tokenize("(if: $a is not 1)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is not"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsIn_FusedAsSingleOperator()
    {
      AssertSequence(Tokenize("(if: 1 is in $xs)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Operator, "is in"),
        (TokenType.Variable, "xs"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsA_FusedAsSingleOperator()
    {
      AssertSequence(Tokenize("(if: $a is a number)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is a"),
        (TokenType.Identifier, "number"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsNotIn_FusedAsThreeWordOperator()
    {
      AssertSequence(Tokenize("(if: 1 is not in $xs)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Operator, "is not in"),
        (TokenType.Variable, "xs"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsNotA_FusedAsThreeWordOperator()
    {
      AssertSequence(Tokenize("(if: $a is not a number)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is not a"),
        (TokenType.Identifier, "number"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void DoesNotContain_FusedAsThreeWordOperator()
    {
      AssertSequence(Tokenize("(if: $xs does not contain 1)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "xs"),
        (TokenType.Operator, "does not contain"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void DoesNotMatch_FusedAsThreeWordOperator()
    {
      AssertSequence(Tokenize("(if: $a does not match 1)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "does not match"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsFollowedByValue_StaysAsBareIs()
    {
      AssertSequence(Tokenize("(if: $a is 1)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void IsNotFollowedByNonExtender_StaysAsTwoWordIsNot()
    {
      // "is not 5" should fuse as "is not", not greedily try to consume "5".
      AssertSequence(Tokenize("(if: $a is not 5)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is not"),
        (TokenType.NumberLiteral, "5"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void DoesAlone_FallsBackToIdentifier()
    {
      // `does` is not a valid operator on its own; only `does not contain` and
      // `does not match` exist.
      AssertSequence(Tokenize("(print: does)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Identifier, "does"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void DoesNotFollowedByUnknown_FallsBackToSeparateTokens()
    {
      // "does not equal" isn't a real operator; should not fuse partially.
      AssertSequence(Tokenize("(print: does not equal)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Identifier, "does"),
        (TokenType.Operator, "not"),
        (TokenType.Identifier, "equal"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void MultiWordOperator_FusesAcrossNewlines()
    {
      AssertSequence(Tokenize("(if: $a is\n  not\n  1)"),
        (TokenType.MacroOpen, "if"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "is not"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Possessive_AfterVariable_TokenizedAsApostropheS()
    {
      AssertSequence(Tokenize("(print: $a's name)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "a"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "name"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Possessive_AfterClosingParen_TokenizedAsApostropheS()
    {
      AssertSequence(Tokenize("(print: (foo: 1)'s bar)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.MacroOpen, "foo"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "bar"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Possessive_RequiresNoLeadingWhitespace()
    {
      // `$a 's name'` has whitespace before `'`, so the `'` opens a string
      // literal rather than fusing with the `s`.
      AssertSequence(Tokenize("(print: $a 's name')"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "a"),
        (TokenType.StringLiteral, "s name"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void StringLiteralAtStartOfExpression_StillTokenized()
    {
      // Right after the `(name:` macro opener, `'foo'` should be a string
      // literal — there's no preceding value-like token to attach `'s` to.
      AssertSequence(Tokenize("(print: 'hello')"),
        (TokenType.MacroOpen, "print"),
        (TokenType.StringLiteral, "hello"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void TwoBind_TokenizedAsSingleOperator()
    {
      AssertSequence(Tokenize("(dialog: 2bind $x)"),
        (TokenType.MacroOpen, "dialog"),
        (TokenType.Operator, "2bind"),
        (TokenType.Variable, "x"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void BareTwo_StillTokenizedAsNumberLiteral()
    {
      // Make sure the `2bind` shortcut doesn't swallow a plain `2`.
      AssertSequence(Tokenize("(print: 2)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.NumberLiteral, "2"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void TypeSuffix_TokenizedAsSingleOperator()
    {
      AssertSequence(Tokenize("(set: num-type $x to 1)"),
        (TokenType.MacroOpen, "set"),
        (TokenType.Identifier, "num"),
        (TokenType.Operator, "-type"),
        (TokenType.Variable, "x"),
        (TokenType.Operator, "to"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void BareMinus_StillTokenizedAsOperator()
    {
      // Make sure the `-type` shortcut doesn't break ordinary subtraction.
      AssertSequence(Tokenize("(print: 5 - 2)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.NumberLiteral, "5"),
        (TokenType.Operator, "-"),
        (TokenType.NumberLiteral, "2"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_FirstSecondThirdFourth_TokenizedAsIdentifier()
    {
      // The four ordinal suffixes all collapse into a single Identifier token
      // (digit-led) so the evaluator can dispatch them through the property
      // accessor path.
      AssertSequence(Tokenize("(print: $arr's 1st)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "1st"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));

      AssertSequence(Tokenize("(print: $arr's 2nd)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "2nd"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));

      AssertSequence(Tokenize("(print: $arr's 3rd)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "3rd"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));

      AssertSequence(Tokenize("(print: $arr's 4th)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "4th"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_MultiDigit_TokenizedAsSingleIdentifier()
    {
      AssertSequence(Tokenize("(print: $arr's 100th)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "100th"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_NthLast_TokenizedAsSingleIdentifier()
    {
      // `2ndlast` has to be one token, not `2nd` + `last`, so that the
      // evaluator's identifier accessor sees the full back-anchored ordinal.
      AssertSequence(Tokenize("(print: $arr's 2ndlast)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "2ndlast"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_BareLast_TokenizedAsIdentifier()
    {
      // `last` is letter-led — the normal identifier path handles it without
      // any special casing. This test locks the behaviour the resolver relies
      // on.
      AssertSequence(Tokenize("(print: $arr's last)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "last"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_TrailingLetters_NotFused()
    {
      // `1stnope` is not an ordinal — the trailing identifier-continuation
      // chars disqualify it. The tokenizer falls back to the number-literal
      // path so the result is `1` + `stnope`.
      AssertSequence(Tokenize("(print: 1stnope)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.Identifier, "stnope"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_BareDigit_StillTokenizedAsNumberLiteral()
    {
      // The ordinal shortcut must not swallow plain numeric indexing.
      AssertSequence(Tokenize("(print: $arr's 1)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Variable, "arr"),
        (TokenType.Operator, "'s"),
        (TokenType.NumberLiteral, "1"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void Ordinal_OfForm_TokenizedAsIdentifier()
    {
      // `1st of $arr` — same Identifier token, just on the other side of the
      // accessor operator.
      AssertSequence(Tokenize("(print: 1st of $arr)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.Identifier, "1st"),
        (TokenType.Operator, "of"),
        (TokenType.Variable, "arr"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    // --- Hook references (?name) ---

    [Fact]
    public void HookRef_InExpression_TokenizedWithoutSigil()
    {
      AssertSequence(Tokenize("(replace: ?cake)"),
        (TokenType.MacroOpen, "replace"),
        (TokenType.HookRef, "cake"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HookRef_BuiltInName_TokenizedLikeAnyName()
    {
      AssertSequence(Tokenize("(enchant: ?passage)"),
        (TokenType.MacroOpen, "enchant"),
        (TokenType.HookRef, "passage"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HookRef_WithOrdinalPossessive_KeepsApostropheSOperator()
    {
      // The `'s` must fuse as the possessive operator after a HookRef, not be
      // mistaken for a string-literal opener.
      AssertSequence(Tokenize("(replace: ?cake's 1st)"),
        (TokenType.MacroOpen, "replace"),
        (TokenType.HookRef, "cake"),
        (TokenType.Operator, "'s"),
        (TokenType.Identifier, "1st"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HookRef_InBodyProse_StaysPlainText()
    {
      // `?` is expression-context markup only — in prose it is ordinary text.
      AssertSequence(Tokenize("Really?"),
        (TokenType.Text, "Really?"),
        (TokenType.EndOfFile, ""));
    }

    [Fact]
    public void HookRef_BareQuestionMark_NotAHookRef()
    {
      // `?` not followed by a letter inside an expression produces no token.
      AssertSequence(Tokenize("(print: ?)"),
        (TokenType.MacroOpen, "print"),
        (TokenType.MacroClose, ")"),
        (TokenType.EndOfFile, ""));
    }
  }
}
