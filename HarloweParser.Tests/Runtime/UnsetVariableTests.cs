using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Saving;
using Harlowe.Tokens;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// How a variable with no value reads. Harlowe 3 gives a story variable the
  /// default 0 and leaves a temp variable an error; Harlowe 4 errors on both.
  /// Every Harlowe 3 expectation here is what real Harlowe 3.3.9 rendered.
  /// </summary>
  public class UnsetVariableTests
  {
    private static StorySession Session(string version, string body)
    {
      var story = new HarloweStory { FormatVersion = version };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      story.StartNode = story.GetPassage("Start").Pid;
      return new StorySession(story);
    }

    private static int Errors(RenderResult result)
    {
      int count = 0;
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) count++;
      return count;
    }

    private static string FirstError(RenderResult result)
    {
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) return entry.Content;
      return null;
    }

    private static int Links(RenderResult result)
    {
      int count = 0;
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Link) count++;
      return count;
    }

    [Theory]
    [InlineData("$x", "0")]
    [InlineData("a $x b", "a 0 b")]
    [InlineData("$toString", "0")]
    [InlineData("(print: \"$x\")", "0")]
    [InlineData("(print: $x + 1)", "1")]
    [InlineData("(print: $y + $x)", "0")]
    [InlineData("(print: -$x)", "0")]
    [InlineData("(print: $x is 0)", "true")]
    [InlineData("(print: $x is $y)", "true")]
    [InlineData("(print: $x is \"\")", "false")]
    [InlineData("(print: $x is a num)", "true")]
    [InlineData("(print: $x matches num)", "true")]
    [InlineData("(print: $x is in (a: 0))", "true")]
    [InlineData("(print: (a: $x, 1))", "0,1")]
    [InlineData("(print: (str: $x))", "0")]
    [InlineData("(print: (max: $x, 1))", "1")]
    [InlineData("(print: (altered: via it + $x, 1, 2))", "1,2")]
    [InlineData("(for: each _i, 1, 2)[$x]", "00")]
    [InlineData("(if: $x is 0)[y]", "y")]
    [InlineData("(if: $x > 0)[y](else:)[n]", "n")]
    [InlineData("(if: $x is false)[y](else:)[n]", "n")]
    [InlineData("(set: $a to (a: 1, 2))(print: $a's ($x + 1))", "1")]
    public void Harlowe3_StoryVariableReadsAsZero(string source, string expected)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    [Theory]
    [InlineData("(set: $x to $x + 1)$x", "1")]
    [InlineData("(set: $x to it + 1)$x", "1")]
    [InlineData("(set: $x to it)$x", "0")]
    [InlineData("(set: $y to $x)$y", "0")]
    [InlineData("(set: _t to $x)_t", "0")]
    [InlineData("(put: $x + 1 into $x)$x", "1")]
    [InlineData("(put: $x into $y)$y", "0")]
    [InlineData("(move: $x into $y)$x $y", "0 0")]
    [InlineData("(unpack: (a: $x, 2) into (a: $y, $z))$y $z", "0 2")]
    [InlineData("(set: $d to (dm:))(move: $x into $d's k)(print: $d's k)", "0")]
    public void Harlowe3_AssignmentFromAnUnsetVariable_StoresZero(string source, string expected)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    [Theory]
    [InlineData("(print: it)", "0")]
    [InlineData("(print: it + 1)", "1")]
    [InlineData("(print: 5 + it)", "5")]
    [InlineData("(print: (a: it))", "0")]
    public void Harlowe3_ItWithNothingBound_ReadsAsZero(string source, string expected)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    // The default is a number, so anything a number can't do still fails.
    [Theory]
    [InlineData("(print: $x + \"a\")")]
    [InlineData("(print: \"a\" + $x)")]
    [InlineData("(print: $x + (a: 1))")]
    [InlineData("(print: $x + true)")]
    [InlineData("(print: 2 / $x)")]
    [InlineData("(print: not $x)")]
    [InlineData("(print: $x and true)")]
    [InlineData("(print: true or $x)")]
    [InlineData("(print: $x contains 0)")]
    [InlineData("(print: 0 is in $x)")]
    [InlineData("(print: $x's 1st)")]
    [InlineData("(print: $x's length)")]
    [InlineData("(print: 1st of $x)")]
    [InlineData("(print: (a: ...$x))")]
    [InlineData("(print: (a: 1)'s $x)")]
    [InlineData("(print: (uppercase: $x))")]
    [InlineData("(display: $x)")]
    [InlineData("(go-to: $x)")]
    [InlineData("(print: its 1st)")]
    public void Harlowe3_ZeroStillFailsWhereANumberWould(string source)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal("", result.Text);
      Assert.Equal(1, Errors(result));
    }

    [Theory]
    [InlineData("(set: $x's 1st to 5)$x", "0")]
    [InlineData("(set: $x's name to 5)$x", "0")]
    [InlineData("(put: 5 into $x's 1st)$x", "0")]
    [InlineData("(set: 1st of $x to 5)$x", "0")]
    [InlineData("(move: $x's 1st into $y)$y", "0")]
    public void Harlowe3_PropertyOfAnUnsetVariable_ErrorsAndWritesNothing(string source, string expected)
    {
      var session = Session("3.3.9", source);
      var result = session.Render();
      Assert.Equal(expected, result.Text);
      Assert.Equal(1, Errors(result));
      Assert.Empty(session.GetVariableNames());
    }

    // A missing data name has no default, so `it` can't read one as 0.
    [Theory]
    [InlineData("(set: $d to (dm:))(set: $d's k to it + 1)")]
    [InlineData("(set: $d to (dm:))(set: $d's k to it)")]
    [InlineData("(set: $a to (a: 1))(set: $a's 2nd to it + 1)")]
    public void Harlowe3_ItOnAMissingSlot_StaysAnError(string source)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal(1, Errors(result));
    }

    [Theory]
    [InlineData("3.3.9")]
    [InlineData("4.0.0")]
    public void TempVariables_HaveNoDefaultUnderEitherMajor(string version)
    {
      foreach (var source in new[]
      {
        "_x", "(print: _x)", "(print: _x + 1)", "(print: _x is 0)", "(print: (a: _x))",
        "(set: _t to _t + 1)", "(set: _t to it + 1)", "(set: _t to it)", "(set: $v to _t)",
        "(put: _t + 1 into _t)", "(move: _x into $y)", "(set: _t's 1st to 5)",
      })
      {
        Assert.Equal(1, Errors(Session(version, source).Render()));
      }
    }

    [Fact]
    public void Harlowe3_LinkToAnUnsetVariable_IsABrokenLinkToZero()
    {
      var named = Session("3.3.9", "[[Go->$x]]").Render();
      Assert.Equal("Go", named.Text);
      Assert.Equal(1, Errors(named));
      Assert.Equal(0, Links(named));

      var bare = Session("3.3.9", "[[$x]]").Render();
      Assert.Equal("0", bare.Text);
      Assert.Equal(1, Errors(bare));
      Assert.Equal(0, Links(bare));
    }

    [Theory]
    [InlineData("3.3.9", "[[Go->_x]]")]
    [InlineData("3.3.9", "[[_x]]")]
    [InlineData("4.0.0", "[[Go->$x]]")]
    public void LinkToAnUnreadableVariable_IsJustTheError(string version, string source)
    {
      var result = Session(version, source).Render();
      Assert.Equal("", result.Text);
      Assert.Equal(1, Errors(result));
      Assert.Equal(0, Links(result));
    }

    // A variable right after `(` and before `:` is a custom-macro call, which
    // we don't implement, so it has to stay one error rather than printing as
    // `(0: 1)`.
    [Theory]
    [InlineData("($x:)", "")]
    [InlineData("($x: 1)", "")]
    [InlineData("a ($x: 5, 10) b", "a  b")]
    [InlineData("(set: $x to 5)($x: 1)", "")]
    [InlineData("(print: ($x:))", "")]
    [InlineData("($x-y:)", "")]
    public void CustomMacroCallSyntax_IsOneError(string source, string expected)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal(expected, result.Text);
      Assert.Equal(1, Errors(result));
    }

    [Theory]
    [InlineData("($x)", "(0)")]
    [InlineData("($x :1)", "(0 :1)")]
    [InlineData("( $x:1)", "( 0:1)")]
    [InlineData("($x's y:)", "(0's y:)")]
    [InlineData("($x://y)", "(0://y)")]
    [InlineData("($:)", "($:)")]
    [InlineData("(http://example.com)", "(http://example.com)")]
    [InlineData("see (https://example.com/a?b=c) now", "see (https://example.com/a?b=c) now")]
    public void ParenthesesThatAreNotACall_StayProse(string source, string expected)
    {
      var result = Session("3.3.9", source).Render();
      Assert.Equal(expected, result.Text);
      Assert.Equal(0, Errors(result));
    }

    [Theory]
    [InlineData("($x: 1)")]
    [InlineData("a ($x: 5, 10) b")]
    [InlineData("($x:)")]
    public void CustomMacroCallSyntax_RoundTrips(string source)
    {
      var body = new HarloweBodyParser().Parse(new HarloweTokenizer(HarloweProfile.V3).Tokenize(source), source);
      Assert.Equal(source, new MarkupPrinter().Print(body));
    }

    [Theory]
    [InlineData("$x")]
    [InlineData("(print: $x + 1)")]
    [InlineData("(print: $x + (text-style: \"bold\"))")]
    [InlineData("(if: $x is 0)[y]")]
    [InlineData("(set: $v to $x)(print: $v)")]
    [InlineData("(set: $x's 1st to 5)")]
    public void Harlowe3_ReadingDoesNotCreateTheVariable(string source)
    {
      var session = Session("3.3.9", source);
      session.Render();
      Assert.False(session.TryGetVariable("x", out _));
    }

    [Fact]
    public void Harlowe3_ReadingDoesNotEnterTheSave()
    {
      var session = Session("3.3.9", "(print: $x)(set: $v to $x + 1)");
      session.Render();
      Assert.True(session.SaveGame("slot"));

      Assert.True(session.LoadGame("slot"));
      session.Render();
      Assert.False(session.TryGetVariable("x", out _));
      Assert.True(session.TryGetVariable("v", out var v));
      Assert.Equal(1, v.AsNumber);
    }

    [Fact]
    public void SavedSource_NeverReadsAVariableAsZero()
    {
      // Saved values are engine-written literals. A variable read in one means
      // the blob was tampered with, so it stays an error under a Harlowe 3 story.
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var context = new MacroContext
      {
        Store = new HarloweVariableStore(),
        Invoker = registry,
        Profile = HarloweProfile.V3,
      };

      Assert.True(SaveSerializer.Deserialise("$x", registry, context).IsError);
    }

    // Each names the unset value, because a 0 would also fail at several of
    // these positions and only the message tells the two apart.
    [Theory]
    [InlineData("$x", "$x is not set")]
    [InlineData("(print: $x)", "$x is not set")]
    [InlineData("(print: $x + 1)", "$x is not set")]
    [InlineData("(print: it)", "'it' is not yet set")]
    [InlineData("(print: its 1st)", "'it' is not yet set")]
    [InlineData("(print: (altered: via it + $x, 1, 2))", "$x is not set")]
    [InlineData("(set: $v to $x)", "$x is not set")]
    [InlineData("(set: $v to it + 1)", "$v is not set")]
    [InlineData("(move: $x into $v)", "$x is not set")]
    [InlineData("(move: $x's 1st into $v)", "$x is not set")]
    [InlineData("(set: $x's 1st to 5)", "$x is not set")]
    [InlineData("(put: 5 into $x's name)", "$x is not set")]
    [InlineData("(print: (a: ...$x))", "$x is not set")]
    public void Harlowe4_UnsetValuesAreErrors(string source, string message)
    {
      var session = Session("4.0.0", source);
      var result = session.Render();
      Assert.Equal("", result.Text);
      Assert.Equal(1, Errors(result));
      Assert.Contains(message, FirstError(result));
      Assert.False(session.TryGetVariable("v", out _));
      Assert.False(session.TryGetVariable("x", out _));
    }

    [Fact]
    public void Harlowe4_UnsetVariableBeforeAHook_IsAnError()
    {
      var result = Session("4.0.0", "$x[hi]").Render();
      Assert.Equal(1, Errors(result));
      Assert.Contains("$x is not set", FirstError(result));
    }
  }
}
