using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// The typed Datatype value: literal lexing, per-name matching, the
  /// <c>is a</c> / <c>is not a</c> / <c>matches</c> / <c>does not match</c>
  /// operators, the <c>(datatype:)</c>/<c>(datapattern:)</c> macros, and
  /// save/load source round-tripping. Reference:
  /// <c>ts/datatypes/datatype.ts</c>, the <c>isA</c>/<c>matches</c> pair in
  /// <c>ts/utils/operationutils.ts</c>, and the two macros in
  /// <c>ts/macrolib/values.ts</c>.
  /// </summary>
  public class DatatypeValueTests
  {
    private static (MacroRegistry reg, MacroContext ctx) Setup()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = reg };
      reg.Context = ctx;
      return (reg, ctx);
    }

    private static HarloweValue Eval(string expr)
    {
      var (reg, ctx) = Setup();
      var tokens = new HarloweTokenizer().Tokenize("(_:" + expr + ")");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var node = new HarloweExpressionParser().ParseExpression(cursor);
      return new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker).Evaluate(node);
    }

    private static bool EvalBool(string expr)
    {
      var v = Eval(expr);
      Assert.Equal(HarloweValueKind.Bool, v.Kind);
      return v.AsBool;
    }

    // --- Lexing ---

    [Theory]
    [InlineData("num")]
    [InlineData("number")]
    [InlineData("str")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("boolean")]
    [InlineData("array")]
    [InlineData("dm")]
    [InlineData("datamap")]
    [InlineData("ds")]
    [InlineData("dataset")]
    [InlineData("datatype")]
    [InlineData("changer")]
    [InlineData("colour")]
    [InlineData("color")]
    [InlineData("gradient")]
    [InlineData("image")]
    [InlineData("lambda")]
    [InlineData("macro")]
    [InlineData("codehook")]
    [InlineData("command")]
    [InlineData("measure")]
    [InlineData("even")]
    [InlineData("odd")]
    [InlineData("empty")]
    [InlineData("int")]
    [InlineData("integer")]
    [InlineData("uppercase")]
    [InlineData("lowercase")]
    [InlineData("anycase")]
    [InlineData("whitespace")]
    [InlineData("digit")]
    [InlineData("alnum")]
    [InlineData("alphanumeric")]
    [InlineData("linebreak")]
    [InlineData("newline")]
    [InlineData("any")]
    [InlineData("const")]
    public void EveryReferenceName_LexesAsDatatypeLiteral(string name)
    {
      var tokens = new HarloweTokenizer().Tokenize("(_: " + name + ")");
      Assert.Equal(TokenType.DatatypeLiteral, tokens[1].Type);
      Assert.Equal(name, tokens[1].Value);
      Assert.Equal(HarloweValueKind.Datatype, Eval(name).Kind);
    }

    [Theory]
    [InlineData("NUM", "num")]
    [InlineData("Number", "num")]
    [InlineData("Whitespace", "whitespace")]
    public void DatatypeName_IsCaseInsensitive(string source, string canonical)
    {
      // Reference's rule lowercases the match (`ts/markup/markup.ts`'s datatype
      // fn), so the spelling is free but the value is one.
      Assert.Equal(canonical, Eval(source).AsDatatype.Name);
    }

    [Theory]
    [InlineData("number", "num")]
    [InlineData("string", "str")]
    [InlineData("boolean", "bool")]
    [InlineData("datamap", "dm")]
    [InlineData("dataset", "ds")]
    [InlineData("integer", "int")]
    [InlineData("alphanumeric", "alnum")]
    [InlineData("newline", "linebreak")]
    [InlineData("color", "colour")]
    public void LongSpelling_CanonicalisesToTheAbbreviation(string source, string canonical)
    {
      Assert.Equal(canonical, Eval(source).AsDatatype.Name);
      Assert.True(Eval(source).Equals(Eval(canonical)));
    }

    [Fact]
    public void DatatypeNames_AreNotStolenInPropertyPosition()
    {
      // Same guard the colour rule needs: a word after `'s` (or before `of`)
      // names a data key, whatever else it would mean.
      Assert.Equal(1, Eval("(dm: \"num\", 1)'s num").AsNumber);
      Assert.Equal(2, Eval("empty of (dm: \"empty\", 2)").AsNumber);
    }

    [Fact]
    public void IntoIsNotLexedAsTheIntDatatype()
    {
      // Reference guards its `int` name with notBefore("o") for exactly this;
      // our whole-word identifier scan gets it for free, and this pins it.
      var tokens = new HarloweTokenizer().Tokenize("(put: 1 into $x)");
      Assert.Equal(TokenType.Operator, tokens[2].Type);
      Assert.Equal("into", tokens[2].Value);
    }

    // --- is a ---

    [Theory]
    [InlineData("2 is a num", true)]
    [InlineData("2 is a number", true)]
    [InlineData("2 is a str", false)]
    [InlineData("\"x\" is a str", true)]
    [InlineData("true is a bool", true)]
    [InlineData("(a: 1) is a array", true)]
    [InlineData("(dm: \"a\", 1) is a dm", true)]
    [InlineData("red is a colour", true)]
    [InlineData("num is a datatype", true)]
    [InlineData("2 is a any", true)]
    [InlineData("2 is not a str", true)]
    [InlineData("2 is not a num", false)]
    public void IsA_ComparesAgainstTheTypeName(string expr, bool expected)
    {
      Assert.Equal(expected, EvalBool(expr));
    }

    [Fact]
    public void IsAn_ReadsTheSameAsIsA()
    {
      // Reference's isA pattern is `is\s*an?\b`; its own documentation uses
      // `(a:2,3) is an array`.
      Assert.True(EvalBool("(a: 2, 3) is an array"));
      Assert.True(EvalBool("2 is not an array"));
    }

    [Fact]
    public void IsA_NonDatatypeRightSide_IsAnError()
    {
      var v = Eval("2 is a 3");
      Assert.True(v.IsError);
      Assert.Contains("type names", v.ErrorMessage);
    }

    [Fact]
    public void IsNotA_PreservesTheErrorRatherThanNegatingIt()
    {
      Assert.True(Eval("2 is not a 3").IsError);
    }

    // --- Subset types ---

    [Theory]
    [InlineData("2 is a even", true)]
    [InlineData("3 is a even", false)]
    [InlineData("-4 is a even", true)]
    [InlineData("3 is a odd", true)]
    [InlineData("2.5 is a even", true)] // floor(abs(2.5)) is 2, as in reference
    public void EvenAndOdd_FollowReferenceFloorAbs(string expr, bool expected)
    {
      Assert.Equal(expected, EvalBool(expr));
    }

    [Fact]
    public void EvenAndOdd_RejectNonNumbers()
    {
      // Deliberate divergence. Reference's `even`/`odd` are the only entries in
      // its typeIndex that don't check the value is a number first, so JS
      // coercion makes `"2" is a even` true there. Its own article says these
      // "Only match even/odd numbers", so the documented contract wins.
      Assert.False(EvalBool("\"2\" is a even"));
      Assert.False(EvalBool("\"3\" is a odd"));
    }

    [Theory]
    [InlineData("2 is a int", true)]
    [InlineData("-2 is a int", true)]
    [InlineData("2.5 is a int", false)]
    [InlineData("0 is a int", true)]
    public void Int_MatchesWholeNumbers(string expr, bool expected)
    {
      Assert.Equal(expected, EvalBool(expr));
    }

    [Fact]
    public void Int_ExcludesValuesOutsideInt32()
    {
      // Reference tests `obj === (obj|0)`, a 32-bit truncation, so a whole
      // number past int32 is not an `int` there either. Matched deliberately.
      Assert.False(EvalBool("3000000000 is a int"));
      Assert.True(EvalBool("2147483647 is a int"));
    }

    [Theory]
    [InlineData("\"\" is a empty", true)]
    [InlineData("\"x\" is a empty", false)]
    [InlineData("(a:) is a empty", true)]
    [InlineData("(a: 1) is a empty", false)]
    [InlineData("(dm:) is a empty", true)]
    [InlineData("0 is a empty", false)]
    public void Empty_MatchesEmptySequences(string expr, bool expected)
    {
      Assert.Equal(expected, EvalBool(expr));
    }

    [Theory]
    [InlineData("\"A\" is a uppercase", true)]
    [InlineData("\"a\" is a uppercase", false)]
    [InlineData("\"a\" is a lowercase", true)]
    [InlineData("\"1\" is a lowercase", false)]
    [InlineData("\"A\" is a anycase", true)]
    [InlineData("\"1\" is a anycase", false)]
    [InlineData("\"ab\" is a lowercase", false)] // single characters only
    public void CaseTypes_MatchSingleCasedCharacters(string expr, bool expected)
    {
      Assert.Equal(expected, EvalBool(expr));
    }

    [Theory]
    [InlineData("\"5\" is a digit", true)]
    [InlineData("\"a\" is a digit", false)]
    [InlineData("\"55\" is a digit", false)]
    [InlineData("\"a\" is a alnum", true)]
    [InlineData("\"5\" is a alnum", true)]
    [InlineData("\"!\" is a alnum", false)]
    [InlineData("\" \" is a whitespace", true)]
    [InlineData("\"x\" is a whitespace", false)]
    public void CharacterClassTypes_MatchOneCharacter(string expr, bool expected)
    {
      Assert.Equal(expected, EvalBool(expr));
    }

    [Fact]
    public void Whitespace_MatchesTheNonAsciiFormsButNotOghamSpaceMark()
    {
      // Reference's realWhitespace is "all forms of Unicode 6 whitespace …
      // except Ogham space mark", so char.IsWhiteSpace would be too generous —
      // it also accepts U+1680 and U+0085 NEL.
      Assert.True(EvalBool("\"\u00a0\" is a whitespace"));  // no-break space
      Assert.True(EvalBool("\"\u3000\" is a whitespace"));  // ideographic space
      Assert.False(EvalBool("\"\u1680\" is a whitespace")); // ogham space mark
      Assert.False(EvalBool("\"\u0085\" is a whitespace")); // NEL
    }

    [Fact]
    public void Linebreak_IsTheOneTypeThatMatchesTwoCharacters()
    {
      // Reference's anyNewline alternates \n, \r and \r\n. The escapes here are
      // Harlowe's own, cooked by the tokenizer into the string's value.
      Assert.True(EvalBool("\"\\n\" is a linebreak"));
      Assert.True(EvalBool("\"\\r\" is a linebreak"));
      Assert.True(EvalBool("\"\\r\\n\" is a linebreak"));
      Assert.False(EvalBool("\"\\n\\n\" is a linebreak"));
    }

    [Fact]
    public void UnimplementedValueTypes_MatchNothing()
    {
      // ds/gradient/image/macro/command/codehook/measure still lex and compare
      // — they just have nothing to match, exactly as an author would see for a
      // value they can't construct.
      Assert.False(EvalBool("(a: 1) is a ds"));
      Assert.False(EvalBool("2 is a measure"));
    }

    // --- matches ---

    [Theory]
    [InlineData("2 matches num", true)]
    [InlineData("num matches 2", true)]
    [InlineData("2 matches 2", true)]
    [InlineData("2 matches 3", false)]
    [InlineData("2 does not match str", true)]
    [InlineData("(a: 2, 3) matches (a: num, num)", true)]
    [InlineData("(a: 2, 3) matches (a: num, str)", false)]
    [InlineData("(a: 2, 3) matches (a: num)", false)]
    [InlineData("(a: 2, 3, 4) matches (a: 2, int, int)", true)]
    [InlineData("(a: (a: 2), (a: 4)) matches (a: (a: num), (a: even))", true)]
    [InlineData("(dm: \"a\", 2, \"b\", 4) matches (dm: \"b\", num, \"a\", num)", true)]
    [InlineData("(dm: \"a\", 2) matches (dm: \"b\", num)", false)]
    public void Matches_ComparesDataAgainstAPattern(string expr, bool expected)
    {
      // The array/datamap examples are reference's own, from the Datatype
      // article in ts/datatypes/datatype.ts.
      Assert.Equal(expected, EvalBool(expr));
    }

    [Fact]
    public void Matches_IsSymmetricInTheDatatypeSide()
    {
      Assert.True(EvalBool("(a: num, num) matches (a: 2, 3)"));
    }

    [Fact]
    public void Matches_TwoDatatypes_TrueIfEitherTypesTheOther()
    {
      // Reference ORs both directions, so the `datatype` type accepts `num`.
      Assert.True(EvalBool("datatype matches num"));
      Assert.True(EvalBool("num matches num"));
      Assert.False(EvalBool("num matches str"));
    }

    [Fact]
    public void Matches_NeverErrors_UnlikeIsA()
    {
      // `matches` takes a pattern on either side, so a non-datatype right side
      // is an ordinary structural comparison, not the `is a` error.
      Assert.False(EvalBool("2 matches \"2\""));
    }

    // --- Equality and storage ---

    [Fact]
    public void Datatypes_CompareWithIs()
    {
      Assert.True(EvalBool("num is num"));
      Assert.True(EvalBool("num is number"));
      Assert.False(EvalBool("num is str"));
    }

    [Fact]
    public void EqualDatatypesHashEqually()
    {
      var a = Eval("number");
      var b = Eval("num");
      Assert.True(a.Equals(b));
      Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Datatype_FlowsThroughVariables()
    {
      var (reg, ctx) = Setup();
      var ast = new HarloweBodyParser().Parse(
        new HarloweTokenizer().Tokenize("(set: $t to num)(set: $u to $t)"));
      new BodyRenderer(new BufferedRenderOutput(), reg, ctx).Render(ast);

      Assert.Equal(HarloweValueKind.Datatype, ctx.Store.Get("u", false).Kind);
      Assert.True(ctx.Store.Get("t", false).Equals(ctx.Store.Get("u", false)));
    }

    // --- Printing and source ---

    [Fact]
    public void Datatype_PrintsAsItsObjectName()
    {
      // Reference's print() is `[the num datatype]` in verbatim markup; the
      // brackets are literal text on our render channel.
      Assert.Equal("[the num datatype]", Eval("number").ToHarloweString());

      var (reg, ctx) = Setup();
      var output = new BufferedRenderOutput();
      new BodyRenderer(output, reg, ctx).Render(
        new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: num)")));
      Assert.Equal("[the num datatype]", output.Text);
    }

    [Fact]
    public void IsA_DrivesAConditionalHook()
    {
      // The headline authoring idiom, end to end through the renderer rather
      // than the evaluator alone: `(if: $money is a num)`.
      var (reg, ctx) = Setup();
      var output = new BufferedRenderOutput();
      new BodyRenderer(output, reg, ctx).Render(new HarloweBodyParser().Parse(
        new HarloweTokenizer().Tokenize(
          "(set: $money to 5)(if: $money is a num)[rich](else:)[broke]")));
      Assert.Equal("rich", output.Text);
    }

    [Fact]
    public void Datatype_ToSource_RoundTrips()
    {
      Assert.Equal("num", Eval("number").ToSource());
      Assert.True(Eval(Eval("number").ToSource()).Equals(Eval("num")));
    }

    [Fact]
    public void Datatype_SourceSurvivesInsideACollection()
    {
      var v = Eval("(a: num, str)");
      Assert.Equal("(a:num,str)", v.ToSource());
      Assert.True(Eval(v.ToSource()).Equals(v));
    }

    [Theory]
    [InlineData("(set: $t to number)")]
    [InlineData("(if: $x is a num)[y]")]
    [InlineData("(if: $x matches (a: num, str))[y]")]
    public void DatatypeLiteral_RoundTripsThroughMarkupPrinter(string source)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));
      Assert.Equal(source, new MarkupPrinter().Print(ast));
    }

    // --- (datatype:) / (datapattern:) ---

    [Theory]
    [InlineData("(datatype: 2)", "num")]
    [InlineData("(datatype: 2.5)", "num")]
    [InlineData("(datatype: \"x\")", "str")]
    [InlineData("(datatype: true)", "bool")]
    [InlineData("(datatype: (a: 1))", "array")]
    [InlineData("(datatype: (dm: \"a\", 1))", "dm")]
    [InlineData("(datatype: red)", "colour")]
    [InlineData("(datatype: num)", "datatype")]
    public void DatatypeMacro_ProducesTheGeneralType(string expr, string expected)
    {
      var v = Eval(expr);
      Assert.Equal(HarloweValueKind.Datatype, v.Kind);
      Assert.Equal(expected, v.AsDatatype.Name);
    }

    [Fact]
    public void DatatypeMacro_NeverProducesASubsetType()
    {
      // Reference searches only basicTypeIndex, so 2 is `num`, never `even`,
      // and "5" is `str`, never `digit`.
      Assert.True(EvalBool("(datatype: 2) is num"));
      Assert.True(EvalBool("(datatype: \"5\") is str"));
    }

    [Fact]
    public void DatatypeMacro_ComparesTwoValuesTypes()
    {
      // The documented idiom: `_theirName is a (datatype: _myName)`.
      Assert.True(EvalBool("\"Bob\" is a (datatype: \"Amy\")"));
      Assert.False(EvalBool("2 is a (datatype: \"Amy\")"));
    }

    [Fact]
    public void DatatypeMacro_ValueWithNoTypeName_IsAnError()
    {
      Assert.True(Eval("(datatype: ?hook)").IsError);
    }

    [Fact]
    public void DatapatternMacro_ReplacesLeavesWithTheirTypes()
    {
      // Reference's own example: (datapattern: (a:15,45)) produces (a:num,num).
      Assert.Equal("(a:num,num)", Eval("(datapattern: (a: 15, 45))").ToSource());
      Assert.True(EvalBool("(a: 1, 2) matches (datapattern: (a: 15, 45))"));
      Assert.False(EvalBool("(a: 1, \"x\") matches (datapattern: (a: 15, 45))"));
    }

    [Fact]
    public void DatapatternMacro_RecursesThroughNestedStructures()
    {
      Assert.Equal("(a:(a:num),str)", Eval("(datapattern: (a: (a: 1), \"x\"))").ToSource());
      Assert.Equal("(dm:\"a\",num)", Eval("(datapattern: (dm: \"a\", 1))").ToSource());
    }

    [Fact]
    public void DatapatternMacro_ScalarBehavesLikeDatatypeMacro()
    {
      Assert.True(EvalBool("(datapattern: 2) is num"));
    }

    [Fact]
    public void DatapatternMacro_PropagatesAnUntypeableLeaf()
    {
      Assert.True(Eval("(datapattern: (a: ?hook))").IsError);
    }

    // --- Character-class edge cases ---
    //
    // Reference splits these datatypes across two implementation styles, and
    // the split is load-bearing. `uppercase`/`lowercase` are defined over
    // `[...obj]` (code points); `alnum`/`anycase`/`whitespace`/`digit` go
    // through `obj.match("^…$")`, a RegExp built from a *string* and so without
    // the `u` flag, which can only ever match a single UTF-16 code unit.

    // A code point above U+FFFF is two code units, so reference's anchored
    // match can never accept one — even though `anyRealLetter` ends with the
    // whole surrogate range U+D800-U+DFFF.
    [Fact]
    public void Alnum_RejectsAstralCharacters()
    {
      string astral = char.ConvertFromUtf32(0x1D400); // MATHEMATICAL BOLD CAPITAL A
      Assert.False(EvalBool("\"" + astral + "\" is an alnum"));
      Assert.False(EvalBool("\"" + astral + "\" is a anycase"));
    }

    // The other half of that same class: a *lone* surrogate is one code unit
    // and is inside the range, so reference does accept it. Faithful in both
    // directions, which is the only way the rule stays explicable.
    [Fact]
    public void Alnum_AcceptsLoneSurrogate()
    {
      Assert.True(EvalBool("\"" + ((char)0xD800) + "\" is an alnum"));
    }

    [Fact]
    public void Digit_AndWhitespace_AreSingleCodeUnitsToo()
    {
      Assert.True(EvalBool("\"7\" is a digit"));
      // U+1D7CE MATHEMATICAL BOLD DIGIT ZERO: a digit by name, not by `\d`.
      Assert.False(EvalBool("\"" + char.ConvertFromUtf32(0x1D7CE) + "\" is a digit"));
      // U+2028 LINE SEPARATOR is in reference's realWhitespace class.
      Assert.True(EvalBool("\"" + ((char)0x2028) + "\" is a whitespace"));
      // U+1680 OGHAM SPACE MARK is explicitly excluded from it.
      Assert.False(EvalBool("\"" + ((char)0x1680) + "\" is a whitespace"));
    }

    // Deliberate divergence. Reference tests `char !== char.toUpperCase()`, and
    // JS full case mapping expands "ß" to "SS", so `"ß" is a lowercase` is true
    // there. .NET invariant casing is simple (non-expanding) and netstandard2.0
    // has no full-case-mapping API. Kept non-expanding so these datatypes agree
    // with this library's own (uppercase:)/(lowercase:), which is the property
    // reference says it wants.
    [Fact]
    public void Lowercase_DoesNotExpandUnderCaseMapping()
    {
      Assert.False(EvalBool("\"" + ((char)0x00DF) + "\" is a lowercase")); // eszett
      Assert.False(EvalBool("\"" + ((char)0xFB01) + "\" is a lowercase")); // fi ligature
      // Ordinary cased characters are unaffected.
      Assert.True(EvalBool("\"a\" is a lowercase"));
      Assert.True(EvalBool("\"A\" is an uppercase"));
      Assert.True(EvalBool("\"a\" is a anycase"));
    }

    // --- Interning ---

    [Fact]
    public void Datatype_IsInternedPerCanonicalName()
    {
      // Both spellings fold to one canonical name, and that name has exactly
      // one instance — so evaluating a literal costs a lookup, not an
      // allocation, and reference equality agrees with EqualsDatatype.
      Assert.Same(DatatypeValue.FromLexeme("num"), DatatypeValue.FromLexeme("number"));
      Assert.Same(DatatypeValue.FromLexeme("num"), DatatypeValue.From(HarloweValue.OfNumber(1)));
      Assert.Same(DatatypeValue.FromLexeme("NUM"), DatatypeValue.FromLexeme("num"));
      Assert.NotSame(DatatypeValue.FromLexeme("num"), DatatypeValue.FromLexeme("str"));
    }

    [Fact]
    public void Datatype_SeparatesPrintedProseFromDiagnosticForm()
    {
      var num = DatatypeValue.FromLexeme("num");
      Assert.Equal("[the num datatype]", num.ToPrintedString());
      // ToString stays a diagnostic: player-facing prose must be asked for.
      Assert.DoesNotContain("[the", num.ToString());
    }

    // --- Recursion depth ---

    [Fact]
    public void Matches_OnOverDeepValue_IsAnInProseErrorNotACrash()
    {
      // DeepCopyValue stops copying at its own cap rather than erroring, so a
      // structure deeper than the cap can genuinely reach the store. `matches`
      // must report that rather than blowing the stack, which no in-prose error
      // policy can catch.
      var (reg, ctx) = Setup();
      var deep = HarloweValue.OfNumber(1);
      for (int i = 0; i < 400; i++)
        deep = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue> { deep });
      ctx.Store.Set("deep", false, deep);

      var tokens = new HarloweTokenizer().Tokenize("(_:$deep matches $deep)");
      var cursor = new TokenCursor(tokens);
      cursor.Advance();
      var node = new HarloweExpressionParser().ParseExpression(cursor);
      var result = new ExpressionEvaluator(ctx.Store, ctx.EvaluationContext, ctx.Invoker).Evaluate(node);

      Assert.True(result.IsError);
      Assert.Contains("nested too deeply", result.ErrorMessage);
    }

    [Fact]
    public void Equality_OnOverDeepValue_DoesNotCrash()
    {
      var deep = HarloweValue.OfNumber(1);
      for (int i = 0; i < 400; i++)
        deep = HarloweValue.OfArray(new System.Collections.Generic.List<HarloweValue> { deep });
      // Equals is an object override with nowhere to put an error, so the cap
      // reports "not equal" — wrong, but survivable, unlike a stack overflow.
      Assert.False(deep.Equals(deep));
    }
  }
}
