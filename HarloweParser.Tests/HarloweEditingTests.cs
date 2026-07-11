using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Runtime;
using Harlowe.Twee;
using Xunit;

namespace Harlowe.Tests
{
  /// <summary>
  /// Coverage for the editing API surface (public ctor, AddPassage,
  /// RemovePassage, RenamePassage, public metadata setters). Until the v1.3
  /// polish slice these were all internal — the tests in this file are
  /// verifying that an external consumer (in another assembly) can build
  /// and mutate stories.
  /// </summary>
  public class HarloweEditingTests
  {
    private static HarlowePassage MakePassage(string name, string body = "")
    {
      var ast = new PassageBody { Children = new List<IBodyNode>() };
      if (!string.IsNullOrEmpty(body))
        ast.Children.Add(new TextNode { Content = body });
      return new HarlowePassage
      {
        Name = name,
        Ast = ast,
        RawBody = body,
        Tags = new List<string>(),
        Branches = new List<Branch>(),
        IsDirty = true,
      };
    }

    // Real-pipeline construction (tokenized + parsed links, clean passages),
    // for the RenamePassage link-rewriting tests that need genuine LinkNodes
    // rather than MakePassage's single fake TextNode.
    private static Harlowe Read(string twee) => new TweeReader().Read(twee);

    [Fact]
    public void PublicConstructor_BuildsEmptyStory()
    {
      var story = new Harlowe();
      Assert.Equal(0, story.PassageCount);
      Assert.Equal(string.Empty, story.StoryName);
      Assert.Equal("0", story.StartNode);
    }

    [Fact]
    public void AddPassage_PublicAndIndexedByName()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First", "hello"));
      Assert.Equal(1, story.PassageCount);
      Assert.NotNull(story.GetPassage("First"));
    }

    [Fact]
    public void AddPassage_AutoSynthesizesPid_WhenNullOrEmpty()
    {
      var story = new Harlowe();
      var p = MakePassage("First");
      p.Pid = null;
      story.AddPassage(p);
      Assert.Equal("1", p.Pid);

      var q = MakePassage("Second");
      q.Pid = "";
      story.AddPassage(q);
      Assert.Equal("2", q.Pid);
    }

    [Fact]
    public void AddPassage_PreservesExplicitPid()
    {
      var story = new Harlowe();
      var p = MakePassage("First");
      p.Pid = "42";
      story.AddPassage(p);
      Assert.Equal("42", p.Pid);
    }

    [Fact]
    public void AddPassage_DuplicateName_Throws()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.Throws<System.ArgumentException>(() => story.AddPassage(MakePassage("First")));
    }

    [Fact]
    public void AddPassage_Null_Throws()
    {
      var story = new Harlowe();
      Assert.Throws<System.ArgumentNullException>(() => story.AddPassage(null));
    }

    [Fact]
    public void AddPassage_HydratesAstFromBody_WhenAstIsNull()
    {
      // The documented from-scratch shorthand only sets Name + Body. AddPassage
      // should parse the body so the resulting passage is structurally complete
      // — otherwise rendering and TweeWriter both see a hollow passage.
      var story = new Harlowe();
      var p = new HarlowePassage { Name = "Foo", Body = "hello" };
      story.AddPassage(p);

      var stored = story.GetPassage("Foo");
      Assert.NotNull(stored.Ast);
      Assert.NotNull(stored.Ast.Children);
      Assert.Single(stored.Ast.Children);
      Assert.IsType<TextNode>(stored.Ast.Children[0]);
      Assert.Equal("hello", ((TextNode)stored.Ast.Children[0]).Content);
      Assert.Equal("hello", stored.RawBody);
      Assert.NotNull(stored.Branches);
    }

    [Fact]
    public void AddPassage_DoesNotMutateCallerBodyFromMacroSource()
    {
      // The caller's body source is authoritative. Hydration must not rewrite
      // it to a macro-stripped prose view — that would silently drop macros on
      // round-trip through (passage.Body → AddPassage).
      var story = new Harlowe();
      var p = new HarlowePassage { Name = "X", Body = "(set: $x to 1)Hello" };
      story.AddPassage(p);
      Assert.Equal("(set: $x to 1)Hello", p.Body);
      Assert.Equal("(set: $x to 1)Hello", story.GetPassage("X").Body);
      // The AST must still be hydrated.
      Assert.NotNull(p.Ast);
    }

    [Fact]
    public void Body_AndRawBody_ShareOneBackingValue()
    {
      // Body and RawBody are one underlying string. Editing either must be
      // visible through the other and reach Twee write-out — previously they
      // were independent fields, so a Body edit left RawBody (what the writer
      // emits) stale, and vice versa.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage { Name = "P", Body = "original" });
      var p = story.GetPassage("P");

      p.Body = "edited via Body";
      Assert.Equal("edited via Body", p.RawBody);

      p.RawBody = "edited via RawBody";
      Assert.Equal("edited via RawBody", p.Body);

      // The edit reaches the writer (clean passage emits its source verbatim).
      Assert.Contains("edited via RawBody", new TweeWriter().Write(story));
    }

    [Fact]
    public void AddPassage_HydrationDoesNotOverwritePrebuiltAst()
    {
      // Test fixtures (and the HTML/Twee loaders) construct the AST by hand and
      // pass a fully-formed passage. Hydration must not stomp on their work.
      var story = new Harlowe();
      var prebuilt = MakePassage("Foo", "hand-built");
      var originalAst = prebuilt.Ast;
      var originalBranches = prebuilt.Branches;
      story.AddPassage(prebuilt);

      Assert.Same(originalAst, story.GetPassage("Foo").Ast);
      Assert.Same(originalBranches, story.GetPassage("Foo").Branches);
    }

    [Fact]
    public void AddPassage_NullBody_LeavesAstNull()
    {
      // A passage with neither Body nor Ast is degenerate but legal — render
      // and TweeWriter are both null-safe. Hydration must not invent content.
      var story = new Harlowe();
      var p = new HarlowePassage { Name = "Empty" };
      story.AddPassage(p);
      Assert.Null(story.GetPassage("Empty").Ast);
    }

    [Fact]
    public void AddPassage_EmptyBody_ParsesToEmptyAst()
    {
      // An empty body is still a body. The AST should be a real (empty) tree
      // rather than null so downstream code can treat the passage uniformly.
      var story = new Harlowe();
      var p = new HarlowePassage { Name = "Empty", Body = "" };
      story.AddPassage(p);

      var stored = story.GetPassage("Empty");
      Assert.NotNull(stored.Ast);
      Assert.NotNull(stored.Ast.Children);
      Assert.Empty(stored.Ast.Children);
    }

    [Fact]
    public void AddPassage_HydratedPassage_RendersThroughSession()
    {
      // End-to-end: a hand-constructed story should render to the expected
      // text. This is the core regression — before hydration, this returned
      // empty content.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage { Pid = "1", Name = "Start", Body = "(set: $x to 5)$x" });
      story.StartNode = "1";

      var session = new StorySession(story);
      var r = session.Render();
      Assert.Equal("5", r.Text);
    }

    [Fact]
    public void AddPassage_HydratedPassage_CollectsBranches()
    {
      // [[Next]] in the body should land in Branches via the same collector the
      // HTML and Twee loaders use.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage { Name = "Start", Body = "go [[Next]]" });

      var stored = story.GetPassage("Start");
      Assert.NotNull(stored.Branches);
      Assert.Single(stored.Branches);
      Assert.Equal("Next", stored.Branches[0].Name);
    }

    [Fact]
    public void AddPassage_ParseError_RecoversWithSyntheticAst()
    {
      // AddPassage matches the HTML/Twee bulk loaders' contract: a broken body
      // doesn't throw out of the editing API — the passage is hydrated with a
      // ParseErrorNode AST so the rest of the story remains usable and the
      // broken passage renders an in-prose error at render time. Editors that
      // batch per-passage saves don't crash on a single bad expression.
      var story = new Harlowe();
      var p = new HarlowePassage { Name = "Broken", Body = "(for: each)" };
      story.AddPassage(p);

      Assert.Same(p, story.GetPassage("Broken"));
      Assert.NotNull(p.Ast);
      Assert.Single(p.Ast.Children);
      var err = Assert.IsType<ParseErrorNode>(p.Ast.Children[0]);
      Assert.Contains("Broken", err.Message);
      // RawBody captures the original source so TweeWriter can round-trip it.
      Assert.Equal("(for: each)", p.RawBody);
    }

    [Fact]
    public void RemovePassage_ExistingPassage_ReturnsTrue()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.True(story.RemovePassage("First"));
      Assert.Equal(0, story.PassageCount);
      Assert.Null(story.GetPassage("First"));
    }

    [Fact]
    public void RemovePassage_NonExistent_ReturnsFalse()
    {
      var story = new Harlowe();
      Assert.False(story.RemovePassage("Nope"));
    }

    [Fact]
    public void RemovePassage_Null_ReturnsFalse()
    {
      var story = new Harlowe();
      Assert.False(story.RemovePassage(null));
    }

    [Fact]
    public void RemovePassage_StartPassage_LeavesGetStartPassageNull()
    {
      // Removing the start passage breaks GetStartPassage cleanly — it's the
      // same dangling-pid behaviour HTML stories already exhibit when the
      // export's startnode attribute names a missing passage.
      var story = new Harlowe();
      var start = MakePassage("First");
      start.Pid = "1";
      story.AddPassage(start);
      story.StartNode = "1";

      Assert.NotNull(story.GetStartPassage());
      Assert.True(story.RemovePassage("First"));
      Assert.Null(story.GetStartPassage());
    }

    [Fact]
    public void RenamePassage_RekeysLookup()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("Old"));
      Assert.True(story.RenamePassage("Old", "New"));
      Assert.Null(story.GetPassage("Old"));
      Assert.NotNull(story.GetPassage("New"));
      Assert.Equal("New", story.GetPassage("New").Name);
    }

    [Fact]
    public void RenamePassage_PreservesPassageInstance()
    {
      // Identity check: the renamed entry should be the same object, not a
      // copy. Catches an implementation that re-creates the passage instead
      // of re-keying.
      var story = new Harlowe();
      var p = MakePassage("Old");
      story.AddPassage(p);
      story.RenamePassage("Old", "New");
      Assert.Same(p, story.GetPassage("New"));
    }

    [Fact]
    public void RenamePassage_NonExistent_ReturnsFalse()
    {
      var story = new Harlowe();
      Assert.False(story.RenamePassage("Nope", "Anything"));
    }

    [Fact]
    public void RenamePassage_Collision_LeavesStoryUnchanged()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      story.AddPassage(MakePassage("Second"));
      Assert.False(story.RenamePassage("First", "Second"));
      // Both should still resolve to their original entries.
      Assert.Equal("First", story.GetPassage("First").Name);
      Assert.Equal("Second", story.GetPassage("Second").Name);
    }

    [Fact]
    public void RenamePassage_SameName_ReturnsTrueOrFalseBasedOnExistence()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.True(story.RenamePassage("First", "First"));
      Assert.False(story.RenamePassage("Nope", "Nope"));
    }

    [Fact]
    public void RenamePassage_RewritesInboundLinks_AllThreeForms()
    {
      // Bare, right-arrow, and left-arrow links all retarget; the collapsed
      // [[Old]] form updates its visible text too (text == target there).
      var story = Read(":: Start\n[[Old]] [[go->Old]] [[Old<-back]]\n\n:: Old\nhi");
      Assert.True(story.RenamePassage("Old", "New"));
      Assert.Equal("[[New]] [[go->New]] [[New<-back]]", story.GetPassageBody("Start"));
    }

    [Fact]
    public void RenamePassage_RewritesSelfLink()
    {
      // The renamed passage is included, so a link to itself updates.
      var story = Read(":: Old\nloop [[Old]]");
      story.RenamePassage("Old", "New");
      Assert.Equal("loop [[New]]", story.GetPassageBody("New"));
    }

    [Fact]
    public void RenamePassage_RegeneratesAstAndBranches()
    {
      // The reparse drives navigation: BodyRenderer emits LinkNode.Target, and
      // Branches is re-derived from it. Both must reflect the new name.
      var story = Read(":: Start\n[[go->Old]]\n\n:: Old\nhi");
      story.RenamePassage("Old", "New");
      var branches = story.GetPassageBranches("Start");
      Assert.Equal("New", Assert.Single(branches).Name);
      Assert.Equal("go", branches[0].Text);
    }

    [Fact]
    public void RenamePassage_PreservesSurroundingFormatting_PassageStaysClean()
    {
      // Only the link target changes; the rest of the body is byte-identical and
      // the passage is not marked dirty (no MarkupPrinter re-canonicalization).
      var story = Read(":: Start\nbefore [[go->Old]] after\n\n:: Old\nhi");
      story.RenamePassage("Old", "New");
      var start = story.GetPassage("Start");
      Assert.Equal("before [[go->New]] after", start.RawBody);
      Assert.False(start.IsDirty);
    }

    [Fact]
    public void RenamePassage_NonLinkingPassage_LeftUntouchedAndClean()
    {
      var story = Read(":: Start\nplain prose, no links\n\n:: Old\nhi");
      story.RenamePassage("Old", "New");
      var start = story.GetPassage("Start");
      Assert.Equal("plain prose, no links", start.RawBody);
      Assert.False(start.IsDirty);
    }

    [Fact]
    public void RenamePassage_UpdateInboundLinksFalse_LeavesLinksAlone()
    {
      var story = Read(":: Start\n[[Old]]\n\n:: Old\nhi");
      story.RenamePassage("Old", "New", updateInboundLinks: false);
      Assert.Equal("[[Old]]", story.GetPassageBody("Start"));
    }

    [Fact]
    public void RenamePassage_DoesNotRewriteMacroStringTargets()
    {
      // Documents the limitation (matches Twine): (goto:) and friends take a
      // string a rewriter can't tell apart from any other string, so they're
      // the caller's responsibility.
      var story = Read(":: Start\n(goto: \"Old\")\n\n:: Old\nhi");
      story.RenamePassage("Old", "New");
      Assert.Contains("(goto: \"Old\")", story.GetPassageBody("Start"));
    }

    [Fact]
    public void GetPassage_DirectNameMutation_StillReturnsByDictionaryKey()
    {
      // HarlowePassage.Name is a public field for legacy reasons; mutating it
      // after AddPassage leaves the story's lookup keyed by the old name.
      // GetPassage must not throw — it lives on the runtime hot path
      // ((display:), goto) where the project's contract is in-prose errors,
      // not exceptions. Lookup returns by dictionary key; the consumer's
      // mismatched passage.Name is theirs to clean up via RenamePassage.
      var story = new Harlowe();
      var p = MakePassage("First");
      story.AddPassage(p);
      p.Name = "Mutated";

      // Lookup by the original key still resolves to the entry.
      Assert.Same(p, story.GetPassage("First"));
      // Lookup by the consumer-set name doesn't (the dict was never re-keyed).
      Assert.Null(story.GetPassage("Mutated"));
    }

    [Fact]
    public void GetPassage_NullName_ReturnsNull()
    {
      // GetPassage should be null-safe on input — a quirk of the prior
      // ContainsKey form (which throws on null) hid this; the integrity-check
      // rewrite restores the documented "null on miss" contract.
      var story = new Harlowe();
      Assert.Null(story.GetPassage(null));
    }

    [Fact]
    public void GetPassageBody_NullName_ReturnsEmptyString()
    {
      // GetPassageBody used to delegate to Dictionary.TryGetValue, which
      // throws ArgumentNullException on null keys. Now matches the rest of
      // the lookup API's null-safe contract.
      var story = new Harlowe();
      Assert.Equal(string.Empty, story.GetPassageBody(null));
    }

    [Fact]
    public void GetPassageBranches_NullName_ReturnsNull()
    {
      var story = new Harlowe();
      Assert.Null(story.GetPassageBranches(null));
    }

    [Fact]
    public void GetPassageBody_HandBuiltPassageWithNullBody_ReturnsEmptyString()
    {
      // A pre-built-AST passage may carry Body=null (HydratePassageFromBody
      // short-circuits when Ast is set and never coerces Body). GetPassageBody
      // must still honour its documented never-null return — otherwise
      // downstream .Length / .Contains calls crash with NullReferenceException.
      var story = new Harlowe();
      var ast = new Ast.Body.PassageBody { Children = new System.Collections.Generic.List<Ast.Body.IBodyNode>() };
      story.AddPassage(new HarlowePassage { Name = "X", Body = null, Ast = ast });
      Assert.Equal(string.Empty, story.GetPassageBody("X"));
    }

    [Fact]
    public void GetPassageByPid_FindsByPid()
    {
      var story = new Harlowe();
      var p = MakePassage("First");
      p.Pid = "42";
      story.AddPassage(p);
      Assert.Same(p, story.GetPassageByPid("42"));
    }

    [Fact]
    public void GetPassageByPid_NullOrUnknown_ReturnsNull()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      Assert.Null(story.GetPassageByPid(null));
      Assert.Null(story.GetPassageByPid(""));
      Assert.Null(story.GetPassageByPid("999"));
    }

    [Fact]
    public void Passages_EnumeratesInLoadOrder()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("First"));
      story.AddPassage(MakePassage("Second"));
      story.AddPassage(MakePassage("Third"));
      var names = new List<string>();
      foreach (var p in story.Passages) names.Add(p.Name);
      Assert.Equal(new[] { "First", "Second", "Third" }, names);
    }

    [Fact]
    public void StoryMetadata_PublicallyWritable_ReflectsInWriterOutput()
    {
      var story = new Harlowe();
      story.StoryName = "MyStory";
      story.Ifid = "ABC-123";
      story.Format = "Harlowe";
      story.FormatVersion = "3.3.9";
      var first = MakePassage("First", "body");
      story.AddPassage(first);
      story.StartNode = first.Pid;

      string output = new TweeWriter().Write(story);
      Assert.Contains(":: StoryTitle\nMyStory", output);
      Assert.Contains("\"ifid\": \"ABC-123\"", output);
      Assert.Contains("\"format\": \"Harlowe\"", output);
      Assert.Contains("\"start\": \"First\"", output);
    }

    [Fact]
    public void StoryDataExtras_PublicallyWritable_PreservedThroughWriter()
    {
      var story = new Harlowe();
      story.StoryDataExtras = new Dictionary<string, object>
      {
        { "tag-colors", new Dictionary<string, object> { { "important", "red" } } },
        { "zoom", 2.0 },
      };
      story.AddPassage(MakePassage("First", "body"));

      string output = new TweeWriter().Write(story);
      Assert.Contains("\"tag-colors\":", output);
      Assert.Contains("\"important\": \"red\"", output);
      Assert.Contains("\"zoom\": 2", output);
    }

    [Fact]
    public void RoundTrip_AddedPassageShowsUpInOutputAndReread()
    {
      var story = new TweeReader().Read(":: First\nA");
      story.AddPassage(MakePassage("Added", "B"));
      string output = new TweeWriter().Write(story);

      var roundTripped = new TweeReader().Read(output);
      Assert.Equal(2, roundTripped.PassageCount);
      Assert.NotNull(roundTripped.GetPassage("Added"));
      Assert.Equal("B", roundTripped.GetPassage("Added").RawBody);
    }

    [Fact]
    public void RoundTrip_RemovedPassageGoneFromOutput()
    {
      var story = new TweeReader().Read(":: First\nA\n\n:: Second\nB");
      Assert.True(story.RemovePassage("Second"));
      string output = new TweeWriter().Write(story);
      Assert.DoesNotContain(":: Second", output);
    }

    [Fact]
    public void RoundTrip_RenamedPassageReflectedInOutputHeader()
    {
      var story = new TweeReader().Read(":: Old\nbody");
      story.RenamePassage("Old", "New");
      string output = new TweeWriter().Write(story);
      Assert.Contains(":: New", output);
      Assert.DoesNotContain(":: Old", output);
    }

    // --- Pid synthesis (M3 — must not collide with explicit/post-remove pids) ---

    [Fact]
    public void AddPassage_AfterRemove_DoesNotReusePidOfRemainingPassage()
    {
      // Two synthesized pids ("1", "2"). Remove the first; the next
      // AddPassage must NOT synthesize "2" (which would collide).
      var story = new Harlowe();
      story.AddPassage(MakePassage("A"));
      story.AddPassage(MakePassage("B"));
      Assert.Equal("1", story.GetPassage("A").Pid);
      Assert.Equal("2", story.GetPassage("B").Pid);

      story.RemovePassage("A");
      story.AddPassage(MakePassage("C"));
      // Max numeric pid was 2 → next is 3. Crucially not "2", which is B's.
      Assert.Equal("3", story.GetPassage("C").Pid);
    }

    [Fact]
    public void AddPassage_AfterExplicitHighPid_StartsAboveIt()
    {
      // An explicit pid > Count must still leave the synthesizer collision-
      // free on the next AddPassage.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage
      {
        Name = "Manual", Pid = "10",
        Ast = new PassageBody { Children = new List<IBodyNode>() },
        Tags = new List<string>(), Branches = new List<Branch>()
      });
      story.AddPassage(MakePassage("Auto"));
      Assert.Equal("11", story.GetPassage("Auto").Pid);
    }

    [Fact]
    public void AddPassage_NonNumericPidsAreSkippedBySynthesizer()
    {
      // A non-numeric explicit pid shouldn't break the max-scan — it's just
      // skipped; the synthesizer keeps producing numerics.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage
      {
        Name = "Weird", Pid = "abc",
        Ast = new PassageBody { Children = new List<IBodyNode>() },
        Tags = new List<string>(), Branches = new List<Branch>()
      });
      story.AddPassage(MakePassage("Auto"));
      Assert.Equal("1", story.GetPassage("Auto").Pid);
    }

    // --- Passage ordering (M4 — rename must not move the entry) ---

    [Fact]
    public void Passages_EnumeratesInInsertionOrder()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("A"));
      story.AddPassage(MakePassage("B"));
      story.AddPassage(MakePassage("C"));

      var names = new List<string>();
      foreach (var p in story.Passages) names.Add(p.Name);
      Assert.Equal(new[] { "A", "B", "C" }, names);
    }

    [Fact]
    public void RenamePassage_PreservesPositionInPassagesEnumeration()
    {
      // Without an explicit order list, a rename = Remove+Add would push the
      // renamed entry to the end. The order list keeps it in place.
      var story = new Harlowe();
      story.AddPassage(MakePassage("A"));
      story.AddPassage(MakePassage("B"));
      story.AddPassage(MakePassage("C"));
      story.RenamePassage("B", "B2");

      var names = new List<string>();
      foreach (var p in story.Passages) names.Add(p.Name);
      Assert.Equal(new[] { "A", "B2", "C" }, names);
    }

    [Fact]
    public void RemovePassage_RemovesFromOrderList()
    {
      var story = new Harlowe();
      story.AddPassage(MakePassage("A"));
      story.AddPassage(MakePassage("B"));
      story.AddPassage(MakePassage("C"));
      story.RemovePassage("B");

      var names = new List<string>();
      foreach (var p in story.Passages) names.Add(p.Name);
      Assert.Equal(new[] { "A", "C" }, names);
    }

    [Fact]
    public void RoundTrip_RenameDoesNotReorderTweeOutput()
    {
      // Twee output emits passages in story.Passages order. Renaming the
      // middle passage must keep it in the middle of the output, not move
      // it to the end.
      var story = new TweeReader().Read(":: A\nfirst\n\n:: B\nsecond\n\n:: C\nthird");
      story.RenamePassage("B", "B2");
      string output = new TweeWriter().Write(story);
      int idxA = output.IndexOf(":: A", System.StringComparison.Ordinal);
      int idxB2 = output.IndexOf(":: B2", System.StringComparison.Ordinal);
      int idxC = output.IndexOf(":: C", System.StringComparison.Ordinal);
      Assert.True(idxA >= 0 && idxB2 > idxA && idxC > idxB2,
        $"expected A < B2 < C in output, got A={idxA} B2={idxB2} C={idxC}");
    }

    [Fact]
    public void TweeWriter_AstNulledByConsumer_FallsBackToRawBody()
    {
      // Public-field DTO: a consumer can null a passage's Ast after adding it.
      // The writer must fall back to RawBody rather than throw or emit nothing.
      var story = new Harlowe();
      story.AddPassage(MakePassage("P", "raw text"));   // MakePassage marks IsDirty
      story.GetPassage("P").Ast = null;
      Assert.Contains("raw text", new TweeWriter().Write(story));
    }

    [Fact]
    public void AddPassage_TokenizerFailure_RecoversWithStub()
    {
      // `=<` throws in the tokenizer itself; AddPassage's AST hydration must
      // apply the same catch + stub recovery the loaders use rather than let
      // the exception escape to the caller.
      var story = new Harlowe();
      story.AddPassage(new HarlowePassage { Name = "Bad", RawBody = "(if: $x =< 5)[low]" });

      var stored = story.GetPassage("Bad");
      Assert.True(ParseErrorNode.IsWhollyParseError(stored.Ast));
      Assert.Equal("(if: $x =< 5)[low]", stored.RawBody);
    }
  }
}
