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
    {
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;

      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));
      var builder = new RenderTreeBuilder();
      new BodyRenderer(builder, registry, ctx).Render(ast);
      EnchantmentPass.Update(builder.Root, ctx.Enchantments);
      root = builder.Root;

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(builder.Root, buf);
      return buf;
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

    // --- Errors ---

    [Fact]
    public void Change_NonHookNameTarget_EmitsError()
    {
      var buf = Render("|m>[x](change: \"m\", (text-style: \"bold\"))", out _);
      var error = Assert.Single(buf.Entries, e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.Contains("hook name", error.Content);
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
    public void EnchantmentPassCannotMutatePendingGoto_StructuralGuard()
    {
      // StorySession.DispatchEvent runs EnchantmentPass.Update BEFORE checking
      // the deferred hook's PendingGoto. That ordering is only safe because
      // EnchantmentPass.Update has no path to mutate MacroContext.PendingGoto:
      //
      //   - EnchantmentPass.Update(root, enchantments) takes no MacroContext.
      //   - Changer.ApplyTo(container, source) — the enchant-path apply — also
      //     takes no MacroContext (only Changer.Apply, used by the *render*
      //     path, does).
      //
      // If a future refactor threads MacroContext into either signature, a
      // changer running inside the enchant pass could clobber the click's
      // queued (goto:) and the dispatch path would silently navigate to the
      // wrong target. Pin both signatures here so that regression vector
      // surfaces as a failing test instead of as a runtime bug.
      var updateMethod = typeof(EnchantmentPass).GetMethod(nameof(EnchantmentPass.Update));
      Assert.NotNull(updateMethod);
      foreach (var p in updateMethod.GetParameters())
        Assert.NotEqual(typeof(MacroContext), p.ParameterType);

      var applyToMethod = typeof(Changer).GetMethod(nameof(Changer.ApplyTo));
      Assert.NotNull(applyToMethod);
      foreach (var p in applyToMethod.GetParameters())
        Assert.NotEqual(typeof(MacroContext), p.ParameterType);
    }
  }
}
