using Harlowe.Runtime;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Tests for <see cref="HtmlRenderOutput"/> — the adapter that translates
  /// semantic <see cref="StyleSpec"/> events back to HTML for web consumers
  /// (and as the back-compat path for tests that assert HTML output).
  /// </summary>
  public class HtmlRenderOutputTests
  {
    private static (BufferedRenderOutput buf, IRenderOutput sink) NewSink()
    {
      var buf = new BufferedRenderOutput();
      return (buf, new HtmlRenderOutput(buf));
    }

    [Fact]
    public void Bold_EmitsBTags()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Bold = true });
      sink.Text("hi");
      sink.PopStyle();
      Assert.Equal("<b>hi</b>", buf.Text);
    }

    [Fact]
    public void Italic_EmitsITags()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Italic = true });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<i>x</i>", buf.Text);
    }

    [Fact]
    public void Underline_EmitsUTags()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Underline = true });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<u>x</u>", buf.Text);
    }

    [Fact]
    public void Strikethrough_EmitsSTags()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Strikethrough = true });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<s>x</s>", buf.Text);
    }

    [Fact]
    public void NestedPushes_NestTagsInOrder()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Bold = true });
      sink.PushStyle(new StyleSpec { Italic = true });
      sink.Text("x");
      sink.PopStyle();
      sink.PopStyle();
      Assert.Equal("<b><i>x</i></b>", buf.Text);
    }

    [Fact]
    public void MultipleFlagsOneSpec_EmitsMultipleTags()
    {
      // A single PushStyle with both bold and italic emits both <b> and <i>;
      // PopStyle closes both in reverse.
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Bold = true, Italic = true });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<b><i>x</i></b>", buf.Text);
    }

    [Fact]
    public void EmptySpec_EmitsNothing()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec());
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("x", buf.Text);
    }

    [Fact]
    public void Color_EmitsSpanWithStyle()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Color = "red" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"color: red;\">x</span>", buf.Text);
    }

    [Fact]
    public void BackgroundColor_EmitsSpanWithStyle()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { BackgroundColor = "yellow" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"background-color: yellow;\">x</span>", buf.Text);
    }

    [Fact]
    public void FontFamily_EmitsSpanWithStyle()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { FontFamily = "serif" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"font-family: serif;\">x</span>", buf.Text);
    }

    [Fact]
    public void FontSize_EmitsSpanWithStyle()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { FontSize = "120%" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"font-size: 120%;\">x</span>", buf.Text);
    }

    [Fact]
    public void FlagPlusValue_CollapsesToSingleSpan()
    {
      // When a value field is set, all flags fold into the same span's
      // style attribute rather than sandwiching the span between short tags.
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Bold = true, Color = "red" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"color: red; font-weight: bold;\">x</span>", buf.Text);
    }

    [Fact]
    public void UserSuppliedColor_IsAttributeEscaped()
    {
      // Defensive against a story variable like (text-color: $userInput) where
      // $userInput contains attribute-breaking characters.
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Color = "\"; alert(1) //" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("&quot;", buf.Text);
      Assert.DoesNotContain("alert(1) //\"", buf.Text);
    }

    [Fact]
    public void TextHtmlLinkError_PassThrough()
    {
      var (buf, sink) = NewSink();
      sink.Text("a");
      sink.Html("<br/>");
      sink.Link("text", "target");
      sink.Error("oops");
      Assert.Equal("a<br/>", buf.Text);
      // Link and Error stay on their own channels; not in the text stream.
      var entries = buf.Entries;
      Assert.Equal(BufferedRenderOutput.Kind.Link, entries[2].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.Error, entries[3].Kind);
    }

    [Fact]
    public void PopStyle_WhenStackEmpty_NoOp()
    {
      // Defensive — extra pop shouldn't crash. Should never happen in normal
      // use, but the renderer should be forgiving.
      var (buf, sink) = NewSink();
      sink.PopStyle();
      Assert.Equal(string.Empty, buf.Text);
    }

    [Fact]
    public void EscapeAttribute_HandlesAllFiveSpecials()
    {
      Assert.Equal("&amp;", HtmlRenderOutput.EscapeAttribute("&"));
      Assert.Equal("&lt;", HtmlRenderOutput.EscapeAttribute("<"));
      Assert.Equal("&gt;", HtmlRenderOutput.EscapeAttribute(">"));
      Assert.Equal("&quot;", HtmlRenderOutput.EscapeAttribute("\""));
      Assert.Equal("&#39;", HtmlRenderOutput.EscapeAttribute("'"));
    }

    [Fact]
    public void EscapeAttribute_NullAndEmpty()
    {
      Assert.Equal(string.Empty, HtmlRenderOutput.EscapeAttribute(null));
      Assert.Equal(string.Empty, HtmlRenderOutput.EscapeAttribute(string.Empty));
    }
  }
}
