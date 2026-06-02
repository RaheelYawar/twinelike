using System;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// End-to-end coverage for the <c>(for:)</c> body-iteration changer.
  /// Drives the full body parser → renderer pipeline so the assertions cover
  /// the descriptor-patch refactor as well as the macro itself.
  /// </summary>
  public class ForMacroTests
  {
    private class Harness
    {
      public BufferedRenderOutput Buf;
      public MacroContext Ctx;
      public HarloweVariableStore Store;
    }

    private static Harness Render(string source, Action<MacroContext> configure = null)
    {
      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();
      var ast = bodyParser.Parse(tokenizer.Tokenize(source));

      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);

      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = registry };
      registry.Context = ctx;
      configure?.Invoke(ctx);

      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(ast);

      return new Harness { Buf = buf, Ctx = ctx, Store = store };
    }

    // --- Happy path ---

    [Fact]
    public void For_IteratesHookOncePerItem()
    {
      var h = Render("(for: each _x, 1, 2, 3)[_x ]");
      Assert.Equal("1 2 3 ", h.Buf.Text);
    }

    [Fact]
    public void For_UsesItInsideHook()
    {
      // Inside the hook, `it` should refer to the current item — same value
      // as the named parameter — per Harlowe lambda semantics.
      var h = Render("(for: each _x, 1, 2, 3)[(print: it) ]");
      Assert.Equal("1 2 3 ", h.Buf.Text);
    }

    [Fact]
    public void For_StoryParamSigilWorks()
    {
      var h = Render("(for: each $item, \"a\", \"b\")[$item ]");
      Assert.Equal("a b ", h.Buf.Text);
    }

    [Fact]
    public void For_SingleArrayArg_AutoIterates()
    {
      var h = Render("(for: each _x, (a: 10, 20, 30))[_x;]");
      Assert.Equal("10;20;30;", h.Buf.Text);
    }

    [Fact]
    public void For_EmptyItems_RendersNothing()
    {
      var h = Render("before(for: each _x, (a:))[loop ]after");
      Assert.Equal("beforeafter", h.Buf.Text);
    }

    [Fact]
    public void For_ZeroItemArgs_RendersNothing()
    {
      // No items at all after the lambda (the arity reference allows via
      // zeroOrMore). The hook is simply not printed — no arg-count error.
      var h = Render("before(for: each _x)[loop ]after");
      Assert.Equal("beforeafter", h.Buf.Text);
    }

    [Fact]
    public void Loop_Alias_IteratesLikeFor()
    {
      var h = Render("(loop: each _x, 1, 2, 3)[_x ]");
      Assert.Equal("1 2 3 ", h.Buf.Text);
    }

    [Fact]
    public void Loop_Alias_ZeroItems_RendersNothing()
    {
      var h = Render("before(loop: each _x)[x ]after");
      Assert.Equal("beforeafter", h.Buf.Text);
    }

    // --- Scope hygiene ---

    [Fact]
    public void For_RestoresSurroundingBinding()
    {
      var h = Render("(set: _x to 99)(for: each _x, 1, 2)[_x ]_x");
      // After the loop, _x must be the surrounding 99 again, not the last item.
      Assert.Equal("1 2 99", h.Buf.Text);
    }

    [Fact]
    public void For_RestoresIt()
    {
      var h = Render("(set: $anchor to 42)(for: each _x, 1, 2)[_x ](print: it)");
      // After the loop, `it` should restore to its prior value (set by $anchor).
      Assert.Equal("1 2 42", h.Buf.Text);
    }

    // --- Error / arity ---

    [Fact]
    public void For_NonEachLambda_Errors()
    {
      var h = Render("(for: _x where _x > 0, 1, 2, 3)[_x ]");
      // Macro-level error surfaces via IRenderOutput.Error rather than text.
      Assert.Contains("each", string.Join(" ", CollectErrors(h.Buf)));
    }

    [Fact]
    public void For_NonLambdaFirstArg_Errors()
    {
      var h = Render("(for: 5, 1, 2)[content]");
      Assert.Contains("lambda", string.Join(" ", CollectErrors(h.Buf)));
    }

    [Fact]
    public void For_WithoutAttachedHook_RendersNothing()
    {
      // A changer without a hook is a no-op (the value is discarded). The
      // engine shouldn't crash, just emit nothing.
      var h = Render("before(for: each _x, 1, 2, 3)after");
      Assert.Equal("beforeafter", h.Buf.Text);
    }

    // --- Composition with style changers ---

    [Fact]
    public void For_ComposedWithStyleChanger_AppliesStylesPerIteration()
    {
      // Canonical chain form: no wrapping parens. Body parser folds
      // `(m1)+(m2)[hook]` into a ChangerChainNode.
      var h = Render("(text-style: \"bold\")+(for: each _x, 1, 2)[_x]");
      // 2 iterations × (PushStyle + Text + PopStyle) = 2 of each event.
      int pushes = 0, pops = 0, texts = 0;
      foreach (var e in h.Buf.Entries)
      {
        if (e.Kind == BufferedRenderOutput.Kind.PushStyle) pushes++;
        else if (e.Kind == BufferedRenderOutput.Kind.PopStyle) pops++;
        else if (e.Kind == BufferedRenderOutput.Kind.Text) texts++;
      }
      Assert.Equal(2, pushes);
      Assert.Equal(2, pops);
      Assert.Equal(2, texts);
    }

    [Fact]
    public void For_ReverseOrderComposition_StillStylesPerIteration()
    {
      // Iteration-then-style: should still wrap each per-item render with
      // the style. Patch order doesn't change observable behavior because
      // Apply always runs RunStyles inside the iteration body when iteration
      // is set.
      var h = Render("(for: each _x, 1, 2)+(text-style: \"italic\")[_x]");
      int pushes = 0, pops = 0, texts = 0;
      foreach (var e in h.Buf.Entries)
      {
        if (e.Kind == BufferedRenderOutput.Kind.PushStyle) pushes++;
        else if (e.Kind == BufferedRenderOutput.Kind.PopStyle) pops++;
        else if (e.Kind == BufferedRenderOutput.Kind.Text) texts++;
      }
      Assert.Equal(2, pushes);
      Assert.Equal(2, pops);
      Assert.Equal(2, texts);
    }

    [Fact]
    public void For_ChainedTwoIterations_LaterWinsSilently()
    {
      // Known v2.3D limitation: chaining two (for:)s silently overwrites the
      // first via IterationPatch.Apply. The first lambda + items are
      // dropped. Documenting the actual behavior so a regression surfaces if
      // we add an in-prose error later (likely once revision macros land).
      var h = Render("(for: each _x, 99, 99)+(for: each _y, 1, 2)[_y]");
      Assert.Equal("12", h.Buf.Text);
    }

    [Fact]
    public void For_ApplyWithoutContext_EmitsErrorRatherThanCrashing()
    {
      // Defensive: a unit-test caller that constructs a Changer.FromIteration
      // and calls Apply without a MacroContext should get an in-prose error,
      // not a NullReferenceException.
      var lambda = new LambdaValue
      {
        Node = new LambdaNode
        {
          ParameterName = "x",
          ParameterIsTemporary = true,
          IsEach = true,
        },
      };
      var changer = Changer.FromIteration(new IterationSpec
      {
        Lambda = lambda,
        Items = new System.Collections.Generic.List<HarloweValue> { HarloweValue.OfNumber(1) },
        ParamName = "x",
        ParamIsTemporary = true,
      });

      var buf = new BufferedRenderOutput();
      changer.Apply(buf, o => o.Text("X"), ctx: null);

      // No iterations happened — instead an error entry was emitted.
      var errors = CollectErrors(buf);
      Assert.NotEmpty(errors);
    }

    private static System.Collections.Generic.List<string> CollectErrors(BufferedRenderOutput buf)
    {
      var errs = new System.Collections.Generic.List<string>();
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Error) errs.Add(e.Content);
      return errs;
    }
  }
}
