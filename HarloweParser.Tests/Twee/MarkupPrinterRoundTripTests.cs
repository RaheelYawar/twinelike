using System.IO;
using Harlowe.Ast.Body;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Twee
{
  /// <summary>
  /// Round-trip property tests for <see cref="MarkupPrinter"/>: parsing a
  /// Harlowe source string and printing the resulting AST should produce a
  /// string that re-parses to the same shape (we check this via
  /// printed-equality — re-printing the second AST yields the same text).
  /// </summary>
  public class MarkupPrinterRoundTripTests
  {
    private static PassageBody ParseBody(string source)
    {
      var tokens = new HarloweTokenizer().Tokenize(source);
      return new HarloweBodyParser().Parse(tokens);
    }

    private static IExpressionNode ParseExpression(string source)
    {
      // Wrap the expression in (set: ...) so the body parser allows top-level
      // `to`/`into` assignment forms (the round-trip suite covers both). Other
      // expression shapes parse the same way whether the wrapper is (print:)
      // or (set:), so a single wrapper covers every test case.
      var tokens = new HarloweTokenizer().Tokenize("(set: " + source + ")");
      var ast = new HarloweBodyParser().Parse(tokens);
      var macro = (Ast.Body.MacroNode)ast.Children[0];
      return macro.Arguments[0];
    }

    private static string PrintBody(string source) => new MarkupPrinter().Print(ParseBody(source));
    private static string PrintExpr(string source) => new MarkupPrinter().Print(ParseExpression(source));

    private static void AssertBodyRoundTripStable(string source)
    {
      string first = PrintBody(source);
      string second = PrintBody(first);
      Assert.Equal(first, second);
    }

    private static void AssertExprRoundTripStable(string source)
    {
      string first = PrintExpr(source);
      string second = PrintExpr(first);
      Assert.Equal(first, second);
    }

    // ----- Body round-trips -----

    [Fact] public void Body_Plain() => AssertBodyRoundTripStable("Hello world.");
    [Fact] public void Body_WithVariable() => AssertBodyRoundTripStable("Hello $name.");
    [Fact] public void Body_WithTempVar() => AssertBodyRoundTripStable("Loop _i times.");
    [Fact] public void Body_MultiLine() => AssertBodyRoundTripStable("Line one\nLine two\n\nLine three");
    [Fact] public void Body_SimpleLink() => AssertBodyRoundTripStable("Go [[Next]].");
    [Fact] public void Body_LinkWithText() => AssertBodyRoundTripStable("Click [[here->Next]] now.");
    [Fact] public void Body_ReverseLink_Canonicalizes() => AssertBodyRoundTripStable("Click [[Next<-here]] now.");
    [Fact] public void Body_AnonymousHook() => AssertBodyRoundTripStable("Some [content] in prose.");
    [Fact] public void Body_NamedHook_Right() => AssertBodyRoundTripStable("|greeting>[hello]");
    [Fact] public void Body_NamedHook_Left() => AssertBodyRoundTripStable("[hello]<greeting|");
    [Fact] public void Body_BareMacro() => AssertBodyRoundTripStable("(set: $hp to 10)");
    [Fact] public void Body_MacroWithHook() => AssertBodyRoundTripStable("(if: $x)[yes]");
    [Fact] public void Body_ReplaceHookRef() => AssertBodyRoundTripStable("(replace: ?cake)[new]");
    [Fact] public void Body_AppendStringTarget() => AssertBodyRoundTripStable("(append: \"old\")[ more]");
    [Fact] public void Body_PrependOrdinalTarget() => AssertBodyRoundTripStable("(prepend: ?item's last)[first ]");
    [Fact] public void Body_ChangeMacro() => AssertBodyRoundTripStable("(change: ?cake, (text-style: \"bold\"))");
    [Fact] public void Body_EnchantPage() => AssertBodyRoundTripStable("(enchant: ?page, (text-style: \"italic\"))");
    [Fact] public void Body_ClickHookTarget() => AssertBodyRoundTripStable("(click: ?cake)[surprise]");
    [Fact] public void Body_ClickAppend() => AssertBodyRoundTripStable("(click-append: ?m)[ more]");
    [Fact] public void Body_MouseOverPrepend() => AssertBodyRoundTripStable("(mouseover-prepend: ?m)[before ]");
    [Fact] public void Body_NestedHook() => AssertBodyRoundTripStable("(if: $x)[outer (if: $y)[inner]]");
    [Fact] public void Body_HtmlPassthrough() => AssertBodyRoundTripStable("Hello <b>world</b>!");
    [Fact] public void Body_MixedShape()
      => AssertBodyRoundTripStable("Hello $name.\n(if: $found)[You see [[Cave]].]\n");

    [Fact]
    public void Body_MacroWithSpacingBeforeHook_Canonicalizes()
    {
      // Whitespace between `)` and `[` is insignificant — the parser attaches
      // the hook either way, so the printer collapses the gap.
      string canon = PrintBody("(if: $x)[yes]");
      string spaced = PrintBody("(if: $x)   [yes]");
      string newlined = PrintBody("(if: $x)\n[yes]");
      Assert.Equal(canon, spaced);
      Assert.Equal(canon, newlined);
    }

    [Fact]
    public void Body_LinkArrowDirection_Canonicalizes()
    {
      // Both `->` and `<-` arrow forms produce identical AST, so the printer
      // emits a single canonical form (`->`).
      Assert.Equal(PrintBody("[[here->Next]]"), PrintBody("[[Next<-here]]"));
    }

    // ----- Expression round-trips -----

    [Fact] public void Expr_Number() => AssertExprRoundTripStable("42");
    [Fact] public void Expr_String() => AssertExprRoundTripStable("\"hello\"");
    [Fact] public void Expr_String_WithEscapes() => AssertExprRoundTripStable("\"line1\\nline2\\twith\\ttabs\\\\backslash\"");
    [Fact] public void Expr_String_BothQuoteTypes() => AssertExprRoundTripStable("\"a\\\"b'c\"");
    [Fact] public void Expr_String_HexAndUnicodeEscape() => AssertExprRoundTripStable("\"\\u2603 \\x41\"");
    [Fact] public void Expr_Bool() => AssertExprRoundTripStable("true");
    [Fact] public void Expr_Variable() => AssertExprRoundTripStable("$hp");
    [Fact] public void Expr_TempVariable() => AssertExprRoundTripStable("_i");
    [Fact] public void Expr_Identifier() => AssertExprRoundTripStable("it");
    [Fact] public void Expr_HookRef() => AssertExprRoundTripStable("?cake");
    [Fact] public void Expr_HookRefForwardOrdinal() => AssertExprRoundTripStable("?cake's 1st");
    [Fact] public void Expr_HookRefLast() => AssertExprRoundTripStable("?cake's last");
    [Fact] public void Expr_HookRefNthLast() => AssertExprRoundTripStable("?cake's 2ndlast");
    [Fact] public void Expr_HookRefChained() => AssertExprRoundTripStable("?cake's 1st's last");
    [Fact] public void Expr_Add() => AssertExprRoundTripStable("$a + $b");
    [Fact] public void Expr_Multiply() => AssertExprRoundTripStable("$a * $b");
    [Fact] public void Expr_PrecedenceMul() => AssertExprRoundTripStable("$a + $b * $c");
    [Fact] public void Expr_PrecedenceParens() => AssertExprRoundTripStable("($a + $b) * $c");
    [Fact] public void Expr_PossessiveChain() => AssertExprRoundTripStable("$arr's name");
    [Fact] public void Expr_OfChain() => AssertExprRoundTripStable("name of $arr");
    [Fact] public void Expr_PossessiveOrdinal() => AssertExprRoundTripStable("$arr's 1st");
    [Fact] public void Expr_PossessiveNthLast() => AssertExprRoundTripStable("$arr's 2ndlast");
    [Fact] public void Expr_PossessiveLast() => AssertExprRoundTripStable("$arr's last");
    [Fact] public void Expr_Comparison() => AssertExprRoundTripStable("$hp >= 10");
    [Fact] public void Expr_IsEqual() => AssertExprRoundTripStable("$x is 5");
    [Fact] public void Expr_IsNot() => AssertExprRoundTripStable("$x is not 5");
    [Fact] public void Expr_Logical() => AssertExprRoundTripStable("$brave and $found");
    [Fact] public void Expr_LogicalChain() => AssertExprRoundTripStable("$a or $b and $c");
    [Fact] public void Expr_Contains() => AssertExprRoundTripStable("$arr contains 5");
    [Fact] public void Expr_IsIn() => AssertExprRoundTripStable("5 is in $arr");
    [Fact] public void Expr_IsNotIn() => AssertExprRoundTripStable("5 is not in $arr");
    [Fact] public void Expr_DoesNotContain() => AssertExprRoundTripStable("$arr does not contain 5");
    [Fact] public void Expr_Not() => AssertExprRoundTripStable("not $found");
    [Fact] public void Expr_UnaryMinus() => AssertExprRoundTripStable("-$x");
    [Fact] public void Expr_Spread() => AssertExprRoundTripStable("...$arr");
    [Fact] public void Expr_Bind() => AssertExprRoundTripStable("bind $x");
    [Fact] public void Expr_TwoBind() => AssertExprRoundTripStable("2bind $x");
    [Fact] public void Expr_Its() => AssertExprRoundTripStable("its name");
    [Fact] public void Expr_NestedMacro() => AssertExprRoundTripStable("(random: 1, 6)");
    [Fact] public void Expr_NestedMacroArgs() => AssertExprRoundTripStable("(if: $hp > 0)");
    [Fact] public void Expr_DatamapMacro() => AssertExprRoundTripStable("(dm: \"name\", \"Bob\", \"hp\", 10)");
    [Fact] public void Expr_To() => AssertExprRoundTripStable("$x to 10");
    [Fact] public void Expr_Into() => AssertExprRoundTripStable("10 into $x");

    [Fact]
    public void Expr_NestedPossessive_PreservesShape()
    {
      // `$arr's $other's name` parses as `($arr's $other)'s name` (left-assoc).
      // `$arr's ($other's name)` parses as `$arr's ($other's name)` (parens win).
      // The printer must emit parens when the AST has the right-leaning shape;
      // the round-trip catches drift.
      AssertExprRoundTripStable("$arr's ($other's name)");
    }

    // ----- v2.3A lambdas -----

    [Fact] public void Expr_Lambda_TempParam() => AssertExprRoundTripStable("_x where _x > 5");
    [Fact] public void Expr_Lambda_StoryParam() => AssertExprRoundTripStable("$item where $item is \"target\"");
    [Fact] public void Expr_Lambda_ImplicitIt() => AssertExprRoundTripStable("where it > 5");
    [Fact] public void Expr_Lambda_BodyHasAnd() => AssertExprRoundTripStable("_x where _x > 5 and _x < 10");
    [Fact] public void Expr_Lambda_AsMacroArg() => AssertExprRoundTripStable("(find: _x where _x > 5, 1, 6, 3, 7)");
    [Fact] public void Expr_Lambda_ViaAlone() => AssertExprRoundTripStable("_x via _x * 2");
    [Fact] public void Expr_Lambda_ImplicitItVia() => AssertExprRoundTripStable("via it * 2");
    [Fact] public void Expr_Lambda_WhereThenVia() => AssertExprRoundTripStable("_x where _x > 5 via _x * 2");
    [Fact] public void Expr_Lambda_AlteredCall() => AssertExprRoundTripStable("(altered: _x via _x * 2, 1, 2, 3)");
    [Fact] public void Expr_Lambda_EachTemp() => AssertExprRoundTripStable("each _x");
    [Fact] public void Expr_Lambda_EachStory() => AssertExprRoundTripStable("each $item");
    [Fact] public void Expr_Lambda_ForCall() => AssertExprRoundTripStable("(for: each _x, 1, 2, 3)");
    [Fact] public void Expr_Lambda_MakingFold() => AssertExprRoundTripStable("_item making _acc via _acc + _item");
    [Fact] public void Expr_Lambda_FoldedCall() => AssertExprRoundTripStable("(folded: _item making _acc via _acc + _item, 1, 2, 3)");
    [Fact] public void Expr_Lambda_RotatedToCall() => AssertExprRoundTripStable("(rotated-to: _x where _x is 3, 1, 2, 3)");
    [Fact] public void Expr_SortedCall() => AssertExprRoundTripStable("(sorted: 3, 1, 2)");

    // ----- testFile.html corpus round-trip -----

    [Fact]
    public void Corpus_AllFixturePassages_RoundTripStable()
    {
      // Parse the testFile.html fixture and assert every passage's AST prints
      // to a string that re-parses to itself. This is the most realistic
      // coverage we have — a real Twine 2 export with the full shape mix.
      string html = File.ReadAllText(Path.Combine("TestFiles", "testFile.html"));
      var story = new Harlowe(html);
      var printer = new MarkupPrinter();
      foreach (var passageName in new[] {
        "Disclaimer", "FirstPassage", "Travelling Companion - Vivek",
        "Travelling Companion - Nadeem", "Travelling Companion - Ramita",
        "Final Push - Walking", "Rucksack - Give Away", "Rucksack - Dont Give Away" })
      {
        var passage = story.GetPassage(passageName);
        if (passage == null) continue; // tolerant — not every fixture name may exist
        string first = printer.Print(passage.Ast);
        var reparsed = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(first));
        string second = printer.Print(reparsed);
        Assert.Equal(first, second);
      }
    }
  }
}
