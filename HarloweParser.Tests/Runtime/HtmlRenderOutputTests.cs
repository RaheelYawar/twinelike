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
    public void BeginLink_ForwardsTyped_LabelEscapedByTextChannel()
    {
      var (buf, sink) = NewSink();
      sink.BeginLink("P2");
      sink.Text("a<b");
      sink.EndLink();
      Assert.Equal(BufferedRenderOutput.Kind.BeginLink, buf.Entries[0].Kind);
      Assert.Equal("P2", buf.Entries[0].Target);
      Assert.Equal("a&lt;b", buf.Entries[1].Content);
      Assert.Equal(BufferedRenderOutput.Kind.EndLink, buf.Entries[2].Kind);
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
    public void Text_EscapesAngleBracketsAndAmpersand()
    {
      // The adapter emits HTML structure around prose; literal `<`, `>`, `&`
      // in story text would otherwise be eaten or misrendered by the browser.
      var (buf, sink) = NewSink();
      sink.Text("2 < 3 && 4 > 1");
      Assert.Equal("2 &lt; 3 &amp;&amp; 4 &gt; 1", buf.Text);
    }

    [Fact]
    public void Text_EscapesEmbeddedMarkupSoItCannotInject()
    {
      // A story variable rendered through Text() must not be able to inject
      // live HTML — even though IF authoring rarely sees untrusted content,
      // the adapter still treats Text as text-context, not raw HTML.
      var (buf, sink) = NewSink();
      sink.Text("<img src=x onerror=alert(1)>");
      Assert.Equal("&lt;img src=x onerror=alert(1)&gt;", buf.Text);
      Assert.DoesNotContain("<img", buf.Text);
    }

    [Fact]
    public void Html_StaysRawForAuthorEmbeddedMarkup()
    {
      // Html() is the explicit raw-passthrough channel; author HTML in
      // passage source reaches the browser intact.
      var (buf, sink) = NewSink();
      sink.Html("<br/>");
      Assert.Equal("<br/>", buf.Text);
    }

    [Fact]
    public void Text_InsideStyleSpan_IsStillEscaped()
    {
      // Escaping has to happen regardless of surrounding style tags — a span
      // wrapper doesn't change the content context.
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Bold = true });
      sink.Text("a < b");
      sink.PopStyle();
      Assert.Equal("<b>a &lt; b</b>", buf.Text);
    }

    [Fact]
    public void Text_NullOrEmpty_IsHandled()
    {
      var (buf, sink) = NewSink();
      sink.Text(string.Empty);
      sink.Text(null);
      Assert.Equal(string.Empty, buf.Text);
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

    // --- Interactive regions ---

    [Fact]
    public void BeginInteractive_EmitsAnchorWithRegionIdAndKind()
    {
      var (buf, sink) = NewSink();
      sink.BeginInteractive(new InteractiveRegion { Id = "r-3", Kind = InteractionKind.Click });
      sink.Text("cake");
      sink.EndInteractive();
      Assert.Equal("<a href=\"javascript:void(0)\" data-region-id=\"r-3\" data-interaction=\"Click\">cake</a>", buf.Text);
    }

    [Fact]
    public void BeginInteractive_EscapesUserSuppliedAttributeValues()
    {
      // Defensive: a hostile region id can't break out of the attribute.
      var (buf, sink) = NewSink();
      sink.BeginInteractive(new InteractiveRegion { Id = "\"><script>", Kind = InteractionKind.MouseOver });
      sink.Text("x");
      sink.EndInteractive();
      Assert.Contains("data-region-id=\"&quot;&gt;&lt;script&gt;\"", buf.Text);
    }

    [Fact]
    public void BeginInteractive_NullRegion_StillHonoursConfiguredHref()
    {
      // The defensive null-region path used to emit a bare `<a>` and silently
      // drop the configured _interactiveHref, defeating a CSP-strict consumer's
      // href override on the one path it most needed to apply. Now the
      // anchor is emitted with the configured href and empty data-* values
      // so the consumer's click-dispatch script sees a uniform shape.
      var (buf, sink) = NewSink();
      sink.BeginInteractive(null);
      sink.Text("x");
      sink.EndInteractive();
      Assert.Contains("href=\"javascript:void(0)\"", buf.Text);
      Assert.Contains("data-region-id=\"\"", buf.Text);
      Assert.EndsWith(">x</a>", buf.Text);
    }

    [Fact]
    public void BeginInteractive_NullRegion_EmitsEmptyDataInteraction()
    {
      // The defensive null-region path used to stamp `data-interaction="Click"`
      // (the default(InteractionKind) value) onto the anchor — making it
      // indistinguishable from a real click region to a dispatch script that
      // selects on `[data-interaction="Click"]`. Now the attribute is empty
      // so scripts can tell the placeholder anchor apart from a real one.
      var (buf, sink) = NewSink();
      sink.BeginInteractive(null);
      sink.EndInteractive();
      Assert.Contains("data-interaction=\"\"", buf.Text);
      Assert.DoesNotContain("data-interaction=\"Click\"", buf.Text);
    }

    [Fact]
    public void BeginInteractive_ConfigurableHref_OverridesDefault()
    {
      // A CSP-strict consumer can substitute an alternative href via the
      // constructor — empty string omits the attribute entirely.
      var buf = new BufferedRenderOutput();
      var sink = new HtmlRenderOutput(buf, "#");
      sink.BeginInteractive(new InteractiveRegion { Id = "r-1", Kind = InteractionKind.Click });
      sink.EndInteractive();
      Assert.Contains("href=\"#\"", buf.Text);
      Assert.DoesNotContain("javascript:", buf.Text);
    }

    [Fact]
    public void BeginInteractive_EmptyHref_OmitsTheAttributeEntirely()
    {
      var buf = new BufferedRenderOutput();
      var sink = new HtmlRenderOutput(buf, string.Empty);
      sink.BeginInteractive(new InteractiveRegion { Id = "r-1", Kind = InteractionKind.Click });
      sink.EndInteractive();
      Assert.DoesNotContain("href=", buf.Text);
      Assert.Contains("data-region-id=\"r-1\"", buf.Text);
    }

    [Fact]
    public void BeginInteractive_WhitespaceHref_TreatedAsEmpty()
    {
      // A whitespace-only configured href used to slip past the empty check
      // and emit `href=" "`, which browsers treat as a same-page navigation
      // on click — reloading the page and destroying session state. Now
      // collapses to the same omit-the-attribute path empty triggers.
      var buf = new BufferedRenderOutput();
      var sink = new HtmlRenderOutput(buf, "  ");
      sink.BeginInteractive(new InteractiveRegion { Id = "r-1", Kind = InteractionKind.Click });
      sink.EndInteractive();
      Assert.DoesNotContain("href=", buf.Text);
    }

    [Fact]
    public void Constructor_RejectsHtmlRenderOutputAsInner()
    {
      // Composing two HtmlRenderOutput adapters double-escapes Text/Error
      // while passing Html/PushStyle through once — the asymmetric corruption
      // produces `&amp;lt;` artifacts that are hard to track down. Surface
      // the misuse at construction time.
      var buf = new BufferedRenderOutput();
      var inner = new HtmlRenderOutput(buf);
      Assert.Throws<System.ArgumentException>(() => new HtmlRenderOutput(inner));
    }

    [Fact]
    public void Link_EscapesDisplayTextButPassesTargetThroughForSemanticResolution()
    {
      // Defense-in-depth: link display text is user-controlled prose and gets
      // the same Text-style escape so a downstream sink mirroring it into
      // innerHTML can't be XSSed. The target stays raw because it is a
      // passage-name reference the consumer resolves semantically; consumers
      // embedding it in an href attribute must attribute-escape at that point.
      var (buf, sink) = NewSink();
      sink.Link("<click>", "P\"><script>");
      var link = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Link);
      Assert.NotNull(link);
      Assert.Equal("&lt;click&gt;", link.Content);
      Assert.Equal("P\"><script>", link.Target);
    }

    [Fact]
    public void Error_HtmlEscapesMessage()
    {
      // Error messages embed user-controlled strings (variable names, datamap
      // keys, validator-rejected chars). A consumer that mirrors error
      // entries into innerHTML must not be XSSable through them.
      var (buf, sink) = NewSink();
      sink.Error("<img src=x onerror=alert(1)>");
      var err = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Equal("&lt;img src=x onerror=alert(1)&gt;", err.Content);
    }

    // --- v3.1 styling slice: new value fields ---

    [Fact]
    public void BackgroundImage_EmitsBackgroundImageAndSize()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { BackgroundImage = "sky.png" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"background-image: url(sky.png); background-size: cover;\">x</span>", buf.Text);
    }

    [Fact]
    public void BackgroundImage_UserSuppliedValue_IsEscaped()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { BackgroundImage = "\"); evil(" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("&quot;", buf.Text);
    }

    [Fact]
    public void Opacity_EmitsOpacityDeclaration()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Opacity = 0.5 });
      sink.Text("x");
      sink.PopStyle();
      Assert.Equal("<span style=\"opacity: 0.5;\">x</span>", buf.Text);
    }

    [Theory]
    [InlineData(TextAlignment.Left, "left")]
    [InlineData(TextAlignment.Center, "center")]
    [InlineData(TextAlignment.Right, "right")]
    [InlineData(TextAlignment.Justify, "justify")]
    public void Alignment_EmitsTextAlign(TextAlignment a, string cssValue)
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Alignment = a });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("text-align: " + cssValue + ";", buf.Text);
    }

    [Fact]
    public void OffCentreAlignment_EmitsMarginAndMaxWidth()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Alignment = TextAlignment.Center, AlignCenterOffsetPercent = 17 });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("text-align: center;", buf.Text);
      Assert.Contains("margin-left: 17%;", buf.Text);
      Assert.Contains("max-width: 50%;", buf.Text);
      Assert.Contains("display: block;", buf.Text);
    }

    [Fact]
    public void BalancedCentreAlignment_HasNoMargin()
    {
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Alignment = TextAlignment.Center });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("text-align: center;", buf.Text);
      Assert.DoesNotContain("margin-left", buf.Text);
    }

    // --- v3.1: effects ---

    [Fact]
    public void Mark_EmitsHighlighterBackground()
    {
      var (buf, sink) = NewSink();
      var s = new StyleSpec();
      s.Effects.Add(TextEffect.Mark);
      sink.PushStyle(s);
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("background-color: hsla(50, 100%, 50%, 0.5);", buf.Text);
    }

    [Fact]
    public void Blur_EmitsTransparentColorAndTextShadow()
    {
      var (buf, sink) = NewSink();
      var s = new StyleSpec();
      s.Effects.Add(TextEffect.Blur);
      sink.PushStyle(s);
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("color: transparent;", buf.Text);
      Assert.Contains("text-shadow:", buf.Text);
    }

    [Fact]
    public void Mirror_EmitsTransformScaleX()
    {
      var (buf, sink) = NewSink();
      var s = new StyleSpec();
      s.Effects.Add(TextEffect.Mirror);
      sink.PushStyle(s);
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("transform: scaleX(-1);", buf.Text);
      Assert.Contains("display: inline-block;", buf.Text);
    }

    [Theory]
    [InlineData(TextEffect.Blink, "harlowe-blink")]
    [InlineData(TextEffect.FadeInOut, "harlowe-fade-in-out")]
    [InlineData(TextEffect.Shudder, "harlowe-shudder")]
    [InlineData(TextEffect.Rumble, "harlowe-rumble")]
    [InlineData(TextEffect.Sway, "harlowe-sway")]
    [InlineData(TextEffect.Buoy, "harlowe-buoy")]
    [InlineData(TextEffect.Fidget, "harlowe-fidget")]
    public void Animations_EmitAnimationDeclaration(TextEffect effect, string keyframeName)
    {
      var (buf, sink) = NewSink();
      var s = new StyleSpec();
      s.Effects.Add(effect);
      sink.PushStyle(s);
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("animation: " + keyframeName, buf.Text);
    }

    [Fact]
    public void FlagPlusEffect_CollapsesToSingleSpan()
    {
      // Bold + a mark effect should fold into one span, not bold-tags
      // around a span — the effect fields force the value-field path.
      var (buf, sink) = NewSink();
      var s = new StyleSpec { Bold = true };
      s.Effects.Add(TextEffect.Mark);
      sink.PushStyle(s);
      sink.Text("x");
      sink.PopStyle();
      Assert.StartsWith("<span style=\"", buf.Text);
      Assert.Contains("background-color: hsla(50, 100%, 50%, 0.5);", buf.Text);
      Assert.Contains("font-weight: bold;", buf.Text);
      Assert.EndsWith("</span>", buf.Text);
    }

    [Theory]
    [InlineData(TextEffect.Superscript, "vertical-align: super;")]
    [InlineData(TextEffect.Subscript, "vertical-align: sub;")]
    [InlineData(TextEffect.Condense, "letter-spacing: -0.08em;")]
    [InlineData(TextEffect.Expand, "letter-spacing: 0.08em;")]
    [InlineData(TextEffect.Outline, "1px 1px 0 #fff")]
    [InlineData(TextEffect.Shadow, "text-shadow: 0.08em 0.08em 0.08em #000;")]
    [InlineData(TextEffect.Emboss, "text-shadow: 0 1px 0 #fff, 0 -1px 0 #000;")]
    [InlineData(TextEffect.Smear, "0 0 0.08em currentColor")]
    [InlineData(TextEffect.Blurrier, "text-shadow: 0 0 0.2em currentColor;")]
    [InlineData(TextEffect.UpsideDown, "transform: scaleY(-1);")]
    public void Effect_EmitsItsCssRecipe(TextEffect effect, string expectedCss)
    {
      var (buf, sink) = NewSink();
      var s = new StyleSpec();
      s.Effects.Add(effect);
      sink.PushStyle(s);
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains(expectedCss, buf.Text);
    }

    [Fact]
    public void UnderlineAndStrike_FoldIntoOneTextDecoration_OnTheSpanPath()
    {
      // A value field forces the span path, where the two decoration flags
      // must merge into a single text-decoration declaration.
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Underline = true, Strikethrough = true, Color = "red" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("text-decoration: underline line-through;", buf.Text);
      Assert.Contains("color: red;", buf.Text);
    }

    [Fact]
    public void SuperscriptFlag_FoldsIntoSpan_WhenValueFieldPresent()
    {
      // Flag-only superscript takes the <sup> short-tag path; alongside a
      // value field it folds into the span as the parallel CSS recipe.
      var (buf, sink) = NewSink();
      sink.PushStyle(new StyleSpec { Superscript = true, Color = "red" });
      sink.Text("x");
      sink.PopStyle();
      Assert.Contains("vertical-align: super;", buf.Text);
      Assert.Contains("font-size: 0.83em;", buf.Text);
    }
  }
}
