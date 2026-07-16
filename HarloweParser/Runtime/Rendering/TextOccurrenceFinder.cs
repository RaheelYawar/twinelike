using System.Collections.Generic;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Finds literal substring occurrences in a render tree's prose and wraps
  /// each one as an addressable <see cref="RenderHookNode"/>, so string-target
  /// macros (<c>(replace: "old text")</c>, <c>(enchant: "…", …)</c>, the
  /// <c>(click:)</c> family) have something concrete to splice into or wrap.
  ///
  /// <para>
  /// Matching runs over the tree's <em>flattened</em> text in document order,
  /// so an occurrence may bestride container boundaries — inline formatting,
  /// styled spans, links, hooks. This is reference Harlowe's
  /// <c>findTextInNodes</c> (<c>ts/utils/renderutils.ts</c>): it accumulates
  /// consecutive text nodes "to allow transformations of exact textual matches
  /// within passage text, <em>regardless</em> of the actual DOM hierarchy which
  /// those matches bestride", splitting the boundary nodes around each hit.
  /// </para>
  ///
  /// <para>
  /// One match still yields exactly one wrap: every matched fragment is
  /// <em>moved into</em> a single <see cref="RenderHookNode"/> placed at the
  /// first fragment's position — reference's <c>wrapTextNodes</c> does the
  /// same via jQuery <c>wrapAll</c>, which rehomes all of a match's text nodes
  /// into one <c>&lt;tw-pseudo-hook&gt;</c> at the first node's location. The
  /// rehoming is permanent there too (the pseudo-hook is unwrapped in place),
  /// so <c>(replace: "hello friend")</c> over <c>Say ''hello'' friend</c> puts
  /// the replacement <em>inside</em> the bold span, and a container whose text
  /// moved out entirely is left empty rather than pruned. Downstream consumers
  /// (revision splices, enchant/interaction tagging, the strip/disenchant
  /// sweeps) are unaffected: the wrap unit is one hook node, exactly as when
  /// matches were confined to a single text node.
  /// </para>
  /// </summary>
  public static class TextOccurrenceFinder
  {
    private struct Slot
    {
      public IRenderContainer Parent;
      public RenderTextNode Node;
    }

    /// <summary>
    /// Walk <paramref name="tree"/>, wrap every occurrence of
    /// <paramref name="needle"/> in a fresh anonymous <see cref="RenderHookNode"/>,
    /// and return those wraps in document order. Occurrences never overlap —
    /// scanning resumes after each match, as in reference. An empty or null
    /// needle, or a null tree, yields an empty list and leaves the tree
    /// untouched.
    /// </summary>
    public static IReadOnlyList<RenderHookNode> FindAndWrap(RenderNode tree, string needle)
    {
      var wraps = new List<RenderHookNode>();
      if (tree == null || string.IsNullOrEmpty(needle)) return wraps;

      // The flat text-node stream, reference's `dom.textNodes()`. Wraps made
      // below never enter it, so their contents are never rescanned.
      var pending = new List<Slot>();
      Collect(tree, pending);

      // Reference's examinedNodes/examinedText accumulation: pull slots until
      // the accumulated text contains the needle, extract, then continue from
      // the split-off tail.
      var examined = new List<Slot>();
      var text = new System.Text.StringBuilder();
      int next = 0;
      while (next < pending.Count)
      {
        var slot = pending[next++];
        examined.Add(slot);
        text.Append(slot.Node.Content ?? string.Empty);

        int index = text.ToString().IndexOf(needle, System.StringComparison.Ordinal);
        if (index < 0) continue;

        // The match's end always lies inside the newest slot (it would have
        // been found on an earlier iteration otherwise), so the leftover after
        // the match is entirely that slot's tail.
        int remaining = text.Length - (index + needle.Length);

        // Drop examined slots wholly before the match; they stay in place.
        int first = 0;
        while (index >= (examined[first].Node.Content ?? string.Empty).Length)
        {
          index -= (examined[first].Node.Content ?? string.Empty).Length;
          first++;
        }

        wraps.Add(Extract(examined, first, index, remaining, needle, out Slot? tail));
        if (tail.HasValue) pending.Insert(next, tail.Value);
        examined.Clear();
        text.Length = 0;
      }
      return wraps;
    }

    /// <summary>Collects every text node under <paramref name="node"/> with its parent, depth-first document order.</summary>
    private static void Collect(RenderNode node, List<Slot> slots)
    {
      if (!(node is IRenderContainer container)) return;
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is RenderTextNode t)
          slots.Add(new Slot { Parent = container, Node = t });
        else
          Collect(children[i], slots);
      }
    }

    /// <summary>
    /// Splits the matched fragments out of <paramref name="examined"/>[<paramref name="first"/>..]
    /// and moves them into one wrap at the first fragment's position (the
    /// <c>wrapAll</c> placement). <paramref name="offsetInFirst"/> is where the
    /// match starts inside the first fragment; <paramref name="remaining"/> is
    /// the unmatched tail length of the last fragment, returned via
    /// <paramref name="tail"/> (still in its own parent) for further scanning.
    /// </summary>
    private static RenderHookNode Extract(List<Slot> examined, int first, int offsetInFirst,
                                          int remaining, string needle, out Slot? tail)
    {
      tail = null;
      int last = examined.Count - 1;
      var wrap = new RenderHookNode { Name = null };

      if (first == last)
      {
        // Wholly inside one node: split into prefix / wrap(needle) / tail.
        var slot = examined[first];
        string content = slot.Node.Content ?? string.Empty;
        string prefix = content.Substring(0, offsetInFirst);
        string after = content.Substring(offsetInFirst + needle.Length);
        wrap.Children.Add(new RenderTextNode { Content = needle });

        int at = slot.Parent.Children.IndexOf(slot.Node);
        if (prefix.Length > 0)
        {
          slot.Node.Content = prefix;
          slot.Parent.Children.Insert(++at, wrap);
        }
        else
        {
          slot.Parent.Children[at] = wrap;
        }
        if (after.Length > 0)
        {
          var tailNode = new RenderTextNode { Content = after };
          slot.Parent.Children.Insert(at + 1, tailNode);
          tail = new Slot { Parent = slot.Parent, Node = tailNode };
        }
        return wrap;
      }

      // Bestriding match. First fragment: matched right side moves into the
      // wrap, which takes the fragment's place (after any unmatched prefix).
      var firstSlot = examined[first];
      string firstContent = firstSlot.Node.Content ?? string.Empty;
      wrap.Children.Add(new RenderTextNode { Content = firstContent.Substring(offsetInFirst) });
      int atFirst = firstSlot.Parent.Children.IndexOf(firstSlot.Node);
      if (offsetInFirst > 0)
      {
        firstSlot.Node.Content = firstContent.Substring(0, offsetInFirst);
        firstSlot.Parent.Children.Insert(atFirst + 1, wrap);
      }
      else
      {
        firstSlot.Parent.Children[atFirst] = wrap;
      }

      // Middle fragments are wholly matched: move each node itself.
      for (int i = first + 1; i < last; i++)
      {
        var mid = examined[i];
        mid.Parent.Children.Remove(mid.Node);
        wrap.Children.Add(mid.Node);
      }

      // Last fragment: matched head moves into the wrap; an unmatched tail
      // keeps the node in place (rescanned for further occurrences).
      var lastSlot = examined[last];
      string lastContent = lastSlot.Node.Content ?? string.Empty;
      int matchedLen = lastContent.Length - remaining;
      wrap.Children.Add(new RenderTextNode { Content = lastContent.Substring(0, matchedLen) });
      if (remaining > 0)
      {
        lastSlot.Node.Content = lastContent.Substring(matchedLen);
        tail = lastSlot;
      }
      else
      {
        lastSlot.Parent.Children.Remove(lastSlot.Node);
      }
      return wrap;
    }
  }
}
