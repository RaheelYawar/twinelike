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
  /// selects the passage root; <c>?link</c> selects every
  /// <see cref="RenderLinkNode"/>. <c>?page</c> is reserved but resolves to
  /// nothing here — it is a session-level scope wired in a later sub-slice.
  /// Ordinal <see cref="HookRefStep"/>s then narrow the match list left to
  /// right; an out-of-range step narrows to empty.
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

      string n = name.Name ?? string.Empty;
      if (string.Equals(n, "passage", StringComparison.OrdinalIgnoreCase))
      {
        matches.Add(tree);
      }
      else if (string.Equals(n, "link", StringComparison.OrdinalIgnoreCase))
      {
        Collect(tree, node => node is RenderLinkNode, matches);
      }
      else if (string.Equals(n, "page", StringComparison.OrdinalIgnoreCase))
      {
        // ?page is a session-level scope — wired in a later sub-slice.
      }
      else
      {
        Collect(tree, node => node is RenderHookNode hook
          && string.Equals(hook.Name ?? string.Empty, n, StringComparison.OrdinalIgnoreCase), matches);
      }

      return ApplySteps(matches, name.Steps);
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
