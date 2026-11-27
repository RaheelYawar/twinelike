using System.Collections.Generic;
using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>The <c>(transition:)</c> family and time literals. Expected text and HTML were rendered by the real 3.3.9 build.</summary>
  public class TransitionMacroTests
  {
    private static RenderResult Render(string body)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "test", RawBody = body });
      story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
      story.StartNode = story.GetPassage("test").Pid;
      return new StorySession(story).Render();
    }

    private static string Shown(RenderResult result)
    {
      var text = new System.Text.StringBuilder();
      foreach (var entry in result.Entries)
      {
        if (entry.Kind == BufferedRenderOutput.Kind.Text || entry.Kind == BufferedRenderOutput.Kind.Link) text.Append(entry.Content);
        else if (entry.Kind == BufferedRenderOutput.Kind.Error) text.Append("!");
      }
      return text.ToString();
    }

    private static List<StyleSpec> Styles(RenderResult result)
    {
      var list = new List<StyleSpec>();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.PushStyle) list.Add(entry.Style);
      return list;
    }

    private static string FirstError(RenderResult result)
      => result.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error)?.Content;

    [Theory]
    [InlineData("(t8n: \"dissolve\")[x]", "dissolve")]
    [InlineData("(transition: \"Slide-Left\")[x]", "slideleft")]
    [InlineData("(t8n: \"slide_left\")[x]", "slideleft")]
    [InlineData("(t8n: \"SLIDELEFT\")[x]", "slideleft")]
    [InlineData("(t8n: \"blur\")[x]", "blur")]
    [InlineData("(t8n: \"dissolve\") + (t8n: \"shudder\")[x]", "shudder")]
    [InlineData("(set: $t to (t8n: \"pulse\"))$t[x]", "pulse")]
    [InlineData("(t8n: \"dissolve\")|a>[x]", "dissolve")]
    public void NamesTheTransition(string source, string name)
    {
      var result = Render(source);
      Assert.Equal("x", Shown(result));
      var styles = Styles(result);
      Assert.Single(styles);
      Assert.Equal(name, styles[0].Transition);
    }

    [Fact]
    public void ComposedTransitionFieldsAreOneInnermostLayer()
    {
      var result = Render("(t8n: \"fade\")+(t8n-time: .5s)+(text-style: \"bold\")+(t8n-delay: .5s)[x]");
      var styles = Styles(result);
      Assert.Equal(2, styles.Count);
      Assert.True(styles[0].Bold);
      Assert.Equal("fade", styles[1].Transition);
      Assert.Equal(500, styles[1].TransitionTime);
      Assert.Equal(500, styles[1].TransitionDelay);
      Assert.Null(styles[1].TransitionSkip);
    }

    [Theory]
    [InlineData("(t8n-time: 2s)[x]", 0)]
    [InlineData("(t8n-delay: 2s)+(t8n-skip: 1s)[x]", 0)]
    [InlineData("(text-style: \"bold\")+(t8n-time: 2s)[x]", 1)]
    public void ATimeAloneAddsNoLayerToAHook(string source, int layers)
    {
      // Reference adds its transition container only when a transition is named.
      var result = Render(source);
      Assert.Equal(layers, Styles(result).Count);
      Assert.DoesNotContain(Styles(result), s => s.TransitionTime != null || s.TransitionDelay != null);
    }

    [Theory]
    [InlineData("(t8n-time: 2s)[[b]]")]
    [InlineData("(t8n-time: 2s)(link-goto: \"b\")")]
    [InlineData("(enchant: ?link, (t8n-time: 2s))[[b]]")]
    public void ATimeAloneReachesAPassageLink(string source)
    {
      // (transition-time:) also sets how long the passage transition takes.
      var styles = Styles(Render(source));
      Assert.Single(styles);
      Assert.Equal(2000, styles[0].TransitionTime);
      Assert.Null(styles[0].Transition);
    }

    [Fact]
    public void ADelayAloneDoesntReachAPassageLink()
      => Assert.Empty(Styles(Render("(t8n-delay: 2s)[[b]]")));

    [Fact]
    public void TextStyleNoneLeavesTheTransition()
    {
      var styles = Styles(Render("(t8n: \"fade\")+(text-style: \"none\")[x]"));
      Assert.Single(styles);
      Assert.Equal("fade", styles[0].Transition);
    }

    [Theory]
    [InlineData("(t8n: \"nope\")[x]", "The string \"nope\" is not a valid name string for the (t8n:) macro. Only the following names are recognised (capitalisation and hyphens ignored): instant, dissolve, fade, rumble, shudder, pulse, zoom, flicker, slideleft, slideright, slideup, slidedown, fadeleft, faderight, fadeup, fadedown, blur.")]
    [InlineData("(t8n: 1)[x]", "The number 1 is not a valid name string for the (t8n:) macro.")]
    [InlineData("(t8n-time: 0s)[x]", "The (t8n-time:) macro's 1st value is the number 0, but should be a positive number.")]
    [InlineData("(t8n-time: -1s)[x]", "The (t8n-time:) macro's 1st value is the number -1000, but should be a positive number.")]
    [InlineData("(t8n-time: \"1s\")[x]", "The (t8n-time:) macro's 1st value is the string \"1s\", but should be a positive number.")]
    [InlineData("(t8n-delay: -1)[x]", "The (t8n-delay:) macro's 1st value is the number -1, but should be a number between 0 and Infinity.")]
    [InlineData("(t8n-skip: 0)[x]", "The (t8n-skip:) macro's 1st value is the number 0, but should be a positive number.")]
    [InlineData("(t8n-arrive: \"nope\")[[b]]", "The string \"nope\" is not a valid name string for the (t8n-arrive:) macro.")]
    public void Refusals(string source, string message)
    {
      var result = Render(source);
      Assert.StartsWith(message, FirstError(result));
    }

    [Fact]
    public void ARefusedTransitionStillShowsItsHook()
    {
      Assert.Equal("!x", Shown(Render("(t8n: \"nope\")[x]")));
      Assert.Equal("!b", Shown(Render("(t8n-arrive: \"nope\")[[b]]")));
    }

    [Theory]
    [InlineData("(t8n: \"dissolve\")[[b]]")]
    [InlineData("(t8n: \"dissolve\")(link-goto: \"b\")")]
    public void APassageLinkRefusesAHookTransition(string source)
    {
      var result = Render(source);
      Assert.Equal("!", Shown(result));
      Assert.Equal("Please attach (transition-depart:) or (transition-arrive:) to a passage link, not (transition:).", FirstError(result));
    }

    [Fact]
    public void PassageTransitionsWrapTheLink()
    {
      var result = Render("(t8n-depart: \"dissolve\")+(t8n-arrive: \"instant\")+(t8n-time: 2s)[[b]]");
      Assert.Equal("b", Shown(result));
      var styles = Styles(result);
      Assert.Single(styles);
      Assert.Equal("dissolve", styles[0].TransitionDepart);
      Assert.Equal("instant", styles[0].TransitionArrive);
      Assert.Equal(2000, styles[0].TransitionTime);
    }

    [Theory]
    [InlineData("(t8n-time: 1s)[[b]]", "b")]
    [InlineData("(t8n-delay: 1s)[[b]]", "b")]
    [InlineData("(t8n-depart: \"dissolve\")[x]", "x")]
    [InlineData("(t8n: \"dissolve\")(click-goto: \"x\", \"b\")x", "x")]
    [InlineData("(t8n: \"dissolve\")(link: \"a\")[b]", "!b")]
    [InlineData("(t8n: \"dissolve\")", "!")]
    public void Attachment(string source, string expected) => Assert.Equal(expected, Shown(Render(source)));

    [Theory]
    [InlineData("(print: (t8n: \"dissolve\"))", "[A (transition:) changer]")]
    [InlineData("(print: (t8n-time: 1s))", "[A (transition-time:) changer]")]
    [InlineData("(print: (t8n-depart: 'fade'))", "[A (transition-depart:) changer]")]
    [InlineData("(print: (colour: red))", "[A (text-colour:) changer]")]
    [InlineData("(print: (bg: red))", "[A (background:) changer]")]
    [InlineData("(print: (size: 2))", "[A (text-size:) changer]")]
    [InlineData("(print: (loop: each _x, 1))", "[A (for:) changer]")]
    [InlineData("(print: (t8n: \"Dissolve\") is (t8n: \"dissolve\"))", "true")]
    [InlineData("(print: (t8n: \"fade\") is (t8n: \"dissolve\"))", "false")]
    public void PrintsAndCompares(string source, string expected) => Assert.Equal(expected, Shown(Render(source)));

    [Theory]
    [InlineData("(t8n: \"dissolve\")", "The (transition:) changer should be stored in a variable")]
    [InlineData("(either: (colour: red))", "The (text-colour:) changer should be stored in a variable")]
    [InlineData("(cond: true, (t8n: 'fade'), 1)", "The (transition:) changer should be stored in a variable")]
    [InlineData("(HIDDEN:)", "The (hidden:) changer should be stored in a variable")]
    public void ErrorsNameTheChangerByItsMainName(string source, string message)
      => Assert.StartsWith(message, FirstError(Render(source)));

    [Theory]
    [InlineData("(print: (FONT: 'x'))", "[A (font:) changer]")]
    [InlineData("(print: (TEXT_STYLE: 'bold'))", "[A (text-style:) changer]")]
    [InlineData("(print: (TEXTSTYLE: 'bold'))", "[A (text-style:) changer]")]
    [InlineData("(print: (Click_Replace: ?a))", "[A (click-replace:) changer]")]
    [InlineData("(print: (OUT:))", "[A (output:) changer]")]
    [InlineData("(print: (T8N: 'fade'))", "[A (transition:) changer]")]
    [InlineData("(print: (text-colour: red) + (FONT: 'x'))", "[A (text-colour:) changer]")]
    [InlineData("(set: $p to (partial: \"FONT\", \"x\"))(print: ($p:))", "[A (font:) changer]")]
    public void PrintsTheRegisteredSpelling(string source, string expected) => Assert.Equal(expected, Shown(Render(source)));

    [Fact]
    public void TheHtmlOutputNamesTheTransition()
    {
      var buffer = new BufferedRenderOutput();
      var html = new HtmlRenderOutput(buffer);
      html.PushStyle(new StyleSpec { Transition = "dissolve", TransitionTime = 500 });
      html.Text("x");
      html.PopStyle();
      html.PushStyle(new StyleSpec { Bold = true, TransitionDelay = 1000 });
      html.Text("y");
      html.PopStyle();
      Assert.Equal("<span data-t8n=\"dissolve\" data-t8n-time=\"500\">x</span><span style=\"font-weight: bold;\" data-t8n-delay=\"1000\">y</span>",
        buffer.Text);
    }

    [Theory]
    [InlineData("(print: 2s)", "2000")]
    [InlineData("(print: 500ms)", "500")]
    [InlineData("(print: .5s)", "500")]
    [InlineData("(print: 2S)", "2000")]
    [InlineData("(print: 2MS)", "2")]
    [InlineData("(print: 1.5s + 1)", "1501")]
    [InlineData("(print: 1.1s)", "1100")]
    [InlineData("(print: 1.s)", "1000")]
    [InlineData("(print: 0.5s)", "500")]
    [InlineData("(print: 10ms is 10)", "true")]
    [InlineData("(print: 2s+1)", "2001")]
    [InlineData("(print: .5)", "0.5")]
    [InlineData("(print: .5 + .25)", "0.75")]
    [InlineData("(print: 3 - .5)", "2.5")]
    [InlineData("(print: (a: .5, 1))", "0.5,1")]
    public void TimesAreMilliseconds(string source, string expected) => Assert.Equal(expected, Shown(Render(source)));

    [Fact]
    public void ATimeNeedsAWordBoundary() => Assert.Contains("!", Shown(Render("(print: 2sx)")));

    [Theory]
    [InlineData("(print: 1.)")]
    [InlineData("(print: ..2)")]
    [InlineData("(print: .5.)")]
    [InlineData("(set: $a to (a: 1))(print: (a: ....$a))")]
    public void AStrayDotIsAnError(string source)
      => Assert.EndsWith("\".\" isn't valid Harlowe syntax for the inside of a macro call.", FirstError(Render(source)));

    [Fact]
    public void SpreadStillWorks() => Assert.Equal("1,2", Shown(Render("(set: $a to (a: 1, 2))(print: (a: ...$a))")));
  }
}
