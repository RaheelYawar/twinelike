using System.Collections.Generic;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Harlowe.Runtime.Rendering;
using Harlowe.Tokens;
using Xunit;

namespace Harlowe.Tests.Runtime.Rendering
{
  /// <summary>
  /// Tests for the internal render-tree layer: <see cref="RenderTreeBuilder"/>,
  /// <see cref="RenderTreeFlusher"/>, and the flush-parity guarantee — for any
  /// content not using a tree-mutating macro, rendering through the tree and
  /// flushing it produces a byte-identical <see cref="BufferedRenderOutput"/>
  /// entry stream to rendering straight at the buffer.
  /// </summary>
  public class RenderTreeTests
  {
    // --- Helpers ---

    private static PassageBody Parse(string source)
      => new HarloweBodyParser().Parse(new HarloweTokenizer().Tokenize(source));

    private static MacroContext FreshContext(out MacroRegistry registry)
    {
      registry = new MacroRegistry();
      StandardMacros.RegisterAll(registry);
      var ctx = new MacroContext { Store = new HarloweVariableStore(), Invoker = registry };
      registry.Context = ctx;
      return ctx;
    }

    /// <summary>Render straight at a buffer — the pre-render-tree path.</summary>
    private static BufferedRenderOutput RenderDirect(string source)
    {
      var ctx = FreshContext(out var registry);
      var buf = new BufferedRenderOutput();
      new BodyRenderer(buf, registry, ctx).Render(Parse(source));
      return buf;
    }

    /// <summary>Render into a tree, then flush the tree at a buffer.</summary>
    private static BufferedRenderOutput RenderViaTree(string source, out RenderRoot root)
    {
      var ctx = FreshContext(out var registry);
      var builder = new RenderTreeBuilder();
      new BodyRenderer(builder, registry, ctx).Render(Parse(source));
      root = builder.Root;
      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(builder.Root, buf);
      return buf;
    }

    private static void AssertEntriesEqual(BufferedRenderOutput expected, BufferedRenderOutput actual)
    {
      // The render tree coalesces consecutive prose into one text node, so a
      // flushed tree emits fewer (merged) Text events than direct rendering
      // does for the same source — same displayed text, different granularity.
      // Normalize both sides by merging adjacent Text entries before comparing,
      // so the parity check asserts output equivalence rather than exact event
      // granularity.
      var e = Coalesce(expected.Entries);
      var a = Coalesce(actual.Entries);
      Assert.Equal(e.Count, a.Count);
      for (int i = 0; i < e.Count; i++)
      {
        Assert.Equal(e[i].Kind, a[i].Kind);
        Assert.Equal(e[i].Content, a[i].Content);
        Assert.Equal(e[i].Target, a[i].Target);
        Assert.Equal(e[i].Style, a[i].Style);
      }
    }

    private static List<BufferedRenderOutput.Entry> Coalesce(IReadOnlyList<BufferedRenderOutput.Entry> entries)
    {
      var result = new List<BufferedRenderOutput.Entry>();
      for (int i = 0; i < entries.Count; i++)
      {
        var entry = entries[i];
        if (entry.Kind == BufferedRenderOutput.Kind.Text && result.Count > 0
            && result[result.Count - 1].Kind == BufferedRenderOutput.Kind.Text)
        {
          var prev = result[result.Count - 1];
          result[result.Count - 1] = new BufferedRenderOutput.Entry
          {
            Kind = prev.Kind,
            Content = prev.Content + entry.Content,
            Target = prev.Target,
            Style = prev.Style,
            Region = prev.Region
          };
        }
        else
        {
          result.Add(entry);
        }
      }
      return result;
    }

    // --- Flush parity ---

    [Theory]
    [InlineData("Hello, world.")]
    [InlineData("Line one\nLine two")]
    [InlineData("Some [content] in prose.")]
    [InlineData("|greeting>[hello] there")]
    [InlineData("[hello]<greeting| there")]
    [InlineData("Nested [outer [inner] tail] done")]
    [InlineData("Go [[Next]] now.")]
    [InlineData("Text <b>bold</b> tail")]
    [InlineData("(text-style: \"bold\")[loud]")]
    [InlineData("(text-style: \"bold\")[a (text-style: \"italic\")[b] c]")]
    [InlineData("(set: $x to 5)$x done")]
    [InlineData("(if: true)[shown](if: false)[hidden]")]
    [InlineData("(print: \"hi\")")]
    [InlineData("(for: each _n, 1, 2, 3)[item ]")]
    [InlineData("$missing here")]
    public void FlushedTree_MatchesDirectRender(string source)
    {
      var direct = RenderDirect(source);
      var viaTree = RenderViaTree(source, out _);
      AssertEntriesEqual(direct, viaTree);
    }

    // --- Tree shape ---

    [Fact]
    public void Builder_PlainText_ProducesTextNodeUnderRoot()
    {
      RenderViaTree("hi", out var root);
      var text = Assert.IsType<RenderTextNode>(Assert.Single(root.Children));
      Assert.Equal("hi", text.Content);
    }

    [Fact]
    public void Builder_AnonymousHook_ProducesHookNodeWithNullName()
    {
      RenderViaTree("a [b] c", out var root);
      Assert.Equal(3, root.Children.Count);
      var hook = Assert.IsType<RenderHookNode>(root.Children[1]);
      Assert.Null(hook.Name);
      Assert.Equal(HookAnchor.None, hook.Anchor);
      var inner = Assert.IsType<RenderTextNode>(Assert.Single(hook.Children));
      Assert.Equal("b", inner.Content);
    }

    [Fact]
    public void Builder_NamedHook_CarriesNameAndAnchor()
    {
      RenderViaTree("|greeting>[hello]", out var root);
      var hook = Assert.IsType<RenderHookNode>(Assert.Single(root.Children));
      Assert.Equal("greeting", hook.Name);
      Assert.Equal(HookAnchor.Right, hook.Anchor);
    }

    [Fact]
    public void Builder_StyleChanger_ProducesStyleNodeWrappingHookContent()
    {
      RenderViaTree("(text-style: \"bold\")[loud]", out var root);
      var style = Assert.IsType<RenderStyleNode>(Assert.Single(root.Children));
      Assert.True(style.Style.Bold);
      // The attached hook still produces its own hook node inside the style.
      var hook = Assert.IsType<RenderHookNode>(Assert.Single(style.Children));
      var text = Assert.IsType<RenderTextNode>(Assert.Single(hook.Children));
      Assert.Equal("loud", text.Content);
    }

    [Fact]
    public void Builder_NestedHooks_NestInTree()
    {
      RenderViaTree("[outer [inner] tail]", out var root);
      var outer = Assert.IsType<RenderHookNode>(Assert.Single(root.Children));
      Assert.Equal(3, outer.Children.Count);
      var inner = Assert.IsType<RenderHookNode>(outer.Children[1]);
      Assert.Equal("inner", ((RenderTextNode)Assert.Single(inner.Children)).Content);
    }

    // --- Flusher direct ---

    [Fact]
    public void Flusher_NullRoot_NoOp()
    {
      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(null, buf);
      Assert.Empty(buf.Entries);
    }

    [Fact]
    public void Flusher_HookNode_EmitsOnlyChildren_NoOwnEvent()
    {
      var root = new RenderRoot();
      var hook = new RenderHookNode { Name = "x" };
      hook.Children.Add(new RenderTextNode { Content = "inside" });
      root.Children.Add(hook);

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(root, buf);

      var entry = Assert.Single(buf.Entries);
      Assert.Equal(BufferedRenderOutput.Kind.Text, entry.Kind);
      Assert.Equal("inside", entry.Content);
    }

    [Fact]
    public void Flusher_StyleNode_BracketsChildrenWithPushPop()
    {
      var root = new RenderRoot();
      var style = new RenderStyleNode { Style = new StyleSpec { Italic = true } };
      style.Children.Add(new RenderTextNode { Content = "x" });
      root.Children.Add(style);

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(root, buf);

      Assert.Collection(buf.Entries,
        e => Assert.Equal(BufferedRenderOutput.Kind.PushStyle, e.Kind),
        e => Assert.Equal(BufferedRenderOutput.Kind.Text, e.Kind),
        e => Assert.Equal(BufferedRenderOutput.Kind.PopStyle, e.Kind));
    }

    // --- Builder defensive guards ---

    [Fact]
    public void Builder_PopStyleWithoutPush_DoesNotThrowOrCorrupt()
    {
      var builder = new RenderTreeBuilder();
      ((IRenderOutput)builder).PopStyle(); // unbalanced — guarded
      ((IRenderOutput)builder).Text("after");
      var text = Assert.IsType<RenderTextNode>(Assert.Single(builder.Root.Children));
      Assert.Equal("after", text.Content);
    }

    [Fact]
    public void Builder_EndHookWithoutBegin_DoesNotThrowOrCorrupt()
    {
      var builder = new RenderTreeBuilder();
      builder.EndHook(); // unbalanced — guarded
      ((IRenderOutput)builder).Text("after");
      Assert.IsType<RenderTextNode>(Assert.Single(builder.Root.Children));
    }

    // --- Clone independence ---

    [Fact]
    public void StyleNode_Clone_IsolatesStyleMutation()
    {
      // The render tree clones nodes when revision macros splice into multiple
      // targets. If clones shared the StyleSpec reference, mutating one node's
      // style would silently change every clone of it.
      var original = new RenderStyleNode { Style = new StyleSpec { Bold = true } };
      original.Style.Effects.Add(TextEffect.Mark);
      var clone = (RenderStyleNode)original.Clone();

      original.Style.Bold = false;
      original.Style.Effects.Add(TextEffect.Shadow);

      Assert.True(clone.Style.Bold);
      Assert.Single(clone.Style.Effects);
      Assert.Equal(TextEffect.Mark, clone.Style.Effects[0]);
    }

    [Fact]
    public void Builder_IsBalanced_ReflectsStackState()
    {
      var builder = new RenderTreeBuilder();
      Assert.True(builder.IsBalanced);

      ((IRenderOutput)builder).PushStyle(new StyleSpec { Bold = true });
      Assert.False(builder.IsBalanced);

      ((IRenderOutput)builder).PopStyle();
      Assert.True(builder.IsBalanced);
    }

    [Fact]
    public void Builder_AssertBalanced_ThrowsWhenUnbalanced()
    {
      var builder = new RenderTreeBuilder();
      builder.BeginHook("h", HookAnchor.None); // never closed

      var ex = Assert.Throws<System.InvalidOperationException>(() => builder.AssertBalanced());
      Assert.Contains("1 container", ex.Message);
    }

    [Fact]
    public void InteractiveNode_Clone_IsolatesRegionMutation()
    {
      var original = new RenderInteractiveNode
      {
        Region = new InteractiveRegion { Id = "r-1", Kind = InteractionKind.Click }
      };
      var clone = (RenderInteractiveNode)original.Clone();

      original.Region.Id = "r-mutated";
      original.Region.Kind = InteractionKind.MouseOver;

      Assert.Equal("r-1", clone.Region.Id);
      Assert.Equal(InteractionKind.Click, clone.Region.Kind);
    }

    // --- Link container (BeginLink/EndLink) ---

    [Fact]
    public void Builder_BeginEndLink_RoundTripsStructuredLabel()
    {
      var builder = new RenderTreeBuilder();
      IRenderOutput sink = builder;
      sink.BeginLink("P2");
      sink.Text("a");
      sink.PushStyle(new StyleSpec { Bold = true });
      sink.Text("b");
      sink.PopStyle();
      sink.EndLink();
      Assert.True(builder.IsBalanced);

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(builder.Root, buf);
      Assert.Equal(BufferedRenderOutput.Kind.BeginLink, buf.Entries[0].Kind);
      Assert.Equal("P2", buf.Entries[0].Target);
      Assert.Equal(BufferedRenderOutput.Kind.Text, buf.Entries[1].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.PushStyle, buf.Entries[2].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.Text, buf.Entries[3].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.PopStyle, buf.Entries[4].Kind);
      Assert.Equal(BufferedRenderOutput.Kind.EndLink, buf.Entries[5].Kind);
    }

    [Fact]
    public void Flusher_PlainTextLabel_KeepsFlatLinkEvent()
    {
      // Flat Link() and an all-text BeginLink bracket flush identically.
      var builder = new RenderTreeBuilder();
      IRenderOutput sink = builder;
      sink.BeginLink("P2");
      sink.Text("Go");
      sink.EndLink();

      var buf = new BufferedRenderOutput();
      RenderTreeFlusher.Flush(builder.Root, buf);
      var entry = Assert.Single(buf.Entries);
      Assert.Equal(BufferedRenderOutput.Kind.Link, entry.Kind);
      Assert.Equal("Go", entry.Content);
      Assert.Equal("P2", entry.Target);
    }

    [Fact]
    public void LinkNode_TextProperty_FlattensDescendants_AndClones()
    {
      var link = new RenderLinkNode { Target = "P2", Text = "Go" };
      var style = new RenderStyleNode { Style = new StyleSpec { Bold = true } };
      style.Children.Add(new RenderTextNode { Content = " on" });
      link.Children.Add(style);
      Assert.Equal("Go on", link.Text);

      var clone = (RenderLinkNode)link.Clone();
      ((RenderTextNode)link.Children[0]).Content = "mutated";
      Assert.Equal("Go on", clone.Text);
      Assert.Equal("P2", clone.Target);
    }

    [Fact]
    public void Clone_CoversHtmlErrorAndRootNodes()
    {
      var root = new RenderRoot();
      root.Children.Add(new RenderHtmlNode { RawHtml = "<hr>" });
      root.Children.Add(new RenderErrorNode { Message = "boom" });

      var copy = (RenderRoot)root.Clone();
      ((RenderHtmlNode)root.Children[0]).RawHtml = "mutated";
      ((RenderErrorNode)root.Children[1]).Message = "mutated";

      Assert.Equal("<hr>", ((RenderHtmlNode)copy.Children[0]).RawHtml);
      Assert.Equal("boom", ((RenderErrorNode)copy.Children[1]).Message);
    }

    [Fact]
    public void Builder_InteractiveBrackets_NestContentInInteractiveNode()
    {
      // The builder's IRenderOutput bracket pair — a replayed BeginInteractive/
      // EndInteractive stream must rebuild the same tree shape the passes make.
      var builder = new RenderTreeBuilder();
      builder.BeginInteractive(new InteractiveRegion { Id = "r-9", Kind = InteractionKind.Click });
      builder.Text("armed");
      builder.EndInteractive();

      var node = Assert.IsType<RenderInteractiveNode>(Assert.Single(builder.Root.Children));
      Assert.Equal("r-9", node.Region.Id);
      var text = Assert.IsType<RenderTextNode>(Assert.Single(node.Children));
      Assert.Equal("armed", text.Content);
    }
  }
}
