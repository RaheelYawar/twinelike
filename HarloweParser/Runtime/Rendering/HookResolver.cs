using System;
using System.Collections.Generic;
using Harlowe.Ast.Expression;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Resolves a <see cref="HookNameValue"/> against a render tree — the
  /// analogue of the reference Harlowe runtime's <c>selectHook</c>. A hook name
  /// is a query, so this runs fresh every time a macro consumes the name;
  /// nothing is cached.
  ///
  /// <para>
  /// A named reference matches every <see cref="RenderHookNode"/> whose name
  /// matches case-insensitively, in document order. Built-ins: <c>?passage</c>
  /// and <c>?page</c> both select the passage root (<c>?page</c> is the
  /// documented whole-passage styling idiom — cross-passage <c>?page</c> scope
  /// is a later concern); <c>?link</c> selects every
  /// <see cref="RenderLinkNode"/>. Ordinal <see cref="HookRefStep"/>s then
  /// narrow the match list left to right; an out-of-range step narrows to
  /// empty.
  /// </para>
  ///
  /// <para>
  /// In this foundation slice nothing in the runtime calls this yet — it is the
  /// primitive the revision/enchantment macros build on. It is exercised
  /// directly by tests.
  /// </para>
  /// </summary>
  public static class HookResolver
  {
    /// <summary>
    /// Resolve <paramref name="name"/> against <paramref name="tree"/>. Returns
    /// the matched nodes in document order, narrowed by the name's ordinal
    /// steps. Never null; empty on no match. Null-safe on both arguments.
    /// </summary>
    public static IReadOnlyList<RenderNode> Resolve(RenderNode tree, HookNameValue name)
    {
      var matches = new List<RenderNode>();
      if (tree == null || name == null) return matches;
      CollectMatches(tree, name.Name ?? string.Empty, matches);
      return ApplySteps(matches, name.Steps);
    }

    /// <summary>
    /// As <see cref="Resolve(RenderNode, HookNameValue)"/> over several trees,
    /// taken in order as one document, so an ordinal step counts across them.
    /// </summary>
    internal static IReadOnlyList<RenderNode> Resolve(List<RenderNode> trees, HookNameValue name)
    {
      var matches = new List<RenderNode>();
      if (name == null) return matches;
      for (int i = 0; i < trees.Count; i++) CollectMatches(trees[i], name.Name ?? string.Empty, matches);
      return ApplySteps(matches, name.Steps);
    }

    /// <summary>
    /// As <see cref="Resolve(RenderNode, HookNameValue)"/>, then without the
    /// matches that come after <paramref name="position"/> in page order: what
    /// follows its subtree, leaving its ancestors and its own content. This is
    /// reference's filter on a revision's targets while the passage renders,
    /// which runs after the ordinal steps have picked, so <c>?a's last</c> can
    /// pick a later hook and come to nothing. One walk does both. A position
    /// not in <paramref name="tree"/> filters nothing.
    /// </summary>
    internal static IReadOnlyList<RenderNode> ResolveBefore(RenderNode tree, HookNameValue name, RenderNode position)
    {
      var matches = new List<RenderNode>();
      if (tree == null || name == null) return matches;
      var order = new Dictionary<RenderNode, int>();
      int counter = 0, positionEnd = -1;
      CollectOrdered(tree, MatcherFor(name.Name ?? string.Empty, tree), position, matches, order, ref counter, ref positionEnd);
      var picked = ApplySteps(matches, name.Steps);
      if (positionEnd < 0 || picked.Count == 0) return picked;
      var before = new List<RenderNode>(picked.Count);
      for (int i = 0; i < picked.Count; i++)
        if (order[picked[i]] <= positionEnd) before.Add(picked[i]);
      return before;
    }

    private static void CollectOrdered(RenderNode node, Func<RenderNode, bool> predicate, RenderNode position,
      List<RenderNode> into, Dictionary<RenderNode, int> order, ref int counter, ref int positionEnd)
    {
      if (node == null) return;
      int index = counter++;
      if (predicate(node))
      {
        into.Add(node);
        order[node] = index;
      }
      if (node is IRenderContainer container)
      {
        var children = container.Children;
        for (int i = 0; i < children.Count; i++) CollectOrdered(children[i], predicate, position, into, order, ref counter, ref positionEnd);
      }
      if (ReferenceEquals(node, position)) positionEnd = counter - 1;
    }

    /// <summary>Compared as reference's <c>insensitiveName</c> does, ignoring case, dashes and underscores.</summary>
    private static void CollectMatches(RenderNode tree, string n, List<RenderNode> matches)
    {
      if (MacroNames.SameName(n, "passage") || MacroNames.SameName(n, "page"))
      {
        if (tree != null) matches.Add(tree);
        return;
      }
      Collect(tree, MatcherFor(n, tree), matches);
    }

    private static Func<RenderNode, bool> MatcherFor(string n, RenderNode tree)
    {
      // ?passage and ?page both select the passage root, which is where the
      // walk starts. A cross-passage ?page scope is a later concern.
      if (MacroNames.SameName(n, "passage") || MacroNames.SameName(n, "page"))
        return node => ReferenceEquals(node, tree);
      // Passage links and live (link:)-family labels — reference's ?Link
      // matches created <tw-link> elements too. A consumed single-use label
      // has its tag cleared by the dispatch, so it stops matching here.
      if (MacroNames.SameName(n, "link"))
        return node => node is RenderLinkNode
          || (node is RenderHookNode hook && hook.LabelRegionId != null);
      return node => node is RenderHookNode hook
        && hook.Name != null && MacroNames.SameName(hook.Name, n);
    }

    /// <summary>
    /// Depth-first pre-order walk collecting every node for which
    /// <paramref name="predicate"/> holds. The walk descends through every
    /// <see cref="IRenderContainer"/>, including matched nodes, so nested
    /// same-named hooks are all found.
    /// </summary>
    private static void Collect(RenderNode node, Func<RenderNode, bool> predicate, List<RenderNode> into)
    {
      if (node == null) return;
      if (predicate(node)) into.Add(node);
      if (node is IRenderContainer container)
      {
        var children = container.Children;
        for (int i = 0; i < children.Count; i++) Collect(children[i], predicate, into);
      }
    }

    /// <summary>
    /// Apply ordinal narrowing steps. Each step picks one node (1-based,
    /// optionally counted from the end) from the current match list and the
    /// remaining steps narrow that. An out-of-range index narrows to empty.
    /// </summary>
    private static IReadOnlyList<RenderNode> ApplySteps(List<RenderNode> matches, IReadOnlyList<HookRefStep> steps)
    {
      if (steps == null || steps.Count == 0) return matches;

      var current = matches;
      for (int s = 0; s < steps.Count && current.Count > 0; s++)
      {
        var step = steps[s];
        int idx = step.FromEnd ? current.Count - step.Index + 1 : step.Index;
        if (idx < 1 || idx > current.Count) return new List<RenderNode>();
        current = new List<RenderNode> { current[idx - 1] };
      }
      return current;
    }
  }
}
