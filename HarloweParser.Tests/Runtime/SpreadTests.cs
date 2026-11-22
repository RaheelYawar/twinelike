using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>The <c>...</c> spread operator, expanded in macro argument lists as in reference's <c>spreadArguments</c>.</summary>
  public class SpreadTests
  {
    private static string Render(string source)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source));
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(ast);
      return buf.Text;
    }

    private static List<string> ErrorsOf(string source)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source));
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(ast);
      var errors = new List<string>();
      foreach (var entry in buf.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) errors.Add(entry.Content);
      return errors;
    }

    [Theory]
    [InlineData("(set: $l to (a: 1, 2, 3))(for: each _i, ...$l)[_i ]", "1 2 3 ")]
    [InlineData("(print: (max: ...(a: 4, 9, 2)))", "9")]
    [InlineData("(print: (min: 5, ...(a: 4, 9), 1))", "1")]
    [InlineData("(print: (sorted: ...(a: 3, 1, 2)))", "1,2,3")]
    [InlineData("(print: (a: 0, ...(a: 1, 2), 3))", "0,1,2,3")]
    [InlineData("(print: (a: ...(a:), 1))", "1")]
    [InlineData("(print: (either: ...(a: 7)))", "7")]
    [InlineData("(print: (a: ...\"abc\"))", "a,b,c")]
    [InlineData("(print: (a: ...(a: 1), ...(a: 2)))", "1,2")]
    public void Spread_ExpandsIntoArguments(string source, string expected)
      => Assert.Equal(expected, Render(source));

    [Fact]
    public void Spread_SplitsAStringByCodePoint()
      => Assert.Equal("\U0001F600,x", Render("(print: (a: ...\"\U0001F600x\"))"));

    [Fact]
    public void Spread_BuildsADatamapFromPairs()
      => Assert.Equal("5", Render("(set: $d to (dm: ...(a: \"k\", 5)))(print: $d's k)"));

    [Fact]
    public void Spread_CountsTowardArity()
    {
      // (substring:) takes exactly three arguments, supplied here by the spread.
      Assert.Equal("ell", Render("(print: (substring: ...(a: \"hello\", 2, 4)))"));
    }

    [Theory]
    [InlineData("(print: (a: ...5))", "spread out a Number")]
    [InlineData("(print: (a: ...true))", "spread out a Bool")]
    [InlineData("(print: (a: ...(dm: \"k\", 1)))", "spread out a Datamap")]
    [InlineData("(print: (a: ...num))", "spread datatypes")]
    [InlineData("(print: (a: ...$missing))", "$missing is not set")]
    [InlineData("(set: $x to ...(a: 1))", "can only be given directly to a macro")]
    [InlineData("(print: ...(a: 1) + (a: 2))", "can only be given directly to a macro")]
    public void Spread_ReportsBadOperands(string source, string expected)
      => Assert.Contains(expected, string.Join("\n", ErrorsOf(source)));

    [Theory]
    [InlineData("(print: (find: where it > 1, ...(a:)))", "")]
    [InlineData("(print: (altered: via it, ...(a:)))", "")]
    [InlineData("(print: (all-pass: where it > 1, ...(a:)))", "true")]
    [InlineData("(print: (some-pass: where it > 1, ...(a:)))", "false")]
    [InlineData("(print: (none-pass: where it > 1, ...(a:)))", "true")]
    public void LambdaMacros_AcceptAnEmptySpread(string source, string expected)
    {
      Assert.Empty(ErrorsOf(source));
      Assert.Equal(expected, Render(source));
    }

    [Fact]
    public void LambdaMacros_TreatAnUnspreadArrayAsOneItem()
    {
      // `(a: 1, 2) > 0` is a type error, which proves the array wasn't unpacked.
      Assert.NotEmpty(ErrorsOf("(print: (find: _x where _x > 0, (a: 1, 2)))"));
    }
  }
}
