using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Rendering;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the enchantment macros <c>(change:)</c> (one-shot) and
  /// <c>(enchant:)</c> (persistent). The harness mirrors
  /// <see cref="StorySession"/>: render into a <see cref="RenderTreeBuilder"/>,
  /// run the post-render <see cref="EnchantmentPass"/>, then flush.
  /// </summary>
  public class EnchantMacroTests
  {
    private static BufferedRenderOutput Render(string source, out RenderRoot root)
      => Render(source, out root, out _);

    private static BufferedRenderOutput Render(string source, out RenderRoot root, out MacroContext ctx)
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;

      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));
      var builder = new RenderTreeBuilder();
      new BodyRenderer(builder, registry, ctx).Render(ast);
      EnchantmentPass.Update(builder.Root, ctx.Enchantments, ctx);
      root = builder.Root;

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(builder.Root, buf);
      return buf;
    }

    private static BufferedRenderOutput Flush(RenderRoot root)
    {
      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(root, buf);
      return buf;
    }

    private static System.Collections.Generic.List<StyleSpec> PushedStyles(BufferedRenderOutput buf)
    {
      var styles = new System.Collections.Generic.List<StyleSpec>();
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.PushStyle) styles.Add(e.Style);
      return styles;
    }

    private static string Styled(BufferedRenderOutput buf)
    {
      // Render the entry stream compactly: <b>…</> brackets a bold style layer,
      // <i>…</> an italic one.
      var sb = new System.Text.StringBuilder();
      foreach (var e in buf.Entries)
      {
        switch (e.Kind)
        {
          case BufferedRenderOutput.Kind.Text: sb.Append(e.Content); break;
          case BufferedRenderOutput.Kind.PushStyle: sb.Append(e.Style.Bold ? "<b>" : "<i>"); break;
          case BufferedRenderOutput.Kind.PopStyle: sb.Append("</>"); break;
        }
      }
      return sb.ToString();
    }

    // --- (change:) — one-shot ---

    [Fact]
    public void Change_RestylesMatchAbove()
      => Assert.Equal("<b>loud</>", Styled(Render("|m>[loud](change: ?m, (text-style: \"bold\"))", out _)));

    [Fact]
    public void Change_RestylesAllMatchesOnce()
      => Assert.Equal("<b>1</><b>2</>",
        Styled(Render("|a>[1]|a>[2](change: ?a, (text-style: \"bold\"))", out _)));

    [Fact]
    public void Change_DoesNotCatchLaterHook()
    {
      // (change:) is one-shot — a hook declared after it is untouched.
      Assert.Equal("loud", Styled(Render("(change: ?m, (text-style: \"bold\"))|m>[loud]", out _)));
    }

    [Fact]
    public void Change_OrdinalNarrowedTarget_HitsOnlyThatMatch()
      => Assert.Equal("1<b>2</>",
        Styled(Render("|a>[1]|a>[2](change: ?a's last, (text-style: \"bold\"))", out _)));

    [Fact]
    public void Change_UnknownTarget_IsNoOp()
      => Assert.Equal("here", Styled(Render("|m>[here](change: ?other, (text-style: \"bold\"))", out _)));

    // --- (enchant:) — persistent ---

    [Fact]
    public void Enchant_CatchesHookDeclaredAfterTheMacro()
      => Assert.Equal("<b>late</>",
        Styled(Render("(enchant: ?late, (text-style: \"bold\"))|late>[late]", out _)));

    [Fact]
    public void Enchant_CatchesHookDeclaredBeforeTheMacro()
      => Assert.Equal("<b>early</>",
        Styled(Render("|early>[early](enchant: ?early, (text-style: \"bold\"))", out _)));

    [Fact]
    public void Enchant_ReAppliesAfterRevisionMutatesTheTree()
    {
      // The (replace:) rewrites ?x's content; the post-render enchant pass
      // still wraps the (now replaced) content.
      var buf = Render("|x>[old](enchant: ?x, (text-style: \"bold\"))(replace: ?x)[new]", out _);
      Assert.Equal("<b>new</>", Styled(buf));
    }

    [Fact]
    public void Enchant_Page_WrapsWholePassage()
      => Assert.Equal("<b>hello world</>",
        Styled(Render("(enchant: ?page, (text-style: \"bold\"))hello world", out _)));

    [Fact]
    public void Enchant_MultipleEnchantments_AllApply()
    {
      // Two separate enchantments over distinct hooks.
      var buf = Render("|a>[A]|b>[B](enchant: ?a, (text-style: \"bold\"))(enchant: ?b, (text-style: \"bold\"))", out _);
      Assert.Equal("<b>A</><b>B</>", Styled(buf));
    }

    [Fact]
    public void Enchant_ComposedChanger_NestsLayers()
    {
      var buf = Render("|m>[x](enchant: ?m, (text-style: \"bold\") + (text-style: \"italic\"))", out _);
      // Outer = first composed layer (bold), inner = italic.
      Assert.Equal("<b><i>x</></>", Styled(buf));
    }

    [Fact]
    public void Enchant_NoOutput_OfItsOwn()
    {
      // The macro itself prints nothing — only the enchant pass produces style.
      var buf = Render("before(enchant: ?gone, (text-style: \"bold\"))after", out _);
      Assert.Equal("beforeafter", buf.Text);
    }

    // --- String targets ---

    [Fact]
    public void Change_StringTarget_StylesOccurrences()
      => Assert.Equal("<b>gold</> here and <b>gold</>",
        Styled(Render("gold here and gold(change: \"gold\", (text-style: \"bold\"))", out _)));

    [Fact]
    public void Change_StringTarget_DoesNotCatchLaterText()
      => Assert.Equal("gold", Styled(Render("(change: \"gold\", (text-style: \"bold\"))gold", out _)));

    [Fact]
    public void Enchant_StringTarget_CatchesTextDeclaredAfter()
      => Assert.Equal("buy <b>gold</>",
        Styled(Render("(enchant: \"gold\", (text-style: \"bold\"))buy gold", out _)));

    [Fact]
    public void Enchant_StringTarget_IdempotentAcrossPasses()
    {
      var buf = Render("(enchant: \"o\", (text-style: \"bold\"))look", out var root, out var ctx);
      Assert.Equal("l<b>o</><b>o</>k", Styled(buf));

      // A dispatch re-runs the pass; the disenchant sweep must unwind the
      // previous pass's occurrence wraps too, or wraps nest one level per run.
      EnchantmentPass.Update(root, ctx.Enchantments, ctx);
      EnchantmentPass.Update(root, ctx.Enchantments, ctx);
      Assert.Equal("l<b>o</><b>o</>k", Styled(Flush(root)));
    }

    // --- via lambdas ---

    [Fact]
    public void Change_ViaLambda_AppliesPerMatch()
      => Assert.Equal("<b>1</><b>2</>",
        Styled(Render("|a>[1]|a>[2](change: ?a, via (text-style: \"bold\"))", out _)));

    [Fact]
    public void Change_ViaLambda_PosDistinguishesMatches()
    {
      var styles = PushedStyles(Render("|a>[1]|a>[2](change: ?a, via (opacity: pos * 0.25))", out _));
      Assert.Equal(2, styles.Count);
      Assert.Equal(0.25, styles[0].Opacity);
      Assert.Equal(0.5, styles[1].Opacity);
    }

    [Fact]
    public void Enchant_ViaLambda_OnStringTarget_PosCountsOccurrences()
    {
      var styles = PushedStyles(Render("(enchant: \"o\", via (opacity: pos * 0.25))oo", out _));
      Assert.Equal(2, styles.Count);
      Assert.Equal(0.25, styles[0].Opacity);
      Assert.Equal(0.5, styles[1].Opacity);
    }

    [Fact]
    public void Change_ViaLambda_NonChangerResult_ReplacesMatchWithError()
    {
      var buf = Render("|a>[x](change: ?a, via 5)", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("must return a changer", error.Content);
      Assert.DoesNotContain("x", buf.Text);
    }

    [Fact]
    public void Change_ViaLambda_ErrorStopsRemainingMatches()
    {
      // Reference replaces the first failing match with the error and ignores
      // the rest of the scope — the second hook survives, unstyled.
      var buf = Render("|a>[1]|a>[2](change: ?a, via 5)", out _);
      Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.DoesNotContain("1", buf.Text);
      Assert.Contains("2", buf.Text);
      Assert.Empty(PushedStyles(buf));
    }

    // --- Empty hooks (reference's `:empty` skip) ---

    [Fact]
    public void Enchant_EmptyHook_NotEnchanted()
      => Assert.Empty(PushedStyles(Render("|a>[](enchant: ?a, (text-style: \"bold\"))", out _)));

    [Fact]
    public void Change_ViaLambda_EmptyHookDoesNotAdvancePos()
    {
      var styles = PushedStyles(Render("|a>[]|a>[x](change: ?a, via (opacity: pos * 0.25))", out _));
      var style = Assert.Single(styles);
      Assert.Equal(0.25, style.Opacity);
    }

    // --- Errors ---

    [Fact]
    public void Change_NumberTarget_EmitsError()
    {
      var buf = Render("|m>[x](change: 5, (text-style: \"bold\"))", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("hook name or string", error.Content);
    }

    [Fact]
    public void Change_RevisionChangerSecondArg_EmitsError()
    {
      var buf = Render("|a>[x](change: ?a, (replace: ?b))", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("can't include a revision", error.Content);
    }

    [Fact]
    public void Enchant_InteractionChangerSecondArg_EmitsError()
    {
      var buf = Render("|a>[x](enchant: ?a, (click: ?b))", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("can't include a revision, enchantment, or interaction changer", error.Content);
    }

    [Fact]
    public void Enchant_ComposedInteractionChanger_EmitsError()
    {
      var buf = Render("|a>[x](enchant: ?a, (text-style: \"bold\") + (click: ?b))", out _);
      Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Empty(PushedStyles(buf));
    }

    [Fact]
    public void Enchant_WhereLambdaSecondArg_EmitsError()
    {
      var buf = Render("|a>[x](enchant: ?a, _x where _x > 0)", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("'via' lambda", error.Content);
    }

    [Fact]
    public void Change_NonChangerSecondArg_EmitsError()
    {
      var buf = Render("|m>[x](change: ?m, 5)", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("changer", error.Content);
    }

    [Fact]
    public void Enchant_NonChangerSecondArg_EmitsError()
    {
      var buf = Render("(enchant: ?m, \"bold\")", out _);
      Assert.Contains(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
    }

    // --- EnchantmentPass directly ---

    [Fact]
    public void EnchantmentPass_NullArguments_NoOp()
    {
      EnchantmentPass.Update(null, new System.Collections.Generic.List<Enchantment>());
      EnchantmentPass.Update(new RenderRoot(), null);
    }

    [Fact]
    public void EnchantmentPass_MalformedEntries_Skipped()
    {
      var root = new RenderRoot();
      var hook = new RenderHookNode { Name = "m" };
      hook.Children.Add(new RenderTextNode { Content = "x" });
      root.Children.Add(hook);

      EnchantmentPass.Update(root, new System.Collections.Generic.List<Enchantment>
      {
        null,
        new Enchantment { Target = null, Changer = null },
        new Enchantment { Target = new HookNameValue { Name = "m" }, Changer = null },
      });

      // Nothing applied — the hook's content is untouched.
      Assert.IsType<RenderTextNode>(Assert.Single(hook.Children));
    }

    [Fact]
    public void EnchantmentPassCannotMutatePendingGoto()
    {
      // StorySession.DispatchEvent runs EnchantmentPass.Update BEFORE checking
      // the deferred hook's PendingGoto, so pass-time evaluation must never
      // clobber a queued navigation. Since via-lambdas run macros through the
      // context's invoker, a (goto:) reached inside one *does* stage a
      // navigation — the pass must revert it (a lambda's job is to produce a
      // changer; reference never navigates there because its commands don't
      // run in expression position at all).
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;

      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(
        "|m>[x](enchant: ?m, via (goto: \"Elsewhere\"))"));
      var builder = new RenderTreeBuilder();
      new BodyRenderer(builder, registry, ctx).Render(ast);

      ctx.PendingGoto = "Queued"; // the click-deferred hook's navigation
      EnchantmentPass.Update(builder.Root, ctx.Enchantments, ctx);
      Assert.Equal("Queued", ctx.PendingGoto);

      // The lambda still failed loudly — (goto:) doesn't produce a changer.
      var buf = Flush(builder.Root);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("must return a changer", error.Content);
    }

    [Fact]
    public void EnchantmentPass_ViaLambda_DoesNotGrowRegistrationList()
    {
      // A via-lambda's job is to produce a changer, but our evaluator runs
      // command macros in expression position, so an inner (enchant:) registers
      // against the very list the pass is iterating. Without the side-effect
      // guard that registration leaks and re-appends every pass, so the list
      // grows unboundedly across dispatches. The guard rolls it back.
      var buf = Render("|a>[x]|b>[y](enchant: ?a, via (enchant: ?b, (text-style: \"bold\")))",
        out var root, out var ctx);
      Assert.Single(ctx.Enchantments);

      EnchantmentPass.Update(root, ctx.Enchantments, ctx);
      EnchantmentPass.Update(root, ctx.Enchantments, ctx);
      Assert.Single(ctx.Enchantments);
    }

    [Fact]
    public void EnchantmentPass_ViaLambda_DoesNotAdvanceRng()
    {
      // A pass-time (random:) draw would desync the reproducible RNG the save
      // model records off SeedIter. The guard rolls the draw back, so the pass
      // leaves no net advance even though the lambda ran (and failed).
      var buf = Render("|a>[x](enchant: ?a, via (random: 1, 6))", out var root, out var ctx);
      Assert.Equal(0, ctx.Rng.SeedIter);

      EnchantmentPass.Update(root, ctx.Enchantments, ctx);
      Assert.Equal(0, ctx.Rng.SeedIter);
    }

    // --- via-lambda failure modes ---

    private static System.Collections.Generic.List<string> Errors(BufferedRenderOutput buf)
    {
      var list = new System.Collections.Generic.List<string>();
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Error) list.Add(e.Content);
      return list;
    }

    [Fact]
    public void Enchant_ViaLambdaEvaluationError_ReplacesMatchWithIt()
    {
      // The lambda's clause itself errors — the match is swapped for the error
      // (reference's e.replaceWith(error.render())), the rest of the pass stops.
      var buf = Render("gold(enchant: \"gold\", via $missing)", out _);
      var err = Assert.Single(Errors(buf));
      Assert.Contains("$missing is not set", err);
      Assert.DoesNotContain("gold", buf.Text);
    }

    [Fact]
    public void Enchant_ViaLambdaNonEnchantableChanger_ReplacesMatchWithError()
    {
      // The lambda produced a changer, but a revision changer can't enchant —
      // the same notRevisionChanger gate the direct-changer form applies.
      var buf = Render("gold(enchant: \"gold\", via (replace: ?m))", out _);
      var err = Assert.Single(Errors(buf));
      Assert.Contains("revision", err);
      Assert.DoesNotContain("gold", buf.Text);
    }

    [Fact]
    public void Enchant_PageTarget_ViaFailure_ReplacesRootContent()
    {
      // ?page resolves to the root, which has no parent to be replaced in —
      // its content becomes the error instead.
      var buf = Render("gold(enchant: ?page, via (replace: ?m))", out _);
      var err = Assert.Single(Errors(buf));
      Assert.Contains("via", err);
      Assert.DoesNotContain("gold", buf.Text);
    }
  }
}
