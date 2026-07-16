using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Rendering;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the revision macros <c>(replace:)</c> / <c>(append:)</c> /
  /// <c>(prepend:)</c>. Revision changers mutate the render tree, so the
  /// harness renders through a <see cref="RenderTreeBuilder"/> and flushes —
  /// a revision applied at a plain buffer would have no tree to target.
  /// </summary>
  public class RevisionMacroTests
  {
    private static BufferedRenderOutput Render(string source, out RenderRoot root)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;

      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));
      var builder = new RenderTreeBuilder();
      new BodyRenderer(builder, registry, ctx).Render(ast);
      root = builder.Root;

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(builder.Root, buf);
      return buf;
    }

    private static string RenderText(string source) => Render(source, out _).Text;

    // --- Hook-name targeting ---

    [Fact]
    public void Replace_NamedHookAbove_SwapsContent()
      => Assert.Equal("new", RenderText("|cake>[old](replace: ?cake)[new]"));

    [Fact]
    public void Append_NamedHookAbove_AddsAfter()
      => Assert.Equal("old + extra", RenderText("|cake>[old](append: ?cake)[ + extra]"));

    [Fact]
    public void Prepend_NamedHookAbove_AddsBefore()
      => Assert.Equal("extra + old", RenderText("|cake>[old](prepend: ?cake)[extra + ]"));

    [Fact]
    public void Replace_LeftAnchoredHook_AlsoTargetable()
      => Assert.Equal("new", RenderText("[old]<cake|(replace: ?cake)[new]"));

    [Fact]
    public void Revision_AttachedHookNotShownInline()
    {
      // The `[new]` source is spliced into the target, never rendered at the
      // macro's own position.
      var buf = Render("|cake>[old]\n(replace: ?cake)[new]", out _);
      Assert.Equal("new\n", buf.Text);
    }

    [Fact]
    public void Replace_TargetNotYetRendered_IsNoOp()
    {
      // `?cake` is declared after the macro — nothing to target, so the source
      // is simply not shown, matching Harlowe.
      Assert.Equal("old", RenderText("(replace: ?cake)[new]|cake>[old]"));
    }

    [Fact]
    public void Replace_UnknownTarget_IsNoOp()
      => Assert.Equal("here", RenderText("|cake>[here](replace: ?pie)[gone]"));

    [Fact]
    public void Replace_MultipleMatches_AllUpdated()
      => Assert.Equal("XX", RenderText("|item>[a]|item>[b](replace: ?item)[X]"));

    [Fact]
    public void Replace_MultipleMatches_GetIndependentCopies()
    {
      // Each match must get its own clone — the tree stays a tree, and the
      // two targets hold distinct node instances.
      Render("|item>[a]|item>[b](replace: ?item)[X]", out var root);
      var hooks = new List<RenderHookNode>();
      foreach (var c in root.Children)
        if (c is RenderHookNode h && h.Name == "item") hooks.Add(h);
      Assert.Equal(2, hooks.Count);
      Assert.NotSame(hooks[0].Children[0], hooks[1].Children[0]);
    }

    [Fact]
    public void Replace_OrdinalNarrowedTarget_HitsOnlyThatMatch()
      => Assert.Equal("aX", RenderText("|item>[a]|item>[b](replace: ?item's last)[X]"));

    [Fact]
    public void Replace_PassageBuiltIn_ReplacesWholeRender()
    {
      // `?passage` resolves to the render root — replacing it clears everything
      // rendered so far and substitutes the source.
      Assert.Equal("fresh", RenderText("discard this(replace: ?passage)[fresh]"));
    }

    // --- String targeting ---

    [Fact]
    public void Replace_StringOccurrence_SwapsThatRun()
      => Assert.Equal("the new fox", RenderText("the old fox(replace: \"old\")[new]"));

    [Fact]
    public void Append_StringOccurrence_AddsAfterRun()
      => Assert.Equal("the old(!) fox", RenderText("the old fox(append: \"old\")[(!)]"));

    [Fact]
    public void Prepend_StringOccurrence_AddsBeforeRun()
      => Assert.Equal("the (!)old fox", RenderText("the old fox(prepend: \"old\")[(!)]"));

    [Fact]
    public void Replace_StringOccurrence_AllOccurrencesUpdated()
      => Assert.Equal("X and X", RenderText("ab and ab(replace: \"ab\")[X]"));

    [Fact]
    public void Replace_StringInsideAnonymousHook_StillFound()
      => Assert.Equal("new", RenderText("[old](replace: \"old\")[new]"));

    [Fact]
    public void Replace_StringNotPresent_IsNoOp()
      => Assert.Equal("the fox", RenderText("the fox(replace: \"cat\")[dog]"));

    [Fact]
    public void Replace_StringSpanningSpecialChar_StillFound()
      // The tokenizer splits the prose at '(', so "Hello (world)" used to land
      // in two adjacent render-text nodes and the needle was never found.
      // Coalescing text nodes lets the cross-boundary needle match.
      => Assert.Equal("Bye", RenderText("Hello (world)(replace: \"Hello (world)\")[Bye]"));

    [Fact]
    public void Replace_StringSpanningNewline_StillFound()
      // Newlines render as their own Text("\n") call; merging means a needle
      // containing the line break is matchable.
      => Assert.Equal("two", RenderText("one\ntwo(replace: \"one\ntwo\")[two]"));

    // --- String targets bestriding container boundaries (divergence #20) ---

    [Fact]
    public void Replace_StringBestridingFormatSpan_Found()
    {
      // The needle spans out of the ''bold'' span. Reference's findTextInNodes
      // matches "regardless of the actual DOM hierarchy which those matches
      // bestride"; before the flat-scan rework this silently no-op'd.
      Assert.Equal("Say X", RenderText("Say ''hello'' friend(replace: \"hello friend\")[X]"));
    }

    [Fact]
    public void Replace_BestridingMatch_RelocatesIntoFirstFragmentContainer()
    {
      // wrapAll semantics: the whole match moves to the first fragment's
      // position, so the replacement renders inside the bold span — exactly
      // what reference's pseudo-hook rehoming produces.
      var buf = Render("Say ''hello'' friend(replace: \"hello friend\")[X]", out _);
      int push = -1, text = -1, pop = -1;
      for (int i = 0; i < buf.Entries.Count; i++)
      {
        if (buf.Entries[i].Kind == BufferedRenderOutput.Kind.PushStyle) push = i;
        else if (buf.Entries[i].Kind == BufferedRenderOutput.Kind.Text && buf.Entries[i].Content == "X") text = i;
        else if (buf.Entries[i].Kind == BufferedRenderOutput.Kind.PopStyle) pop = i;
      }
      Assert.True(push >= 0 && push < text && text < pop,
        "the spliced X should render inside the bold span");
    }

    [Fact]
    public void Replace_StringSpanningWholeStyledWord_Found()
      // Three fragments: root text, the styled middle, root text. The wrap
      // lands at the first fragment's position (the root), leaving the style
      // span empty — reference's DOM shape after the same operation.
      => Assert.Equal("X", RenderText("a''b''c(replace: \"abc\")[X]"));

    [Fact]
    public void Append_StringBestridingFormatSpan_AddsAfter()
      => Assert.Equal("hello friend!", RenderText("''hello'' friend(append: \"hello friend\")[!]"));

    [Fact]
    public void Replace_StringSpanningIntoHook_Found()
      // The match starts in root prose and ends inside an anonymous hook.
      => Assert.Equal("X", RenderText("pre [fix](replace: \"pre fix\")[X]"));

    [Fact]
    public void Replace_MultipleOccurrences_FirstBestriding_AllFound()
      // Scanning resumes after each match, so a later same-node occurrence
      // is still found after a bestriding one.
      => Assert.Equal("X X", RenderText("''ab'' c ab c(replace: \"ab c\")[X]"));

    // --- Variadic targets (divergence #11, reference's rest(either(HookSet, String))) ---

    [Fact]
    public void Replace_TwoHookTargets_BothSpliced()
      => Assert.Equal("XX", RenderText("|a>[1]|b>[2](replace: ?a, ?b)[X]"));

    [Fact]
    public void Replace_MixedHookAndStringTargets_BothSpliced()
      => Assert.Equal("X X", RenderText("cat |a>[dog](replace: \"cat\", ?a)[X]"));

    [Fact]
    public void Append_TwoTargets_EachGetsOwnCopy()
      => Assert.Equal("1+2+", RenderText("|a>[1]|b>[2](append: ?a, ?b)[+]"));

    [Fact]
    public void Replace_EmptyStringTarget_EmitsError()
    {
      // Reference: "A string given to this (replace:) macro was empty." —
      // silently matching nothing hid the bug before.
      var buf = Render("(replace: \"\")[x]", out _);
      Assert.Contains(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                     && e.Content.Contains("was empty"));
    }

    [Fact]
    public void Replace_EmptyStringAmongValidTargets_StillErrors()
    {
      // reference's !scopes.every(Boolean) rejects the whole call.
      var buf = Render("|a>[1](replace: ?a, \"\")[x]", out _);
      Assert.Contains(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                     && e.Content.Contains("was empty"));
      Assert.Contains("1", buf.Text);   // target untouched
    }

    // --- Composed revision changers accumulate (reference's desc.newTargets) ---

    [Fact]
    public void ComposedReplace_BothTargetsSpliced()
      // Two composed revision changers used to overwrite each other
      // (last-wins); reference pushes both onto desc.newTargets.
      => Assert.Equal("XX", RenderText("|a>[1]|b>[2](replace: ?a)+(replace: ?b)[X]"));

    [Fact]
    public void ComposedMixedModes_EachTargetUsesOwnMode()
      // Reference: "(append: ?a) + (prepend: ?b)" works on one descriptor,
      // each newTarget pairing its own revision method.
      => Assert.Equal("AxxB", RenderText("|a>[A]|b>[B](append: ?a)+(prepend: ?b)[x]"));

    [Fact]
    public void ComposedDuplicateTarget_SplicedOnce()
      // Reference dedups identical (target, mode) pairs —
      // "(replace:?1) + (replace:?1, ?2)" — so the append lands once.
      => Assert.Equal("1!", RenderText("|a>[1](append: ?a)+(append: ?a)[!]"));

    // --- Composition with style changers ---

    [Fact]
    public void Replace_ComposedWithStyleChanger_StyleWrapsSplicedSource()
    {
      var buf = Render("|cake>[old](replace: ?cake)+(text-style: \"bold\")[loud]", out _);
      Assert.Equal("loud", buf.Text);
      Assert.Collection(buf.Entries,
        e => Assert.Equal(BufferedRenderOutput.Kind.PushStyle, e.Kind),
        e => { Assert.Equal(BufferedRenderOutput.Kind.Text, e.Kind); Assert.Equal("loud", e.Content); },
        e => Assert.Equal(BufferedRenderOutput.Kind.PopStyle, e.Kind));
    }

    // --- Errors ---

    [Fact]
    public void Replace_NonTargetArgument_EmitsError()
    {
      var buf = Render("|cake>[old](replace: 5)[new]", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("hook name or string", error.Content);
    }

    [Fact]
    public void Append_NonTargetArgument_EmitsError()
    {
      var buf = Render("(append: true)[x]", out _);
      Assert.Contains(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
    }

    [Fact]
    public void Revision_ComposedWithIteration_EmitsErrorNotSilentDrop()
    {
      // (replace: ?x) + (for: ...) combines two mutually-exclusive
      // hook-consuming changers. The engine runs only one; rather than silently
      // dropping the (for:) loop, it surfaces an in-prose error.
      var buf = Render("|x>[old](replace: ?x)+(for: each _i, 1, 2, 3)[_i]", out _);
      Assert.Contains(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error
                                     && e.Content.Contains("can't be combined"));
    }

    // --- Changer primitive ---

    [Fact]
    public void FromRevision_ProducesRevisionChanger()
    {
      var changer = Changer.FromRevision(new List<RevisionSpec>
      {
        new RevisionSpec
        {
          HookTarget = new HookNameValue { Name = "cake" },
          Mode = RevisionMode.Replace
        }
      });
      Assert.NotNull(changer);
    }

    [Fact]
    public void RevisionPatch_StructuralEquality()
    {
      HarloweValue ChangerFor(string name)
      {
        var registry = new MacroRegistry();
        StandardMacros.RegisterAll(registry);
        var tokens = new HarloweTokenizer().Tokenize("(replace: ?" + name + ")");
        var cursor = new TokenCursor(tokens);
        cursor.Advance();
        var node = new HarloweExpressionParser().ParseExpression(cursor);
        return new ExpressionEvaluator(new HarloweVariableStore(), null, registry).Evaluate(node);
      }

      Assert.Equal(ChangerFor("cake"), ChangerFor("cake"));
      Assert.NotEqual(ChangerFor("cake"), ChangerFor("pie"));
    }
  }
}
