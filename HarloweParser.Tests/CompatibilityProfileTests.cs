using System;
using System.Collections.Generic;
using System.Reflection;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// The compatibility-profile machinery: <see cref="HarloweProfile.Resolve"/>'s
  /// mapping from a story's declared <c>format-version</c> to a profile, and the
  /// drift guard that keeps every declared switch wired to real behaviour.
  ///
  /// <para>This change class cannot produce a red test on its own — a switch
  /// that reads nothing, or a guard placed at two of three sites, is silent
  /// behaviour drift, not a failure. The drift guard below is the machinery
  /// that replaces relying on discipline.</para>
  /// </summary>
  public class CompatibilityProfileTests
  {
    // ----- Resolve -----

    [Theory]
    [InlineData("3.3.9")]
    [InlineData("3.2.3")]
    [InlineData("3")]
    [InlineData("3.0.0-beta")]  // trailing text never changes which major applies
    public void Resolve_ThreeDotX_SelectsV3(string version)
      => Assert.Same(HarloweProfile.V3, HarloweProfile.Resolve(version));

    [Theory]
    [InlineData("4.0.0")]
    [InlineData("4")]
    [InlineData("4.0.0-unstable")]
    public void Resolve_FourDotX_SelectsV4(string version)
      => Assert.Same(HarloweProfile.V4, HarloweProfile.Resolve(version));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_Absent_SelectsLatest(string version)
      => Assert.Same(HarloweProfile.Latest, HarloweProfile.Resolve(version));

    [Theory]
    [InlineData("2.1.0")]
    [InlineData("1.2.4")]
    [InlineData("0.9")]
    public void Resolve_BelowThree_ClampsToV3(string version)
    {
      // The lock-in promise starts at 3.x, so a 2.x story has no profile of
      // its own. It clamps down rather than defaulting up: a 2.x story's prose
      // is exactly as likely to contain `--` em-dashes as a 3.x story's, and
      // V3 renders those whole where Latest would eat them.
      Assert.Same(HarloweProfile.V3, HarloweProfile.Resolve(version));
    }

    [Theory]
    [InlineData("5.2.0")]
    [InlineData("99")]
    [InlineData("12345678901234567890")]  // wider than an int; still a future major
    public void Resolve_FutureMajor_SelectsLatest(string version)
      => Assert.Same(HarloweProfile.Latest, HarloweProfile.Resolve(version));

    [Theory]
    [InlineData("garbage")]
    [InlineData("v3.3.9")]  // leading 'v' — no leading digit run, so unparseable
    [InlineData("   ")]
    [InlineData(".3.9")]
    public void Resolve_Unparseable_SelectsLatest(string version)
      => Assert.Same(HarloweProfile.Latest, HarloweProfile.Resolve(version));

    [Fact]
    public void SaveFormat_IsPinned_NotAnAliasOfLatest()
    {
      // Save blobs re-lex under SaveFormat, never the story's profile: their
      // value sources are engine-emitted, so author compatibility policy has
      // no bearing on them. This asserts the constant's current value — when
      // a Harlowe 5 lands and Latest moves, this test staying green is the
      // point. Changing SaveFormat is a save-format break needing a
      // SaveBlobVersion.Current bump.
      Assert.Same(HarloweProfile.V4, HarloweProfile.SaveFormat);
    }

    // ----- Drift guard -----
    //
    // Reflection for enumeration, a behavioural probe for verification — the
    // same split as the navigation-macro drift guard in BrokenLinkTests. A
    // switch is a claim that two majors behave differently; these facts make
    // that claim testable, so a switch wired to nothing cannot reach green.
    //
    // Adding a switch to HarloweProfile therefore requires adding a probe
    // below. That is the intended workflow, not an obstacle: the probe is the
    // proof the switch does something.
    //
    // A render-level probe is necessary but NOT sufficient for a switch with
    // several guard sites, and this was measured rather than assumed. Undoing
    // any one of CommentMarkup's three tokenizer guards was tried in turn:
    // guard 1 (body dispatch) failed 9 facts, guard 2 (expression dispatch)
    // failed 6 — but guard 3 (the ScanText prose-run break) failed only the
    // two token-level facts in CommentTests, because a fragmented prose run
    // renders to a byte-identical string. A switch whose guards can disagree
    // about token *shape* without changing rendered text needs an assertion
    // at the layer it acts on, not just this one.

    /// <summary>
    /// Every switch on <see cref="HarloweProfile"/>, read at runtime.
    /// <c>BindingFlags.Instance</c> excludes the <c>V3</c>/<c>V4</c>/
    /// <c>Latest</c>/<c>SaveFormat</c> statics, so this is exactly the switch
    /// set with no hand-maintained list to fall out of date.
    /// </summary>
    private static List<string> SwitchNames()
    {
      var names = new List<string>();
      foreach (var p in typeof(HarloweProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        if (p.PropertyType == typeof(bool)) names.Add(p.Name);
      names.Sort(StringComparer.Ordinal);
      return names;
    }

    /// <summary>
    /// Source that renders differently under each profile, one entry per
    /// switch. Keyed by the property name so a rename breaks the pairing
    /// loudly instead of leaving a probe silently testing nothing.
    /// </summary>
    private static readonly Dictionary<string, Func<HarloweProfile, string>> Probes =
      new Dictionary<string, Func<HarloweProfile, string>>(StringComparer.Ordinal)
      {
        { "CommentMarkup", p => RenderUnder(p, "it was -- and remains -- fine") },
      };

    /// <summary>
    /// Renders <paramref name="src"/> end to end under one profile, setting it
    /// on both the tokenizer and the <see cref="MacroContext"/> so a single
    /// signature covers parse-time and runtime switches alike.
    /// </summary>
    private static string RenderUnder(HarloweProfile profile, string src)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext
      {
        Store = new HarloweVariableStore(),
        Invoker = registry,
        Profile = profile,
      };
      registry.Context = ctx;
      var body = new HarloweBodyParser().Parse(new HarloweTokenizer(profile).Tokenize(src), src);
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(body);
      return buf.Text;
    }

    [Fact]
    public void EverySwitch_HasAProbe_AndEveryProbeAMatchingSwitch()
    {
      // Checked in both directions: a switch added with no probe would
      // otherwise ship unverified, and a probe left behind after a rename
      // would otherwise keep passing while testing a switch that no longer
      // exists.
      var switches = SwitchNames();
      foreach (var name in switches)
        Assert.True(Probes.ContainsKey(name),
          $"HarloweProfile.{name} is a switch with no probe — add one to CompatibilityProfileTests.Probes "
          + "so the switch is proven to change behaviour.");

      foreach (var probed in Probes.Keys)
        Assert.True(switches.Contains(probed),
          $"CompatibilityProfileTests.Probes has an entry for '{probed}', which is not a switch on "
          + "HarloweProfile — it was renamed or removed; update or delete the probe.");
    }

    public static IEnumerable<object[]> AllSwitches()
    {
      foreach (var name in SwitchNames()) yield return new object[] { name };
    }

    [Theory]
    [MemberData(nameof(AllSwitches))]
    public void EverySwitch_ActuallyChangesBehaviour(string switchName)
    {
      // This is what catches a switch declared but wired to nothing, or
      // guarded at some of its sites but not all. A probe that does nothing
      // can no longer reach green.
      var probe = Probes[switchName];
      Assert.NotEqual(probe(HarloweProfile.V3), probe(HarloweProfile.V4));
    }

    [Fact]
    public void CommentMarkup_ConcreteValues_BothProfiles()
    {
      // "differs" is necessary but not sufficient — it would still hold if
      // both sides were wrong. Pin what each major actually produces.
      const string src = "it was -- and remains -- fine";
      Assert.Equal("it was -- and remains -- fine", RenderUnder(HarloweProfile.V3, src));
      Assert.Equal("it was ", RenderUnder(HarloweProfile.V4, src));
    }

    // ----- The switch reaching real stories -----

    [Fact]
    public void TweeStory_DeclaringThreeDotX_RendersEmDashesWhole()
    {
      // End to end through the loader: the bug this slice exists to fix. Real
      // Twine exports always declare a format-version, so this is the shape
      // every existing story arrives in.
      var story = new global::Harlowe.Twee.TweeReader().Read(
        ":: StoryData\n{\"format-version\":\"3.3.9\",\"start\":\"Start\"}\n\n"
        + ":: Start\nit was -- and remains -- fine");
      Assert.Same(HarloweProfile.V3, story.Profile);
      Assert.Equal("it was -- and remains -- fine", new StorySession(story).Render().Text);
    }

    [Fact]
    public void TweeStory_DeclaringFourDotX_StillTruncatesAtTheComment()
    {
      var story = new global::Harlowe.Twee.TweeReader().Read(
        ":: StoryData\n{\"format-version\":\"4.0.0\",\"start\":\"Start\"}\n\n"
        + ":: Start\nit was -- and remains -- fine");
      Assert.Same(HarloweProfile.V4, story.Profile);
      Assert.Equal("it was ", new StorySession(story).Render().Text);
    }

    [Fact]
    public void TweeStory_StoryDataAfterPassages_StillSelectsTheProfile()
    {
      // The metadata pre-pass: read in source order, this block would arrive
      // after the passage it governs had already been lexed.
      var story = new global::Harlowe.Twee.TweeReader().Read(
        ":: Start\nit was -- and remains -- fine\n\n"
        + ":: StoryData\n{\"format-version\":\"3.3.9\",\"start\":\"Start\"}");
      Assert.Same(HarloweProfile.V3, story.Profile);
      Assert.Equal("it was -- and remains -- fine", new StorySession(story).Render().Text);
    }

    [Fact]
    public void TweeReader_ProfileOverride_BeatsTheDeclaration()
    {
      var story = new global::Harlowe.Twee.TweeReader(HarloweProfile.V3).Read(
        ":: StoryData\n{\"format-version\":\"4.0.0\",\"start\":\"Start\"}\n\n"
        + ":: Start\nit was -- and remains -- fine");
      Assert.Same(HarloweProfile.V3, story.Profile);
      Assert.Equal("it was -- and remains -- fine", new StorySession(story).Render().Text);
    }

    [Fact]
    public void HtmlStory_ProfileOverride_BeatsTheDeclaration()
    {
      string html = "<tw-storydata name=\"S\" startnode=\"1\" format=\"Harlowe\" format-version=\"4.0.0\">"
        + "<tw-passagedata pid=\"1\" name=\"Start\">it was -- and remains -- fine</tw-passagedata>"
        + "</tw-storydata>";

      Assert.Equal("it was ", new StorySession(new Harlowe(html)).Render().Text);

      var overridden = new Harlowe(html, HarloweProfile.V3);
      Assert.Same(HarloweProfile.V3, overridden.Profile);
      Assert.Equal("it was -- and remains -- fine", new StorySession(overridden).Render().Text);
    }

    [Fact]
    public void HtmlStory_DeclaringThreeDotX_FollowsItsDeclaration()
    {
      string html = "<tw-storydata name=\"S\" startnode=\"1\" format=\"Harlowe\" format-version=\"3.3.9\">"
        + "<tw-passagedata pid=\"1\" name=\"Start\">it was -- and remains -- fine</tw-passagedata>"
        + "</tw-storydata>";
      var story = new Harlowe(html);
      Assert.Same(HarloweProfile.V3, story.Profile);
      Assert.Equal("it was -- and remains -- fine", new StorySession(story).Render().Text);
    }

    [Fact]
    public void AddPassage_FollowsTheStorysDeclaredProfile()
    {
      // A hand-built story is the documented editing shape: set metadata, then
      // add passages. The passage must be lexed under the declared major.
      var story = new Harlowe { FormatVersion = "3.3.9" };
      story.AddPassage(new HarlowePassage { Name = "Start", Body = "it was -- and remains -- fine" });
      story.StartNode = story.GetPassage("Start").Pid;
      Assert.Equal("it was -- and remains -- fine", new StorySession(story).Render().Text);
    }

    [Fact]
    public void SaveBlob_RelexesUnderSaveFormat_NotTheStorysProfile()
    {
      // A save written by a story is re-read under SaveFormat regardless of
      // the major that story declares — the pin that stops an author's
      // format-version bump from re-interpreting existing saves.
      // A value whose own source contains `--`, saved from a story declaring
      // 3.3.9. The blob is engine-emitted source, so it must re-lex the same
      // way whatever the story declares.
      var story = new global::Harlowe.Twee.TweeReader().Read(
        ":: StoryData\n{\"format-version\":\"3.3.9\",\"start\":\"Start\"}\n\n"
        + ":: Start\n(set: $s to \"a--b\")(set: $n to 42)start\n\n"
        + ":: Mutate\n(set: $s to \"gone\")(set: $n to 99)mutated\n\n"
        + ":: Show\n$s $n");
      var session = new StorySession(story);
      session.Render();
      Assert.True(session.SaveGame("slot"), session.LastSaveError);

      Assert.Equal("mutated", session.Goto("Mutate").Text);
      Assert.Equal("gone 99", session.Goto("Show").Text);     // values clobbered

      Assert.True(session.LoadGame("slot"), session.LastLoadError);
      Assert.Equal("Start", session.CurrentPassage);
      // A load restores the store to the saved turn's *start*; Render()
      // reproduces the turn, re-applying its assignments from the blob.
      Assert.Equal("start", session.Render().Text);
      Assert.Equal("a--b 42", session.Goto("Show").Text);     // saved values back, `--` intact
    }

    // ----- GetCompatibilityNotices -----

    [Theory]
    [InlineData("3.3.9")]
    [InlineData("4.0.0")]
    public void Notices_RecognisedMajor_ReportsNothing(string version)
    {
      var story = new Harlowe { FormatVersion = version };
      Assert.Empty(story.GetCompatibilityNotices());
    }

    [Fact]
    public void Notices_NoDeclaredVersion_IsInfo()
    {
      // The common case for hand-built and test stories. Real Twine exports
      // always declare a version, so this is information, not a problem.
      var notices = new Harlowe().GetCompatibilityNotices();
      var n = Assert.Single(notices);
      Assert.Equal(NoticeSeverity.Info, n.Severity);
      Assert.Same(HarloweProfile.Latest, n.Profile);
      Assert.Equal("", n.DeclaredVersion);
      Assert.Contains("no format-version", n.Message);
    }

    [Fact]
    public void Notices_FutureMajor_WarnsAboutTheLibrary()
    {
      var story = new Harlowe { FormatVersion = "5.2.0" };
      var n = Assert.Single(story.GetCompatibilityNotices());
      Assert.Equal(NoticeSeverity.Warning, n.Severity);
      Assert.Same(HarloweProfile.Latest, n.Profile);
      Assert.Equal("5.2.0", n.DeclaredVersion);
      Assert.Contains("newer major than this library implements", n.Message);
    }

    [Fact]
    public void Notices_Unparseable_WarnsAboutTheMetadata()
    {
      // Distinguished from the future-major case on purpose: one means "this
      // library is behind", the other "this metadata is malformed", and they
      // call for different fixes even though both run under the newest.
      var story = new Harlowe { FormatVersion = "not-a-version" };
      var n = Assert.Single(story.GetCompatibilityNotices());
      Assert.Equal(NoticeSeverity.Warning, n.Severity);
      Assert.Contains("could not be read as a version number", n.Message);
      Assert.DoesNotContain("newer major", n.Message);
    }

    [Fact]
    public void Notices_BelowThree_WarnsAndReportsTheClamp()
    {
      var story = new Harlowe { FormatVersion = "2.1.0" };
      var n = Assert.Single(story.GetCompatibilityNotices());
      Assert.Equal(NoticeSeverity.Warning, n.Severity);
      Assert.Same(HarloweProfile.V3, n.Profile);           // clamped, not defaulted up
      Assert.Contains("predates Harlowe 3", n.Message);
      Assert.Contains("Harlowe 3", n.Message);             // and says what it ran as
    }

    [Fact]
    public void Notices_HostOverride_IsReportedEvenWhenTheVersionIsFine()
    {
      // The host chose this, so it isn't a warning — but nothing else records
      // that the story's own declaration was set aside.
      var story = new Harlowe { FormatVersion = "4.0.0", Profile = HarloweProfile.V3 };
      var n = Assert.Single(story.GetCompatibilityNotices());
      Assert.Equal(NoticeSeverity.Info, n.Severity);
      Assert.Same(HarloweProfile.V3, n.Profile);
      Assert.Contains("overriding", n.Message);
      Assert.Contains("4.0.0", n.Message);
    }

    [Fact]
    public void Notices_NeverThrow_AcrossOddVersions()
    {
      // Diagnostics are called at load on whatever the file contained; a
      // malformed version must not take the story down.
      foreach (var v in new[] { null, "", "   ", "3", "0", "-1", "99999999999999999999", "3.3.9-x" })
        Assert.NotNull(new Harlowe { FormatVersion = v }.GetCompatibilityNotices());
    }
  }
}
