using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Links to passages that don't exist. Reference gates every link on
  /// <c>Passages.hasValid</c> and renders an unclickable <c>&lt;tw-broken-link&gt;</c>
  /// (<c>ts/macrolib/links.ts</c>); we emit the label as prose plus an in-prose
  /// error and — the load-bearing part — <em>no</em> <c>Link</c> event at all, so
  /// nothing downstream can make a dead link clickable.
  ///
  /// <para>Why it matters beyond cosmetics: a followed dead link used to strand
  /// the session on a passage that doesn't exist, which went into the timeline,
  /// into the save blob, and made that save permanently unloadable — <c>LoadGame</c>
  /// validates every passage it restores. <see cref="DeadLink_Clicked_Saved_ThenLoaded_Survives"/>
  /// is that whole chain.</para>
  /// </summary>
  public class BrokenLinkTests
  {
    private static Harlowe Story(params (string name, string body)[] passages)
    {
      var s = new Harlowe();
      foreach (var (name, body) in passages)
        s.AddPassage(new HarlowePassage { Name = name, Body = body });
      s.StartNode = s.GetPassage(passages[0].name).Pid;
      return s;
    }

    private static int CountKind(RenderResult r, BufferedRenderOutput.Kind k)
    {
      int n = 0;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == k) n++;
      return n;
    }

    private static bool HasError(RenderResult r, string substring)
    {
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && r.Entries[i].Content != null
            && r.Entries[i].Content.Contains(substring))
          return true;
      return false;
    }

    // ----- the regression this whole fix exists for -----

    [Fact]
    public void DeadLink_Clicked_Saved_ThenLoaded_Survives()
    {
      // Pre-fix: the Link was clickable, Goto stranded the session on "Missing",
      // SaveGame wrote that name into the blob, and LoadGame then rejected its
      // own save forever with "saved passage 'Missing' no longer exists".
      var session = new StorySession(Story(("Start", "[[Go->Missing]]")));
      session.Render();

      session.Goto("Missing");                     // a host wiring a link click
      Assert.Equal("Start", session.CurrentPassage);

      Assert.True(session.SaveGame("slot"), session.LastSaveError);
      Assert.True(session.LoadGame("slot"), session.LastLoadError);
    }

    // ----- rendering -----

    [Fact]
    public void DeadLink_RendersLabelAsProse_WithError_AndNoLinkEvent()
    {
      var r = new StorySession(Story(("Start", "[[Go->Missing]]"))).Render();

      Assert.Equal(0, CountKind(r, BufferedRenderOutput.Kind.Link));
      Assert.Equal("Go", r.Text);
      Assert.True(HasError(r, "'Missing'"));
    }

    [Fact]
    public void LiveLink_StillEmitsAFlatLinkEvent()
    {
      var r = new StorySession(Story(("Start", "[[Go->Real]]"), ("Real", "x"))).Render();

      var link = Assert.Single(r.Entries, e => e.Kind == BufferedRenderOutput.Kind.Link);
      Assert.Equal("Go", link.Content);
      Assert.Equal("Real", link.Target);
      Assert.Equal(0, CountKind(r, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void StandaloneRenderer_WithNoStoryWired_StillEmitsLink()
    {
      // MacroContext.PassageExists is null outside a session (standalone renderer
      // tests, an engine driving BodyRenderer directly), and the check is skipped
      // there rather than calling every link broken — the same escape hatch
      // (goto:) uses.
      var ast = new Parsing.HarloweBodyParser().Parse(
        new Tokens.HarloweTokenizer().Tokenize("[[Go->Anywhere]]"));
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;

      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(ast);

      var link = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Link);
      Assert.Equal("Anywhere", link.Target);
    }

    // ----- Harlowe.GetBrokenLinks() -----

    [Fact]
    public void GetBrokenLinks_FindsThem_AndReportsWhereTheyAre()
    {
      var story = Story(
        ("Start", "[[Go->Missing]] and [[AlsoGone]]"),
        ("Real", "[[back->Start]]"));

      var broken = story.GetBrokenLinks();

      Assert.Equal(2, broken.Count);
      Assert.Equal("Start", broken[0].PassageName);
      Assert.Equal("Go", broken[0].LinkText);
      Assert.Equal("Missing", broken[0].Target);
      Assert.Equal("AlsoGone", broken[1].Target);
    }

    [Fact]
    public void GetBrokenLinks_CleanStory_IsEmpty()
    {
      var story = Story(("Start", "[[Go->Real]]"), ("Real", "[[back->Start]]"));
      Assert.Empty(story.GetBrokenLinks());
    }

    [Fact]
    public void GetBrokenLinks_CatchesLinksOrphanedByRemovePassage()
    {
      // The editing-API companion: RemovePassage silently orphans every link into
      // the passage it deletes, and this is how a tool finds them.
      var story = Story(("Start", "[[Go->Doomed]]"), ("Doomed", "x"));
      Assert.Empty(story.GetBrokenLinks());

      Assert.True(story.RemovePassage("Doomed"));

      var orphan = Assert.Single(story.GetBrokenLinks());
      Assert.Equal("Start", orphan.PassageName);
      Assert.Equal("Doomed", orphan.Target);
    }

    [Fact]
    public void GetBrokenLinks_ToleratesAPassageThatFailedToParse()
    {
      // A broken passage is an error stub with no AST to walk — it can't be
      // scanned, but it must not take the scan down with it.
      var story = Story(("Start", "[[Go->Missing]]"), ("Broken", "(for: each)"));
      var orphan = Assert.Single(story.GetBrokenLinks());
      Assert.Equal("Missing", orphan.Target);
    }

    // ----- macro targets: the half that makes the report trustworthy -----

    [Theory]
    [InlineData("(goto: \"Nowhere\")", "goto")]
    [InlineData("(go-to: \"Nowhere\")", "go-to")]          // normalizes to the same macro
    [InlineData("(display: \"Nowhere\")", "display")]
    [InlineData("(link-goto: \"Nowhere\")", "link-goto")]  // 1-arg: the text IS the target
    [InlineData("(link-goto: \"Flee\", \"Nowhere\")", "link-goto")]
    [InlineData("(click-goto: \"?d\", \"Nowhere\")", "click-goto")]
    [InlineData("(mouseover-goto: \"?d\", \"Nowhere\")", "mouseover-goto")]
    public void GetBrokenLinks_FindsLiteralMacroTargets(string body, string macro)
    {
      var b = Assert.Single(Story(("Start", body)).GetBrokenLinks());

      Assert.Equal("Nowhere", b.Target);
      Assert.Equal(macro, b.MacroName);
      Assert.False(b.IsLink);
      Assert.Null(b.LinkText);
    }

    [Fact]
    public void GetBrokenLinks_FindsAMacroBuriedInAHook()
    {
      // The case that makes a [[…]]-only report a lie: a dead (goto:) down a
      // branch the author hasn't played yet.
      var b = Assert.Single(Story(("Start", "(if: $brave)[(goto: \"Lair\")]")).GetBrokenLinks());
      Assert.Equal("Lair", b.Target);
      Assert.Equal("goto", b.MacroName);
    }

    [Fact]
    public void GetBrokenLinks_FindsAMacroNestedInAnExpression()
    {
      // (display:) in expression position is a documented idiom — its target is
      // just as absent as one in prose.
      var b = Assert.Single(Story(("Start", "(set: $x to (display: \"Stats\"))")).GetBrokenLinks());
      Assert.Equal("Stats", b.Target);
      Assert.Equal("display", b.MacroName);
    }

    [Fact]
    public void GetBrokenLinks_SkipsAComputedTarget()
    {
      // Undecidable statically — skipped rather than guessed at. It still errors
      // in prose at render time.
      Assert.Empty(Story(("Start", "(set: $n to \"X\")(goto: $n)")).GetBrokenLinks());
    }

    [Fact]
    public void GetBrokenLinks_IgnoresAMacroWhoseTargetExists()
    {
      Assert.Empty(Story(("Start", "(display: \"Real\")"), ("Real", "x")).GetBrokenLinks());
    }

    // ----- shaped for a host engine to display -----

    [Fact]
    public void BrokenLink_CarriesAPositionAndAPrintableMessage()
    {
      var story = Story(("Start", "line one\n[[Go->Missing]]\n"));
      var b = Assert.Single(story.GetBrokenLinks());

      Assert.Equal(2, b.Line);
      Assert.Equal(1, b.Column);
      Assert.True(b.IsLink);
      Assert.Equal(
        "In passage 'Start' (line 2, column 1): the link [[Go]] points to the passage 'Missing', which doesn't exist.",
        b.Message);
      Assert.Equal(b.Message, b.ToString());
    }

    [Fact]
    public void BrokenLink_Message_OmitsThePositionWhenUnknown()
    {
      // A hand-built AST carries no positions; the message must degrade cleanly
      // rather than printing "(line 0, column 0)".
      var b = new BrokenLink { PassageName = "P", Target = "T", MacroName = "goto" };
      Assert.Equal("In passage 'P': (goto:) points to the passage 'T', which doesn't exist.", b.Message);
    }
  }
}
