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

    // --- Changer primitive ---

    [Fact]
    public void FromRevision_ProducesRevisionChanger()
    {
      var changer = Changer.FromRevision(new RevisionSpec
      {
        HookTarget = new HookNameValue { Name = "cake" },
        Mode = RevisionMode.Replace
      });
      Assert.NotNull(changer);
      Assert.False(changer.HasIteration);
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
