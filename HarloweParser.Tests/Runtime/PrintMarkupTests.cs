using System.Text;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Printed strings render as markup, as in reference's <c>#runExpression</c>:
  /// <c>(print:)</c>, <c>$var</c>, and any string a macro returns in prose.
  /// </summary>
  public class PrintMarkupTests
  {
    private static StorySession Session(string start, params (string name, string body)[] others)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "Start", RawBody = start });
      foreach (var (name, body) in others)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage("Start").Pid;
      return new StorySession(story);
    }

    private static string Errors(RenderResult result)
    {
      var sb = new StringBuilder();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) sb.Append(entry.Content).Append('\n');
      return sb.ToString();
    }

    private static bool HasBold(RenderResult result, string text)
    {
      int depth = 0;
      foreach (var entry in result.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.PushStyle && entry.Style.Bold) depth++;
        else if (entry.Kind == BufferedRenderOutput.Kind.PopStyle && depth > 0) depth--;
        else if (entry.Kind == BufferedRenderOutput.Kind.Text && depth > 0 && entry.Content == text) return true;
      }
      return false;
    }

    private static BufferedRenderOutput.Entry FirstLink(RenderResult result)
    {
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Link) return entry;
      return null;
    }

    [Theory]
    [InlineData("(print: \"''bold''\")")]
    [InlineData("(set: $v to \"''bold''\")$v")]
    [InlineData("(set: _v to \"''bold''\")_v")]
    [InlineData("(text: \"''bold''\")")]
    [InlineData("(print: (a: \"''bold''\"))")]
    public void PrintedStrings_RenderMarkup(string source)
    {
      var result = Session(source).Render();
      Assert.Equal("bold", result.Text);
      Assert.True(HasBold(result, "bold"));
    }

    [Theory]
    [InlineData("(print: \"[[Go->Next]]\")")]
    [InlineData("(set: $v to \"[[Go->Next]]\")$v")]
    public void PrintedLinks_AreRealLinks(string source)
    {
      var session = Session(source, ("Next", "arrived"));
      var link = FirstLink(session.Render());
      Assert.NotNull(link);
      Assert.Equal("Go", link.Content);
      Assert.Equal("Next", link.Target);
      Assert.Equal("arrived", session.Goto(link.Target).Text);
    }

    [Fact]
    public void PrintedMacroCalls_Run()
      => Assert.Equal("2", Session("(print: \"(print: 1+1)\")").Render().Text);

    [Fact]
    public void PrintedVariables_Resolve()
      => Assert.Equal("Hi Ann", Session("(set: $n to \"Ann\")(set: $g to \"Hi $n\")$g").Render().Text);

    [Fact]
    public void PrintedAssignments_ChangeStoryState()
      => Assert.Equal("5", Session("(print: \"(set: $x to 5)\")$x").Render().Text);

    [Fact]
    public void PrintedHooks_CanBeTargeted()
      => Assert.Equal("new", Session("(print: \"|h>[old]\")(replace: ?h)[new]").Render().Text);

    [Fact]
    public void PrintedMarkup_HasItsOwnTempScope()
    {
      var result = Session("(print: \"(set: _t to 1)\")_t").Render();
      Assert.Contains("_t is not set", Errors(result));
    }

    [Fact]
    public void PrintedMarkup_SeesOuterTemps()
      => Assert.Equal("7", Session("(set: _t to 7)(print: \"_t\")").Render().Text);

    [Fact]
    public void PrintedElse_DoesNotPairWithAnOuterIf()
    {
      // A printed string is its own conditional frame, as in reference.
      var result = Session("(if: false)[a](print: \"(else:)[b]\")(else:)[c]").Render();
      Assert.Equal("c", result.Text);
    }

    [Fact]
    public void PrintedMarkup_FollowsTheStoryProfile()
    {
      Assert.Equal("a--b", Session("(print: \"a--b\")").Render().Text);

      var v4 = new HarloweStory { FormatVersion = "4.0.0" };
      v4.AddPassage(new HarlowePassage { Name = "Start", RawBody = "(print: \"a--b\")" });
      v4.StartNode = v4.GetPassage("Start").Pid;
      Assert.Equal("a", new StorySession(v4).Render().Text);
    }

    [Theory]
    [InlineData("(print: num)", "[the num datatype]")]
    [InlineData("(print: (text-style: \"bold\"))", "[A (text-style:) changer]")]
    [InlineData("(set: $p to (print: num))$p", "[the num datatype]")]
    [InlineData("(print: 12)", "12")]
    [InlineData("(print: true)", "true")]
    [InlineData("(print: (a: 1, num))", "1,[the num datatype]")]
    [InlineData("(print: (dm: \"k\", \"''v''\"))", "k:''v''")]
    [InlineData("(set: $d to (dm: \"k\", \"''v''\"))$d", "k:''v''")]
    public void NonStringValues_PrintLiterally(string source, string expected)
    {
      var result = Session(source).Render();
      Assert.Equal("", Errors(result));
      Assert.Equal(expected, result.Text);
    }

    [Theory]
    [InlineData("(print: num)", "[the num datatype]")]
    [InlineData("(print: (dm: \"k\", \"''v''\"))", "k:''v''")]
    [InlineData("(print: (text-style: \"bold\"))", "[A (text-style:) changer]")]
    public void StoredPrintedValues_StayLiteralThroughSaveAndLoad(string printed, string expected)
    {
      var session = Session("(set: $p to " + printed + ")[[Go->Next]]", ("Next", "$p"));
      session.Render();
      Assert.Equal(expected, session.Goto("Next").Text);

      Assert.True(session.SaveGame("slot"), session.LastSaveError);
      Assert.True(session.LoadGame("slot"), session.LastLoadError);

      var restored = session.Render();
      Assert.Equal("", Errors(restored));
      Assert.Equal(expected, restored.Text);
    }

    [Fact]
    public void PrintedChangerWithNoSource_RefusesToSave()
    {
      // A NaN argument has no source form, so this changer can't be recreated.
      // Saving it as a plain string would bring it back as markup.
      var session = Session("(set: $p to (print: (opacity: 0*(1e308*10))))[[Go->Next]]", ("Next", "$p"));
      session.Render();
      session.Goto("Next");

      Assert.False(session.SaveGame("slot"));
      Assert.NotNull(session.LastSaveError);
    }

    [Fact]
    public void PrintedHtml_ArrivesThroughTheHtmlChannel()
    {
      var result = Session("(print: \"<b>hi</b>\")").Render();

      Assert.Equal(new[] { BufferedRenderOutput.Kind.Html, BufferedRenderOutput.Kind.Text, BufferedRenderOutput.Kind.Html },
        System.Array.ConvertAll(result.Entries.ToArray(), e => e.Kind));
      Assert.Equal("<b>", result.Entries[0].Content);
      Assert.Equal("hi", result.Entries[1].Content);
    }

    [Theory]
    [InlineData("(print: false)[hook]", "falsehook")]
    [InlineData("(print: true)[hook]", "truehook")]
    public void PrintedBooleans_DoNotActAsAttachedConditions(string source, string expected)
      => Assert.Equal(expected, Session(source).Render().Text);

    [Theory]
    [InlineData("plain text")]
    [InlineData("it's fine")]
    [InlineData("a/b ~ c ^ d - e")]
    public void StringsWithoutMarkup_PrintUnchanged(string text)
      => Assert.Equal(text, Session("(print: \"" + text + "\")").Render().Text);

    [Fact]
    public void PrintedUrl_KeepsItsSlashes()
      => Assert.Equal("see http://example.com/a", Session("(print: \"see http://example.com/a\")").Render().Text);

    [Fact]
    public void PlainStrings_DoNotSpendTheBudget()
    {
      var body = new StringBuilder("(set: $n to (a:");
      for (int i = 0; i < 20000; i++) body.Append(i == 0 ? "1" : ",1");
      body.Append("))(set: $s to \"x\")(for: each _i, ...$n)[$s]");

      var result = Session(body.ToString()).Render();

      Assert.Equal("", Errors(result));
      Assert.Equal(20000, result.Text.Length);
    }
  }
}
