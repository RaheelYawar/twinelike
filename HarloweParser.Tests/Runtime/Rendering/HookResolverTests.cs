using System.Collections.Generic;
using Harlowe.Ast.Expression;
using Harlowe.Runtime;
using Harlowe.Runtime.Rendering;
using Xunit;

namespace Harlowe.Tests.Runtime.Rendering
{
  /// <summary>
  /// Tests for <see cref="HookResolver"/> — the <c>selectHook</c> analogue that
  /// turns a <see cref="HookNameValue"/> query into the render-tree nodes it
  /// matches. Trees are hand-built so the resolver is exercised in isolation.
  /// </summary>
  public class HookResolverTests
  {
    private static RenderHookNode Hook(string name, params RenderNode[] children)
    {
      var h = new RenderHookNode { Name = name };
      foreach (var c in children) h.Children.Add(c);
      return h;
    }

    private static RenderTextNode Text(string s) => new RenderTextNode { Content = s };

    private static HookNameValue Name(string name, params HookRefStep[] steps)
      => new HookNameValue { Name = name, Steps = new List<HookRefStep>(steps) };

    // --- Named hooks ---

    [Fact]
    public void Resolve_NamedHook_FindsSingleMatch()
    {
      var cake = Hook("cake", Text("inside"));
      var root = new RenderRoot();
      root.Children.Add(Text("before "));
      root.Children.Add(cake);

      var matches = HookResolver.Resolve(root, Name("cake"));
      Assert.Same(cake, Assert.Single(matches));
    }

    [Fact]
    public void Resolve_NamedHook_IsCaseInsensitive()
    {
      var cake = Hook("Cake");
      var root = new RenderRoot();
      root.Children.Add(cake);
      Assert.Same(cake, Assert.Single(HookResolver.Resolve(root, Name("cAKe"))));
    }

    [Fact]
    public void Resolve_NamedHook_FindsAllMatchesInDocumentOrder()
    {
      var first = Hook("item", Text("1"));
      var second = Hook("item", Text("2"));
      var root = new RenderRoot();
      root.Children.Add(first);
      root.Children.Add(Hook("other"));
      root.Children.Add(second);

      var matches = HookResolver.Resolve(root, Name("item"));
      Assert.Equal(new RenderNode[] { first, second }, matches);
    }

    [Fact]
    public void Resolve_NamedHook_DescendsIntoNestedContainers()
    {
      var inner = Hook("target", Text("deep"));
      var outer = Hook("wrapper", Text("a"), inner);
      var root = new RenderRoot();
      root.Children.Add(outer);

      Assert.Same(inner, Assert.Single(HookResolver.Resolve(root, Name("target"))));
    }

    [Fact]
    public void Resolve_AnonymousHooks_AreSkippedByNameMatch()
    {
      var root = new RenderRoot();
      root.Children.Add(new RenderHookNode { Name = null });
      Assert.Empty(HookResolver.Resolve(root, Name("cake")));
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsEmpty()
    {
      var root = new RenderRoot();
      root.Children.Add(Hook("cake"));
      Assert.Empty(HookResolver.Resolve(root, Name("pie")));
    }

    // --- Built-ins ---

    [Fact]
    public void Resolve_Passage_SelectsTheRoot()
    {
      var root = new RenderRoot();
      root.Children.Add(Hook("cake"));
      Assert.Same(root, Assert.Single(HookResolver.Resolve(root, Name("passage"))));
    }

    [Fact]
    public void Resolve_Link_SelectsEveryLinkNode()
    {
      var l1 = new RenderLinkNode { Text = "a", Target = "A" };
      var l2 = new RenderLinkNode { Text = "b", Target = "B" };
      var root = new RenderRoot();
      root.Children.Add(l1);
      root.Children.Add(Hook("inside", l2));

      var matches = HookResolver.Resolve(root, Name("link"));
      Assert.Equal(new RenderNode[] { l1, l2 }, matches);
    }

    [Fact]
    public void Resolve_Page_DeferredToLaterSlice_ResolvesEmpty()
    {
      var root = new RenderRoot();
      root.Children.Add(Hook("cake"));
      Assert.Empty(HookResolver.Resolve(root, Name("page")));
    }

    // --- Ordinal steps ---

    [Fact]
    public void Resolve_ForwardOrdinal_NarrowsToThatMatch()
    {
      var a = Hook("item", Text("1"));
      var b = Hook("item", Text("2"));
      var c = Hook("item", Text("3"));
      var root = new RenderRoot();
      root.Children.Add(a);
      root.Children.Add(b);
      root.Children.Add(c);

      var matches = HookResolver.Resolve(root, Name("item", new HookRefStep { Index = 2 }));
      Assert.Same(b, Assert.Single(matches));
    }

    [Fact]
    public void Resolve_LastOrdinal_NarrowsToFinalMatch()
    {
      var a = Hook("item");
      var b = Hook("item");
      var root = new RenderRoot();
      root.Children.Add(a);
      root.Children.Add(b);

      var matches = HookResolver.Resolve(root, Name("item", new HookRefStep { Index = 1, FromEnd = true }));
      Assert.Same(b, Assert.Single(matches));
    }

    [Fact]
    public void Resolve_OrdinalOutOfRange_NarrowsToEmpty()
    {
      var root = new RenderRoot();
      root.Children.Add(Hook("item"));
      Assert.Empty(HookResolver.Resolve(root, Name("item", new HookRefStep { Index = 5 })));
    }

    [Fact]
    public void Resolve_NullArguments_ReturnEmpty()
    {
      Assert.Empty(HookResolver.Resolve(null, Name("cake")));
      Assert.Empty(HookResolver.Resolve(new RenderRoot(), null));
    }
  }
}
