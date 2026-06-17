using System.Globalization;
using System.Threading;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the v3.1 styling-changer slice: <c>(text-color:)</c> /
  /// <c>(text-colour:)</c> / <c>(color:)</c> / <c>(colour:)</c>,
  /// <c>(background:)</c> / <c>(bg:)</c>, <c>(font:)</c>,
  /// <c>(text-size:)</c> / <c>(size:)</c>, <c>(opacity:)</c>, and
  /// <c>(align:)</c>. Each macro is a thin <see cref="StyleSpec"/> producer;
  /// the tests confirm the right semantic field lands on the emitted spec and
  /// that argument validation errors render in prose.
  /// </summary>
  public class StylingMacroTests
  {
    private static BufferedRenderOutput RenderRaw(string source)
    {
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = registry };
      registry.Context = ctx;

      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(ast);
      return buf;
    }

    private static StyleSpec FirstPushedStyle(BufferedRenderOutput buf)
    {
      var push = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.PushStyle);
      Assert.NotNull(push);
      return push.Style;
    }

    private static void AssertError(BufferedRenderOutput buf, string contains)
    {
      var err = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Contains(contains, err.Content);
    }

    // --- (text-color:) and aliases ---

    [Theory]
    [InlineData("text-color")]
    [InlineData("text-colour")]
    [InlineData("color")]
    [InlineData("colour")]
    public void TextColor_AllAliases_SetColor(string macroName)
    {
      var buf = RenderRaw("(" + macroName + ": \"red\")[hi]");
      Assert.Equal("red", FirstPushedStyle(buf).Color);
    }

    [Fact]
    public void TextColor_NonString_EmitsError()
    {
      var buf = RenderRaw("(text-color: 5)[hi]");
      AssertError(buf, "String");
    }

    // --- (background:) ---

    [Fact]
    public void Background_ColorString_SetsBackgroundColor()
    {
      var buf = RenderRaw("(background: \"navy\")[hi]");
      var s = FirstPushedStyle(buf);
      Assert.Equal("navy", s.BackgroundColor);
      Assert.Null(s.BackgroundImage);
    }

    [Fact]
    public void Background_ImageExtension_SetsBackgroundImage()
    {
      var buf = RenderRaw("(background: \"sky.png\")[hi]");
      var s = FirstPushedStyle(buf);
      Assert.Equal("sky.png", s.BackgroundImage);
      Assert.Null(s.BackgroundColor);
    }

    [Fact]
    public void Background_HttpUrl_SetsBackgroundImage()
    {
      var buf = RenderRaw("(background: \"https://example.com/bg\")[hi]");
      var s = FirstPushedStyle(buf);
      Assert.Equal("https://example.com/bg", s.BackgroundImage);
    }

    [Fact]
    public void Background_BgAlias_Works()
    {
      var buf = RenderRaw("(bg: \"yellow\")[hi]");
      Assert.Equal("yellow", FirstPushedStyle(buf).BackgroundColor);
    }

    // --- (font:) ---

    [Fact]
    public void Font_SetsFontFamily()
    {
      var buf = RenderRaw("(font: \"Times New Roman\")[hi]");
      Assert.Equal("Times New Roman", FirstPushedStyle(buf).FontFamily);
    }

    [Fact]
    public void Font_NonString_EmitsError()
    {
      var buf = RenderRaw("(font: 5)[hi]");
      AssertError(buf, "String");
    }

    // --- (text-size:) ---

    [Fact]
    public void TextSize_NumberMultiplier_StoredAsEm()
    {
      var buf = RenderRaw("(text-size: 1.5)[hi]");
      Assert.Equal("1.5em", FirstPushedStyle(buf).FontSize);
    }

    [Fact]
    public void TextSize_SizeAlias_Works()
    {
      var buf = RenderRaw("(size: 2)[hi]");
      Assert.Equal("2em", FirstPushedStyle(buf).FontSize);
    }

    [Fact]
    public void TextSize_NonNumber_EmitsError()
    {
      var buf = RenderRaw("(text-size: \"big\")[hi]");
      AssertError(buf, "Number");
    }

    // --- (opacity:) ---

    [Fact]
    public void Opacity_SetsOpacity()
    {
      var buf = RenderRaw("(opacity: 0.25)[hi]");
      Assert.Equal(0.25, FirstPushedStyle(buf).Opacity);
    }

    [Fact]
    public void Opacity_Zero_IsAllowed()
    {
      var buf = RenderRaw("(opacity: 0)[hi]");
      Assert.Equal(0.0, FirstPushedStyle(buf).Opacity);
    }

    [Fact]
    public void Opacity_One_IsAllowed()
    {
      var buf = RenderRaw("(opacity: 1)[hi]");
      Assert.Equal(1.0, FirstPushedStyle(buf).Opacity);
    }

    [Fact]
    public void Opacity_OutOfRange_EmitsError()
    {
      var buf = RenderRaw("(opacity: 1.5)[hi]");
      AssertError(buf, "between 0 and 1");
    }

    [Fact]
    public void Opacity_Negative_EmitsError()
    {
      var buf = RenderRaw("(opacity: -0.1)[hi]");
      AssertError(buf, "between 0 and 1");
    }

    [Fact]
    public void Opacity_OutOfRange_MessageUsesInvariantNumber()
    {
      // The interpolated value must format invariantly (".") regardless of host
      // locale, consistent with every other Harlowe number formatting (cf.
      // HarloweValueTests.ToHarloweString_NumberUsesInvariantCulture).
      var prior = Thread.CurrentThread.CurrentCulture;
      try
      {
        Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
        var buf = RenderRaw("(opacity: 1.5)[hi]");
        AssertError(buf, "got 1.5");
      }
      finally { Thread.CurrentThread.CurrentCulture = prior; }
    }

    // --- (align:) ---

    [Theory]
    [InlineData("<==", TextAlignment.Left)]
    [InlineData("==>", TextAlignment.Right)]
    [InlineData("=><=", TextAlignment.Center)]
    [InlineData("<==>", TextAlignment.Justify)]
    // Arbitrary-length arrows map by shape, matching the reference regex.
    [InlineData("<======", TextAlignment.Left)]
    [InlineData("======>", TextAlignment.Right)]
    [InlineData("<=====>", TextAlignment.Justify)]
    [InlineData("==><==", TextAlignment.Center)]   // balanced (longer) → centre
    public void Align_ArrowSyntax_MapsToEnum(string arrow, TextAlignment expected)
    {
      var buf = RenderRaw("(align: \"" + arrow + "\")[hi]");
      Assert.Equal(expected, FirstPushedStyle(buf).Alignment);
    }

    [Fact]
    public void Align_BalancedCentre_HasNoOffset()
    {
      var buf = RenderRaw("(align: \"=><=\")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal(TextAlignment.Center, style.Alignment);
      Assert.Null(style.AlignCenterOffsetPercent);
    }

    [Theory]
    // The documented example "=><==" leans left of centre.
    [InlineData("=><==", 17)]   // round(1/3*50)
    [InlineData("==><=", 33)]   // round(2/3*50)
    public void Align_OffCentre_CarriesCalibratedMarginPercent(string arrow, int expectedPercent)
    {
      var buf = RenderRaw("(align: \"" + arrow + "\")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal(TextAlignment.Center, style.Alignment);
      Assert.Equal(expectedPercent, style.AlignCenterOffsetPercent);
    }

    [Fact]
    public void Align_DocumentedOffCentreExample_DoesNotError()
    {
      // Reference's own usage example — used to error in ours.
      var buf = RenderRaw("(align: \"=><==\")[hi]");
      Assert.Equal(TextAlignment.Center, FirstPushedStyle(buf).Alignment);
    }

    [Fact]
    public void Align_UnknownArrow_EmitsError()
    {
      // "===" has no direction marker — rejected by the arrow regex.
      var buf = RenderRaw("(align: \"===\")[hi]");
      AssertError(buf, "alignment");
    }

    [Fact]
    public void Align_NonString_EmitsError()
    {
      var buf = RenderRaw("(align: 5)[hi]");
      AssertError(buf, "String");
    }

    // --- CSS-injection guard ---

    [Theory]
    [InlineData("(text-color: \"red; background: blue\")[hi]")]
    [InlineData("(background: \"red; color: white\")[hi]")]
    [InlineData("(font: \"Arial; color: red\")[hi]")]
    [InlineData("(text-color: \"red\\n;hax\")[hi]")]
    [InlineData("(text-color: \"red}body{hax:1\")[hi]")]
    public void StyleValueWithCssStructuralChar_EmitsErrorAndDoesNotPushStyle(string source)
    {
      // A story variable holding `red; background: url(//evil)` must not
      // inject extra declarations into the style="..." attribute. Validation
      // is at the macro layer so the author sees an in-prose error rather
      // than silent injection.
      var buf = RenderRaw(source);
      AssertError(buf, "style value");
      foreach (var e in buf.Entries)
        Assert.NotEqual(BufferedRenderOutput.Kind.PushStyle, e.Kind);
    }

    [Theory]
    [InlineData("(text-color: \"expression(alert(1))\")[hi]")]
    [InlineData("(background: \"javascript:alert(1)\")[hi]")]
    [InlineData("(background: \"JaVaScRiPt:alert(1)\")[hi]")]   // case-insensitive
    [InlineData("(font: \"vbscript:msgbox\")[hi]")]
    [InlineData("(text-color: \"behavior:url(evil.htc)\")[hi]")]
    [InlineData("(text-color: \"Ｊavascript:alert(1)\")[hi]")] // full-width J — earlier ASCII-only prefilter bypassed the scan
    public void StyleValueWithDeniedKeyword_EmitsErrorAndDoesNotPushStyle(string source)
    {
      // Defense-in-depth: even though ';' is blocked separately, CSS feature
      // payloads (legacy IE expression(), javascript:/vbscript: URI schemes,
      // behavior:/binding: hooks) shouldn't survive the validator. Modern
      // browsers ignore them, but non-browser CSS consumers might still run
      // them — the denylist makes the boundary explicit.
      var buf = RenderRaw(source);
      AssertError(buf, "disallowed CSS keyword");
      foreach (var e in buf.Entries)
        Assert.NotEqual(BufferedRenderOutput.Kind.PushStyle, e.Kind);
    }

    [Theory]
    [InlineData("(text-color: \"red；background:url(x)\")[hi]")]   // full-width semicolon U+FF1B → ASCII ; under NFKC
    [InlineData("(text-color: \"red｛...｝\")[hi]")]              // full-width braces U+FF5B / U+FF5D → { / } under NFKC
    public void StyleValueWithFullWidthStructuralChar_IsBlocked(string source)
    {
      // The structural-char check used to scan the raw value and miss any
      // NFKC-equivalent variant; only the keyword pass normalized. Now both
      // checks share the normalized form, so a full-width `；` (U+FF1B, the
      // declaration separator's compatibility variant) trips the structural
      // gate the same way ASCII `;` does.
      var buf = RenderRaw(source);
      AssertError(buf, "style value");
      foreach (var e in buf.Entries)
        Assert.NotEqual(BufferedRenderOutput.Kind.PushStyle, e.Kind);
    }

    [Fact]
    public void StyleValueWithUnpairedSurrogate_RejectsInProse()
    {
      // The tokenizer's \uHHHH escape decodes to a single char and may produce
      // an unpaired surrogate. The validator calls String.Normalize, which
      // throws ArgumentException on invalid Unicode. The runtime contract
      // requires in-prose errors, never thrown exceptions on the render path,
      // AND the validator must fail closed — a raw-value fallback would skip
      // the NFKC-equivalence defense (see surrogate+fullwidth-separator test
      // below).
      var buf = RenderRaw("(text-color: \"\\uD800abc\")[hi]");
      AssertError(buf, "malformed Unicode");
      foreach (var e in buf.Entries)
        Assert.NotEqual(BufferedRenderOutput.Kind.PushStyle, e.Kind);
    }

    [Fact]
    public void StyleValueWithSurrogateAndFullWidthSeparator_IsBlocked()
    {
      // Bypass scenario the closed-fail fix defends against: pairing an
      // unpaired surrogate (which makes Normalize throw) with a full-width
      // semicolon U+FF1B (which NFKC would have folded to ASCII `;`). If the
      // validator falls back to the raw value on the normalize failure, the
      // structural-char loop only sees the ASCII set and the full-width
      // separator survives — letting an attacker smuggle a `;` past the
      // declaration boundary into the inline style attribute.
      var buf = RenderRaw("(text-color: \"red\\uD800；evil:1\")[hi]");
      AssertError(buf, "malformed Unicode");
      foreach (var e in buf.Entries)
        Assert.NotEqual(BufferedRenderOutput.Kind.PushStyle, e.Kind);
    }

    // --- Composition across the new macros ---

    [Fact]
    public void Compose_ColorAndOpacity_StacksTwoStyleLayers()
    {
      var buf = RenderRaw("(text-color: \"red\") + (opacity: 0.5) [hi]");
      int pushes = 0;
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.PushStyle) pushes++;
      Assert.Equal(2, pushes);
    }

    // --- v0.1.2 regression: (background:) url() input shape ---

    [Fact]
    public void Background_BareImagePath_StoresVerbatimSoHtmlRendersOneUrl()
    {
      // Author wrote a bare path. BackgroundImage holds the bare URL; the
      // HTML adapter wraps it in url(...) at emit time.
      var buf = RenderRaw("(background: \"art/sky.png\")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal("art/sky.png", style.BackgroundImage);
    }

    [Fact]
    public void Background_CssUrlShape_StripsTheWrapperSoHtmlDoesntDoubleWrap()
    {
      // Author wrote url(...). We strip the wrapper because BuildSpanTag adds
      // its own. Otherwise we'd emit background-image: url(url(art/sky.png)).
      var buf = RenderRaw("(background: \"url(art/sky.png)\")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal("art/sky.png", style.BackgroundImage);
    }

    [Fact]
    public void Background_CssUrlWithDoubleQuotes_StripsQuotesAndWrapper()
    {
      // url("...") is the canonical CSS shape; the quotes are also stripped
      // so the spec field holds the bare URL.
      var buf = RenderRaw("(background: 'url(\"art/sky.png\")')[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal("art/sky.png", style.BackgroundImage);
    }

    [Fact]
    public void Background_UppercaseUrlWrapper_AlsoStripped()
    {
      // The unwrap is case-insensitive — earlier the prefix match was strict
      // and `URL(foo.png)` survived unchanged, producing `url(URL(foo.png))`
      // in the emitted CSS.
      var buf = RenderRaw("(background: \"URL(art/sky.png)\")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal("art/sky.png", style.BackgroundImage);
    }

    [Fact]
    public void Background_EmptyUrlValue_EmitsInProseError()
    {
      // (background: "url()") used to silently produce a malformed
      // `background-image: url();` declaration. Now flagged at the macro.
      var buf = RenderRaw("(background: \"url()\")[hi]");
      AssertError(buf, "url() value is empty");
    }

    [Fact]
    public void Background_UrlWithTrailingJunk_EmitsInProseError()
    {
      // `url(art/sky.png)evil` — `LooksLikeImage` matches the `url(` prefix
      // but the value isn't a clean CSS url() shape (trailing content after
      // the close paren). An earlier UnwrapCssUrl required `EndsWith(")")`
      // and silently passed this through, producing double-wrapped
      // `background-image: url(url(art/sky.png)evil);` CSS. Reject at the
      // macro instead so the author sees a pointed diagnostic.
      var buf = RenderRaw("(background: \"url(art/sky.png)evil\")[hi]");
      AssertError(buf, "malformed url()");
    }

    [Fact]
    public void Background_BareUrlWithSurroundingWhitespace_StillRoutesAsImage()
    {
      // LooksLikeImage didn't trim before its prefix/suffix matchers, so a
      // value like `" url(art/sky.png) "` fell into the BackgroundColor
      // branch and emitted `background-color: <url>` (silently dropped by
      // browsers). Trim once up front so authors with stray whitespace get
      // the obvious behaviour.
      var buf = RenderRaw("(background: \"  url(art/sky.png)  \")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal("art/sky.png", style.BackgroundImage);
    }

    [Fact]
    public void Background_BarePathWithSurroundingWhitespace_StoresTrimmed()
    {
      var buf = RenderRaw("(background: \"  art/sky.png  \")[hi]");
      var style = FirstPushedStyle(buf);
      Assert.Equal("art/sky.png", style.BackgroundImage);
    }

  }
}
