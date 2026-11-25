using System.Collections.Generic;
using System.Text;

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
    public static IReadOnlyList<RenderHookNode> FindAndWrap(RenderNode tree, string needle) => FindAndWrap(tree, needle, null);

    /// <summary>
    /// As <see cref="FindAndWrap(RenderNode, string)"/>, but only in the text
    /// up to the end of <paramref name="limit"/>'s content, for a revision
    /// that can only reach what's before it. A null limit, or one not in the
    /// tree, limits nothing.
    /// </summary>
    internal static IReadOnlyList<RenderHookNode> FindAndWrap(RenderNode tree, string needle, RenderNode limit)
    {
      var wraps = new List<RenderHookNode>();
      if (tree == null || string.IsNullOrEmpty(needle)) return wraps;

      // The flat text-node stream, reference's `dom.textNodes()`.
      var slots = new List<Slot>();
      bool reachedLimit = false;
      Collect(tree, slots, limit, ref reachedLimit);

      var contents = new string[slots.Count];
      var flat = new StringBuilder();
      for (int i = 0; i < slots.Count; i++)
      {
        contents[i] = slots[i].Node.Content ?? string.Empty;
        flat.Append(contents[i]);
      }
      string text = flat.ToString();

      // What stands in each touched text node's place. Parents are rebuilt once
      // at the end, because inserting per match would be quadratic on a long
      // page. A moved or fully matched node has an empty list.
      var replacements = new Dictionary<RenderNode, List<RenderNode>>();
      var parents = new HashSet<IRenderContainer>();
      List<RenderNode> PiecesOf(int index)
      {
        if (!replacements.TryGetValue(slots[index].Node, out var list))
        {
          list = new List<RenderNode>();
          replacements[slots[index].Node] = list;
          parents.Add(slots[index].Parent);
        }
        return list;
      }

      int slot = 0;      // the slot holding the scan position
      int slotStart = 0; // that slot's offset in the flat text
      int used = 0;      // how much of that slot earlier matches have taken
      for (int hit = text.IndexOf(needle, System.StringComparison.Ordinal);
           hit >= 0;
           hit = text.IndexOf(needle, hit + needle.Length, System.StringComparison.Ordinal))
      {
        while (hit >= slotStart + contents[slot].Length)
        {
          KeepTail(replacements, slots[slot].Node, contents[slot], used);
          slotStart += contents[slot].Length;
          slot++;
          used = 0;
        }

        int offset = hit - slotStart;
        var pieces = PiecesOf(slot);
        if (offset > used) pieces.Add(new RenderTextNode { Content = contents[slot].Substring(used, offset - used) });
        var wrap = new RenderHookNode { Name = null };
        pieces.Add(wrap);
        wraps.Add(wrap);

        int end = hit + needle.Length;
        if (end <= slotStart + contents[slot].Length)
        {
          wrap.Children.Add(new RenderTextNode { Content = needle });
          used = end - slotStart;
          continue;
        }

        // The match bestrides nodes. The first fragment's matched side moves
        // into the wrap, which sits at that fragment's position (wrapAll).
        wrap.Children.Add(new RenderTextNode { Content = contents[slot].Substring(offset) });
        slotStart += contents[slot].Length;
        slot++;

        // Middle fragments are wholly matched, so each node itself moves.
        while (end > slotStart + contents[slot].Length)
        {
          wrap.Children.Add(slots[slot].Node);
          PiecesOf(slot);
          slotStart += contents[slot].Length;
          slot++;
        }

        // The last fragment's matched head moves. Any tail stays in place.
        used = end - slotStart;
        wrap.Children.Add(new RenderTextNode { Content = contents[slot].Substring(0, used) });
        PiecesOf(slot);
      }

      if (wraps.Count == 0) return wraps;
      KeepTail(replacements, slots[slot].Node, contents[slot], used);

      foreach (var parent in parents)
      {
        var children = parent.Children;
        var rebuilt = new List<RenderNode>(children.Count);
        for (int i = 0; i < children.Count; i++)
        {
          if (replacements.TryGetValue(children[i], out var pieces)) rebuilt.AddRange(pieces);
          else rebuilt.Add(children[i]);
        }
        children.Clear();
        children.AddRange(rebuilt);
      }
      return wraps;
    }

    /// <summary>Leaves the unmatched end of a node the scan has finished with in that node's place.</summary>
    private static void KeepTail(Dictionary<RenderNode, List<RenderNode>> replacements, RenderTextNode node, string content, int used)
    {
      if (used < content.Length && replacements.TryGetValue(node, out var pieces))
        pieces.Add(new RenderTextNode { Content = content.Substring(used) });
    }

    /// <summary>Collects every text node under <paramref name="node"/> with its parent, depth-first document order.</summary>
    private static void Collect(RenderNode node, List<Slot> slots, RenderNode limit, ref bool reachedLimit)
    {
      if (!(node is IRenderContainer container)) return;
      var children = container.Children;
      for (int i = 0; i < children.Count && !reachedLimit; i++)
      {
        if (children[i] is RenderTextNode t)
          slots.Add(new Slot { Parent = container, Node = t });
        else
          Collect(children[i], slots, limit, ref reachedLimit);
      }
      if (ReferenceEquals(node, limit)) reachedLimit = true;
    }

  }
}
