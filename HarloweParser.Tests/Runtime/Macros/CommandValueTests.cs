using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Commands as values: <c>[[links]]</c> in expressions, built-in commands held
  /// in variables, and changers attached to them. Expected page text was
  /// rendered by the real 3.3.9 build.
  /// </summary>
  public class CommandValueTests
  {
    private static HarloweStory Story(params (string name, string body)[] passages)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      foreach (var (name, body) in passages)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage(passages[0].name).Pid;
      return story;
    }

    private static RenderResult Render(string body)
      => new StorySession(Story(("Start", body), ("b", "B"))).Render();

    private static List<string> Errors(RenderResult result)
    {
      var errors = new List<string>();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) errors.Add(entry.Content);
      return errors;
    }

    private static List<BufferedRenderOutput.Entry> Links(RenderResult result)
      => result.Entries.FindAll(e => e.Kind == BufferedRenderOutput.Kind.Link);

    /// <summary>The page's text with link labels in place, which <see cref="RenderResult.Text"/> leaves out.</summary>
    private static string Shown(RenderResult result)
    {
      var text = new System.Text.StringBuilder();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Text || entry.Kind == BufferedRenderOutput.Kind.Link)
          text.Append(entry.Content);
      return text.ToString();
    }

    private static void AssertRenders(string source, string expected)
    {
      var result = Render(source);
      Assert.Empty(Errors(result));
      Assert.Equal(expected, Shown(result));
    }

    [Theory]
    [InlineData("(set: $l to [[b]])$l", "b", "b")]
    [InlineData("(set: $l to [[hello->b]])$l", "hello", "b")]
    [InlineData("(set: $l to [[b<-hello]])$l", "hello", "b")]
    [InlineData("(set: $l to (link-goto: 'hello', 'b'))$l", "hello", "b")]
    [InlineData("(set: $v to 'b')(set: $l to [[go->$v]])$l", "go", "b")]
    [InlineData("(set: $v to 'b')(set: $l to [[$v]])$l", "b", "b")]
    [InlineData("(set: $m to (macro: [(output-data: [[hello->b]])]))($m:)", "hello", "b")]
    [InlineData("(set: $m to (macro: [(output:)[[[hello->b]]]]))($m:)", "hello", "b")]
    public void ALinkHeldAsAValue_RendersWhenItReachesProse(string source, string text, string target)
    {
      var result = Render(source);
      Assert.Empty(Errors(result));
      var link = Assert.Single(Links(result));
      Assert.Equal(text, link.Content);
      Assert.Equal(target, link.Target);
    }

    [Fact]
    public void ALinkHeldAsAValue_RendersEveryTimeItsPrinted()
    {
      var result = Render("(set: $l to [[hello->b]])$l $l");
      Assert.Equal(2, Links(result).Count);
      Assert.Equal("hello hello", Shown(result));
    }

    [Fact]
    public void ALinkVariableTarget_ResolvesWhenTheLinkRenders()
    {
      var result = Render("(set: $t to 'nowhere')(set: $l to [[go->$t]])(set: $t to 'b')$l");
      Assert.Empty(Errors(result));
      Assert.Equal("b", Assert.Single(Links(result)).Target);
    }

    [Fact]
    public void AStoredBrokenLink_ShowsItsLabelAndAnError()
    {
      var result = Render("(set: $l to [[hello->nowhere]])$l");
      Assert.Empty(Links(result));
      Assert.Equal("hello", result.Text);
      Assert.Contains("'nowhere'", Assert.Single(Errors(result)));
    }

    [Theory]
    [InlineData("(print: [[hello->b]])", "[A (link-goto:) command]")]
    [InlineData("(print: (link-goto: 'b'))", "[A (link-goto:) command]")]
    [InlineData("(print: (a: [[b]]))", "[A (link-goto:) command]")]
    [InlineData("(print: (print: 5))", "[A (print:) command]")]
    [InlineData("(print: (display: 'b'))", "[A (display:) command]")]
    [InlineData("(print: (goto: 'b'))", "[A (go-to:) command]")]
    [InlineData("(print: (go-to: 'b'))", "[A (go-to:) command]")]
    [InlineData("(print: (load-game: 'f'))", "[A (load-game:) command]")]
    [InlineData("(print: (change: ?a, (text-style: 'bold')))", "[A (change:) command]")]
    [InlineData("(print: (click-goto: ?a, 'b'))", "[A (click-goto:) command]")]
    [InlineData("(print: (link-undo: 'u'))", "[A (link-undo:) command]")]
    public void ACommandPrintedAsData_ShowsItsDescription(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Theory]
    [InlineData("(print: [[hello->b]] is a command)", "true")]
    [InlineData("(print: (link-goto: 'x', 'b') is (link-goto: 'x', 'b'))", "true")]
    [InlineData("(set: $l to [[a->b]])(set: $k to [[a->b]])(print: $l is $k)", "true")]
    [InlineData("(set: $l to [[a->b]])(set: $k to [[c->b]])(print: $l is $k)", "false")]
    [InlineData("(print: (output-data: 1) is (output-data: 1))", "true")]
    [InlineData("(print: (error: 'x') is (error: 'x'))", "true")]
    [InlineData("(print: (error: 'x') is (error: 'y'))", "false")]
    [InlineData("(print: (a: [[b]]) is (a: [[b]]))", "true")]
    public void BuiltInCommands_CompareByTheirSource(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Fact]
    public void CustomCommands_AreEqualOnlyToThemselves()
    {
      AssertRenders("(set: $m to (macro: [(output:)[q]]))(set: $a to ($m:), $b to ($m:))(print: $a is $a)(print: $a is $b)", "truefalse");
    }

    [Theory]
    [InlineData("(set: $c to (print: 5))$c $c", "5 5")]
    [InlineData("(set: $c to (print: 5))(for: each _i, 1, 2)$c", "55")]
    [InlineData("(set: $c to (print: 5))(if: false)$c z", " z")]
    [InlineData("(set: $c to (print: 5))(text-style:'italic')$c", "5")]
    [InlineData("(set: $c to (display: 'b'))$c", "B")]
    [InlineData("(set: $c to (display: 'b'))(if: false)$c z", " z")]
    [InlineData("(set: $c to (link-undo: 'u'))$c", "")]
    [InlineData("|a>[x](set: $c to (click-goto: ?a, 'b'))$c", "x")]
    public void ABuiltInCommandHeldAsAValue_RunsWhenItReachesProse(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Fact]
    public void AStoredGoto_DoesntNavigateUntilItsPrinted()
    {
      var session = new StorySession(Story(("Start", "(set: $c to (go-to: 'b'))z"), ("b", "B")));
      Assert.Equal("z", session.Render().Text);

      session = new StorySession(Story(("Start", "(set: $c to (go-to: 'b'))z$c y"), ("b", "B")));
      Assert.Equal("B", session.Render().Text);
    }

    [Fact]
    public void AStoredChange_DoesNothingUntilItsPrinted()
    {
      var unused = Render("|a>[x](set: $c to (change: ?a, (text-style: 'bold')))");
      Assert.DoesNotContain(unused.Entries, e => e.Kind == BufferedRenderOutput.Kind.PushStyle);

      var printed = Render("|a>[x](set: $c to (change: ?a, (text-style: 'bold')))$c");
      Assert.Contains(printed.Entries, e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
    }

    [Theory]
    [InlineData("(set: $c to (set: $x to 1))")]
    [InlineData("(print: (set: $x to 1))")]
    [InlineData("(print: (put: 1 into $x))")]
    public void AnAssignment_CantBeUsedAsAValue(string source)
    {
      Assert.Contains("operation", Assert.Single(Errors(Render(source))));
    }

    [Theory]
    [InlineData("(if: false)[[b]]z", "z", 0)]
    [InlineData("(if: true)[[b]]", "b", 1)]
    [InlineData("(if: false) [[b]]x", " x", 0)]
    [InlineData("(for: each _i, 1, 2)[[b]]", "bb", 2)]
    [InlineData("(if: false)[[b]][hook]", "hook", 0)]
    [InlineData("(if: false) [[b]] [hook]", "  hook", 0)]
    [InlineData("(if: false)(link-goto:'b')z", "z", 0)]
    [InlineData("(text-style:'italic')+(if: false)[[b]]z", "z", 0)]
    [InlineData("(set: $c to (if: false))$c[[b]]", "", 0)]
    [InlineData("(set: $l to [[hello->b]])(if: false)$l", "", 0)]
    public void AChanger_AttachesToALink(string source, string expected, int links)
    {
      var result = Render(source);
      Assert.Empty(Errors(result));
      Assert.Equal(expected, Shown(result));
      Assert.Equal(links, Links(result).Count);
    }

    [Fact]
    public void AStyleChanger_WrapsTheLinkItAttachesTo()
    {
      var result = Render("(text-style:'italic')[[b]]");
      Assert.Empty(Errors(result));
      int style = result.Entries.FindIndex(e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
      int link = result.Entries.FindIndex(e => e.Kind == BufferedRenderOutput.Kind.Link);
      Assert.InRange(style, 0, link - 1);
    }

    [Theory]
    [InlineData("(if: false)(go-to: 'b')z")]
    [InlineData("(if: true)(go-to: 'b')z")]
    [InlineData("(text-style:'italic')(go-to: 'b')z")]
    public void AChanger_DoesntStopAGoto(string source)
    {
      Assert.Equal("B", new StorySession(Story(("Start", source), ("b", "B"))).Render().Text);
    }

    [Theory]
    [InlineData("|a>[x](if: false)(click-goto: ?a, 'b')z", "xz")]
    [InlineData("|a>[x](text-style:'italic')(mouseover-goto: ?a, 'b')z", "xz")]
    public void AChanger_DoesntStopAClickCommand(string source, string expected)
    {
      var result = Render(source);
      Assert.Empty(Errors(result));
      Assert.Equal(expected, result.Text);
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
    }

    [Theory]
    [InlineData("(text-style:'italic')(load-game: 'f')z", "z", "(load-game:) command")]
    [InlineData("|a>[x](text-style:'italic')(change: ?a, (text-style: 'bold'))z", "xz", "(change:) command")]
    [InlineData("|a>[x](if: false)(enchant: ?a, (text-style: 'bold'))z", "xz", "(enchant:) command")]
    [InlineData("(text-style:'italic')(out-data: 1)z", "z", "(output-data:) command")]
    [InlineData("(if: false)(error: 'x')z", "z", "(error:) command")]
    [InlineData("(if: false)(move: $a into $x)z", "z", "(move:) operation")]
    [InlineData("(if: false)(display: 'nowhere')z", "z", "'nowhere'")]
    [InlineData("(if: false)(display: 5)z", "z", "(display:)")]
    public void ACommandThatCantTakeAChanger_Errors(string source, string expected, string message)
    {
      var result = Render(source);
      Assert.Equal(expected, result.Text);
      Assert.Contains(message, Assert.Single(Errors(result)));
    }

    [Theory]
    [InlineData("(print: (goto: 5))")]
    [InlineData("(print: (goto: 'nowhere'))")]
    [InlineData("(print: (go-to:))")]
    [InlineData("(print: (display: 'nowhere'))")]
    [InlineData("(print: (load-game: 5))")]
    [InlineData("(print: (change: (font:'Skia'), 'baz'))")]
    [InlineData("(print: (change: ?a, 5))")]
    [InlineData("(print: (change: ?a, (append: ?b)))")]
    [InlineData("(print: (enchant: 5, (text-style: 'bold')))")]
    [InlineData("(print: (enchant: ?a))")]
    [InlineData("(print: (click-goto: 5, 'b'))")]
    [InlineData("(print: (click-goto: ?a, 5))")]
    [InlineData("(print: (click-goto: ?a, 'nowhere'))")]
    [InlineData("(print: (click-goto: '', 'b'))")]
    [InlineData("(print: (click-undo: ''))")]
    [InlineData("(print: (mouseout-undo: 5))")]
    [InlineData("(print: (link-undo: 'u', 5))")]
    [InlineData("(print: (link-goto: ''))")]
    [InlineData("(print: (print: 1, 2))")]
    public void ABadCommandCall_ErrorsWhenItsMade(string source)
    {
      var result = Render(source);
      Assert.Single(Errors(result));
      Assert.Equal("", Shown(result));
    }

    [Theory]
    [InlineData("(print: (load-game: 'never saved'))", "[A (load-game:) command]")]
    [InlineData("(print: (click-undo: ?a))", "[A (click-undo:) command]")]
    [InlineData("(print: (link-goto: 'a', 'nowhere'))", "[A (link-goto:) command]")]
    public void ProblemsThatDependOnRunning_WaitUntilItRuns(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Theory]
    [InlineData("(if: false)(load-game: 5)z", "z")]
    [InlineData("(if: false)(change: ?a, 5)z", "z")]
    [InlineData("(if: false)(enchant: 5, (text-style:'bold'))z", "z")]
    [InlineData("(if: false)(click-goto: ?a, 'nowhere')z", "z")]
    [InlineData("(if: false)(click-undo: 5)z", "z")]
    public void AHiddenCommand_StillReportsABadCall(string source, string expected)
    {
      var result = Render(source);
      Assert.Single(Errors(result));
      Assert.Equal(expected, Shown(result));
    }

    [Theory]
    [InlineData("(set: $a to 2)(text-style:'italic')(move: $a into $x)$a $x", "0 2")]
    [InlineData("(set: $a to 2)(text-style:'italic')(put: $a into $x)$x", "2")]
    [InlineData("(text-style:'italic')(unpack: (a: 1) into (a: $x))$x", "1")]
    public void AnAssignmentAfterAChanger_StillAssigns(string source, string expected)
    {
      var result = Render(source);
      Assert.Equal(expected, result.Text);
      Assert.Contains("operation can't have changers", Assert.Single(Errors(result)));
    }

    [Theory]
    [InlineData("(set: $m to (macro: [(output:)(print: 5)]))($m:)z", "z")]
    [InlineData("(set: $m to (macro: [(output:)[[b]]]))($m:)z", "z")]
    [InlineData("(set: $m to (macro: [(output:)(print: 5)q(output-data: 2)]))($m:)z", "z")]
    [InlineData("(set: $m to (macro: [(output:)+(text-style:'bold')(print: 5)]))($m:)z", "z")]
    [InlineData("(set: $n to (macro: [(output:)[w]]))(set: $m to (macro: [(output:)($n:)]))($m:)z", "wz")]
    [InlineData("(set: $c to (output:))$c(print: 5)z", "z")]
    public void OutputAttachedToACommand_CapturesItsHookAndNothingElse(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Theory]
    [InlineData("(set: $c to (macro: [(out:)[]]))($c:)z", "z")]
    [InlineData("(set: $c to (macro: [(out:)[]]))(print: ($c:))z", "[a custom command]z")]
    [InlineData("(set: $c to (macro: [(out:)[]]))(print: ($c:) is a command)", "true")]
    [InlineData("(set: $c to (macro: [(out:)[]q(out:)[n]]))($c:)z", "z")]
    [InlineData("(set: $c to (macro: [(if: true)[(out:)[]](out:)[n]]))($c:)z", "z")]
    [InlineData("(set: $c to (macro: [(if: false)[(out:)[]](out:)[n]]))($c:)z", "nz")]
    [InlineData("(set: $c to (macro: [(if: false)[(out:)[a]](else:)[(out:)[b]]]))($c:)z", "bz")]
    [InlineData("(set: $c to (macro: [(out:)[]]))(if: false)($c:)z", "z")]
    [InlineData("(set: $c to (macro: [(out:)[ ]]))($c:)z", " z")]
    [InlineData("(set: $l to (macro: [(out-data: (a:))]))(set: $c to (macro: [(set: _x to ($l:))(if: _x's length is 0)[(out:)[]](else:)[(out:)[n]]]))($c:)z", "z")]
    [InlineData("(set: $l to (macro: [(out-data: (a:1))]))(set: $c to (macro: [(set: _x to ($l:))(if: _x's length is 0)[(out:)[]](else:)[(out:)[n]]]))($c:)z", "nz")]
    public void AnEmptyOutputHook_IsAValidResult(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Fact]
    public void AnUnclosedCodeHook_ErrorsAndLaterLinesStillRender()
    {
      var result = Render("(print: [ )z\n(print: 5)''b''\n[hook]");
      Assert.Contains("no closing ']'", Assert.Single(Errors(result)));
      Assert.Equal("5b\nhook", Shown(result));
    }

    [Theory]
    [InlineData("|a>[x](set: $m to (macro: [(click-replace: ?a)[y](output-data: 1)]))($m:)", "x1", "y1")]
    [InlineData("|a>[x](set: $m to (macro: [(click-append: ?a)[y](output-data: 1)]))($m:)", "x1", "xy1")]
    [InlineData("|a>[x](set: $m to (macro: [(click: ?a)[y](output-data: 1)]))($m:)", "x1", "x1")]
    [InlineData("(set: $m to (macro: [(click-replace: ?a)[y](output-data: 1)]))($m:)|a>[x]", "1x", "1y")]
    [InlineData("|a>[x](set: $m to (macro: [(output:)[(click-replace: ?a)[y]]]))($m:)", "x", "y")]
    public void AClickInAMacroBody_ArmsThePage(string source, string before, string after)
    {
      var session = new StorySession(Story(("Start", source)));
      var result = session.Render();
      Assert.Equal(before, result.Text);
      var region = result.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
      Assert.Equal(after, session.DispatchEvent(region.Region.Id).Text);
    }

    [Theory]
    [InlineData("|a>[x](set: $m to (macro: [(replace: ?a)[new](output-data: 1)]))($m:)", "new1")]
    [InlineData("|a>[x](set: $m to (macro: [(append: ?a)[new](output-data: 1)]))($m:)", "xnew1")]
    public void ARevisionInAMacroBody_ChangesThePage(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Fact]
    public void ALinkInAMacroBody_StaysInTheDiscardedBody()
    {
      AssertRenders("(set: $m to (macro: [(link-goto: 'x', 'b')(output-data: 1)]))($m:)", "1");
    }

    [Fact]
    public void StoredCommands_SurviveASaveAndLoad()
    {
      var story = Story(
        ("Start", "(set: $l to [[hi->Next]], $p to (print: 'v'), $d to (display: 'Part'))[[Next]]"),
        ("Next", "$l $p $d"),
        ("Part", "shown"));
      var storage = new InMemorySaveStorage();
      var first = new StorySession(story, storage);
      first.Render();
      first.Goto("Next");
      Assert.True(first.SaveGame("slot"), first.LastSaveError);

      var second = new StorySession(story, storage);
      Assert.True(second.LoadGame("slot"), second.LastLoadError);
      var result = second.Render();
      Assert.Empty(Errors(result));
      Assert.Equal("hi v shown", Shown(result));
      Assert.Equal("Next", Assert.Single(Links(result)).Target);
    }

    [Theory]
    [InlineData("(print: [=x])", "")]
    [InlineData("(print: [==x])", "")]
    [InlineData("(set: $h to [=x)$h", "0")]
    public void AnUnclosedHookOpener_IsInvalidInAMacroCall(string source, string expected)
    {
      var result = Render(source);
      Assert.Equal(expected, result.Text);
      Assert.Contains("isn't valid Harlowe syntax", Assert.Single(Errors(result)));
    }

    [Theory]
    [InlineData("(print: (a: 1, 2) matches (a: ...num))", "true")]
    [InlineData("(print: (a: 1, 'x') matches (a: ...num))", "false")]
    [InlineData("(print: (a:) matches (a: ...num))", "false")]
    [InlineData("(print: (a: 1, 2, 'x') matches (a: ...num, str))", "true")]
    [InlineData("(print: (a: 'x') matches (a: ...num, str))", "true")]
    public void ASpreadDatatype_MatchesARunThatMayBeEmpty(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Theory]
    [InlineData("(unpack: (a: 1, 2, 3) into (a: $x, ...num))$x", "1")]
    [InlineData("(unpack: (a: 1, 2, 'c') into (a: ...num, $y))$y", "c")]
    [InlineData("(unpack: (a: 'c') into (a: ...num, $y))$y", "c")]
    public void ABareSpreadInAPattern_ChecksARunAndBindsNothing(string source, string expected)
    {
      AssertRenders(source, expected);
    }

    [Fact]
    public void ABareSpreadWithNothingLeft_NeedsMoreValues()
    {
      var result = Render("(unpack: (a: 1) into (a: $x, ...num))$x");
      Assert.Equal("0", result.Text);
      Assert.Contains("needs 1 more value", Assert.Single(Errors(result)));
    }

    [Theory]
    [InlineData("custom-command")]
    [InlineData("Custom_Command")]
    [InlineData("customcommand")]
    public void Partial_RefusesTheEngineOnlyMacroInAnySpelling(string spelling)
    {
      var result = Render($"(set: $m to (partial: '{spelling}', 1))");
      Assert.Contains("isn't the name of a built-in macro", Assert.Single(Errors(result)));
    }
  }
}
