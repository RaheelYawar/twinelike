using System;
using System.Collections.Generic;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the <c>(text-style:)</c> changer macro and its integration with
  /// the <c>+</c> composition operator and <c>BodyRenderer</c> hook
  /// application. Covers the three styles wired in 2.1A (bold, italic,
  /// underline); the broader styling set lands with 2.1B.
  /// </summary>
  public class TextStyleMacroTests
  {
    private static BufferedRenderOutput Render(string source)
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

    private static HarloweValue Eval(string macroSource)
    {
      // Wrap the macro in (set:) so the evaluator runs it through the
      // standard registry path. Returns the changer value.
      var ast = new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize("(print: " + macroSource + ")"));
      var registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var store = new HarloweVariableStore();
      var ctx = new MacroContext { Store = store, Invoker = registry };
      registry.Context = ctx;

      // The first child is the (print:) MacroNode; its first argument is the
      // macro call we want.
      var macroNode = (Ast.Body.MacroNode)ast.Children[0];
      var arg = macroNode.Arguments[0];
      var evaluator = new ExpressionEvaluator(store, null, registry);
      return evaluator.Evaluate(arg);
    }

    [Fact]
    public void ReturnsChangerValue()
    {
      var v = Eval("(text-style: \"bold\")");
      Assert.Equal(HarloweValueKind.Changer, v.Kind);
      Assert.NotNull(v.AsChanger);
    }

    [Fact]
    public void Bold_WrapsHookInBoldTags()
    {
      var buf = Render("(text-style: \"bold\")[hello]");
      Assert.Equal("<b>hello</b>", buf.Text);
    }

    [Fact]
    public void Italic_WrapsHookInItalicTags()
    {
      var buf = Render("(text-style: \"italic\")[hi]");
      Assert.Equal("<i>hi</i>", buf.Text);
    }

    [Fact]
    public void Underline_WrapsHookInUnderlineTags()
    {
      var buf = Render("(text-style: \"underline\")[hi]");
      Assert.Equal("<u>hi</u>", buf.Text);
    }

    [Fact]
    public void NoAttachedHook_EmitsNothingVisible()
    {
      // A bare changer with no hook is a value, not output. The macro evaluates
      // fine but produces no rendered text.
      var buf = Render("(text-style: \"bold\")");
      Assert.Equal(string.Empty, buf.Text);
    }

    [Fact]
    public void UnknownStyle_EmitsError()
    {
      var buf = Render("(text-style: \"sparkles\")[hi]");
      var err = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Contains("sparkles", err.Content);
    }

    [Fact]
    public void NonStringArg_EmitsError()
    {
      var buf = Render("(text-style: 5)[hi]");
      var err = buf.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.Error);
      Assert.NotNull(err);
      Assert.Contains("String", err.Content);
    }

    [Fact]
    public void Composed_InBodyPosition_DoesNotCompose_v2_1A()
    {
      // 2.1A scope-locking: Harlowe's `(m1)+(m2)[hook]` body syntax requires
      // either body-parser support for a `+`-chain between macros, or
      // stored-changer-then-hook — both deferred. In 2.1A the body parser
      // sees `(m1)`, a literal `+` text run, and `(m2)[hook]`; only the second
      // changer applies. This test pins the current behaviour so the next
      // slice can flip it intentionally.
      var buf = Render("(text-style: \"bold\") + (text-style: \"italic\") [hi]");
      Assert.Contains("<i>hi</i>", buf.Text);
      Assert.DoesNotContain("<b>", buf.Text);
    }

    [Fact]
    public void HookBetweenTags_RendersTextThroughTextChannel()
    {
      // Confirm the hook contents flow through Text (not Html) so plain prose
      // round-trips cleanly through the buffer's text accessor.
      var buf = Render("(text-style: \"bold\")[hi]");
      bool sawText = false;
      foreach (var e in buf.Entries)
        if (e.Kind == BufferedRenderOutput.Kind.Text && e.Content == "hi") { sawText = true; break; }
      Assert.True(sawText);
    }

    [Fact]
    public void StoredInVariable_ThenAppliedToHook_DoesNotApply_v2_1A()
    {
      // 2.1A scope: stored-changer-then-hook is deferred. A bare $variable
      // followed by a hook does NOT apply the stored changer in 2.1A — the
      // hook just renders normally. This locks the current behaviour so any
      // future stored-changer slice can flip it intentionally.
      var buf = Render("(set: $bold to (text-style: \"bold\"))$bold[hi]");
      Assert.DoesNotContain("<b>", buf.Text);
    }
  }
}
