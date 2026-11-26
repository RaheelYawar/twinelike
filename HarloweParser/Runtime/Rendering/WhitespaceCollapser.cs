using System.Collections.Generic;
using System.Text;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Applies collapsing whitespace to every <c>{…}</c> span in a render tree —
  /// reference Harlowe's <c>collapse()</c> in <c>ts/utils/renderutils.ts</c>.
  /// Inside a span, each run of whitespace becomes one space, a space that
  /// follows whitespace or starts the span is dropped, and the span's end is
  /// trimmed back to its last visible text.
  ///
  /// <para>Text from a macro, variable or <c>[[link]]</c> counts as solid: it
  /// is kept exactly as printed and stops trimming from crossing it, as
  /// reference treats text inside a <c>&lt;tw-expression&gt;</c>. A
  /// <c>{…}</c> inside printed content collapses in its own right.</para>
  ///
  /// <para>Idempotent, so the session can re-run it after a click reveals more
  /// content inside a span. The re-run sees the revealed text next to its
  /// already-collapsed neighbours and drops any doubled space between
  /// them.</para>
  /// </summary>
  public static class WhitespaceCollapser
  {
    /// <summary>One piece of text in document order, or a solid non-text item when <see cref="Node"/> is null.</summary>
    private struct Slot
    {
      public IRenderContainer Parent;
      public RenderTextNode Node;

      /// <summary>Which outermost span this belongs to, or -1 outside every span.</summary>
      public int Span;

      /// <summary>True for content kept as written.</summary>
      public bool Solid;
    }

    /// <summary>Collapses every span under <paramref name="root"/> in place. Null-safe.</summary>
    public static void Apply(IRenderContainer root)
    {
      if (root == null) return;

      var slots = new List<Slot>();
      int spanCount = 0;
      Collect(root, -1, false, slots, ref spanCount);
      if (spanCount == 0) return;

      int start = 0;
      while (start < slots.Count)
      {
        int span = slots[start].Span;
        int end = start + 1;
        while (end < slots.Count && slots[end].Span == span) end++;
        if (span >= 0) CollapseSpan(slots, start, end);
        start = end;
      }

      // Emptied text nodes would otherwise reach the host as empty text events.
      // Removed per parent in one sweep, since a span can empty thousands.
      var emptied = new HashSet<IRenderContainer>();
      for (int i = 0; i < slots.Count; i++)
      {
        var slot = slots[i];
        if (slot.Span >= 0 && !slot.Solid && slot.Node.Content.Length == 0) emptied.Add(slot.Parent);
      }
      foreach (var parent in emptied)
        parent.Children.RemoveAll(n => n is RenderTextNode t && string.IsNullOrEmpty(t.Content));
    }

    /// <summary>
    /// Lists every text node in document order, tagged with its outermost span.
    /// A collapsing hook clears <paramref name="solid"/> even inside printed
    /// content, matching reference's <c>parentsUntil(collapsing)</c> filter.
    /// </summary>
    private static void Collect(IRenderContainer container, int span, bool solid, List<Slot> slots, ref int spanCount)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        switch (children[i])
        {
          case RenderTextNode text:
            // Reference prints a kept line break as a <br>, which isn't a text
            // node, so text that is only line breaks is invisible to the trimming.
            if (!string.IsNullOrEmpty(text.Content) && !(solid && IsOnlyLineBreaks(text.Content)))
              slots.Add(new Slot { Parent = container, Node = text, Span = span, Solid = solid });
            break;
          case RenderErrorNode _:
            // An error shows visible text, so a space beside it is kept.
            slots.Add(new Slot { Parent = container, Span = span, Solid = true });
            break;
          case RenderHookNode hook when hook.Whitespace == WhitespaceMode.Collapse:
            Collect(hook, span >= 0 ? span : spanCount++, false, slots, ref spanCount);
            break;
          case RenderHookNode hook when hook.Whitespace == WhitespaceMode.Preserve:
            Collect(hook, span, true, slots, ref spanCount);
            break;
          case RenderLinkNode link:
            // A [[link]] renders inside <tw-expression> in reference.
            Collect(link, span, true, slots, ref spanCount);
            break;
          case IRenderContainer nested:
            Collect(nested, span, solid, slots, ref spanCount);
            break;
        }
      }
    }

    private static bool IsOnlyLineBreaks(string text)
    {
      for (int i = 0; i < text.Length; i++)
        if (text[i] != '\n') return false;
      return true;
    }

    /// <summary>Collapses the slots in <c>[start, end)</c>, which are one whole span.</summary>
    private static void CollapseSpan(List<Slot> slots, int start, int end)
    {
      // The neighbouring text counts only when it is in a collapsing span too.
      string previous = null;
      if (start > 0 && slots[start - 1].Span >= 0)
        previous = slots[start - 1].Node?.Content ?? "A";

      for (int i = start; i < end; i++)
      {
        var slot = slots[i];
        if (slot.Solid) { previous = "A"; continue; }

        string data = CollapseRuns(slot.Node.Content);
        if (data.Length > 0 && data[0] == ' ' && (string.IsNullOrEmpty(previous) || IsJsWhitespace(previous[previous.Length - 1])))
          data = data.Substring(1);
        slot.Node.Content = data;
        previous = data;
      }

      // Trim the end back to the last visible text, counting what was removed.
      int removed = 0;
      for (int i = end - 1; i >= start; i--)
      {
        var slot = slots[i];
        if (slot.Solid) break;

        string text = slot.Node.Content;
        int visible = text.Length;
        while (visible > 0 && IsJsWhitespace(text[visible - 1])) visible--;
        removed += text.Length - visible;
        slot.Node.Content = text.Substring(0, visible);
        if (visible > 0) break;
      }

      // More collapsing text follows, so one space must survive between them.
      if (removed > 0 && end < slots.Count && slots[end].Span >= 0)
      {
        var last = slots[end - 1];
        last.Node.Content += " ";
      }
    }

    /// <summary>What the page doesn't show yet: whether static text is still to be written after a render, and whether it lands in a span.</summary>
    internal enum PendingText
    {
      None,
      /// <summary>Some is to come inside a <c>{…}</c> span.</summary>
      InSpan,
      /// <summary>Some is to come at the top level of the render, which is in a span only if the render's container is.</summary>
      AtRenderRoot,
    }

    /// <summary>One text node of the page in document order, or an error (no <see cref="Node"/>), which reference shows as text.</summary>
    private struct PageText
    {
      public RenderTextNode Node;
      public bool InSpan;
    }

    /// <summary>Where the parts a collapse needs fall in the page's text.</summary>
    private sealed class PageWalk
    {
      public RenderNode Focus, Sentinel, Rendering, PendingRoot;
      public readonly List<PageText> Texts = new List<PageText>();
      public int FocusStart = -1, FocusEnd = -1, AfterSentinel = -1, RenderingEnd = -1;
      public bool FocusInSpan, PendingRootInSpan;
    }

    /// <summary>
    /// Reference's <c>collapse()</c> for one render made after the passage
    /// first rendered, which <c>Section.execute</c> runs when that render
    /// finishes, then <c>collapse</c> of each <c>{…}</c> span in it, outer first.
    /// The rest of the page isn't touched again, so its spacing stays as it was.
    /// <list type="bullet">
    /// <item>The before node is the nearest text before <paramref name="rendering"/>,
    /// and the after node the nearest text after <paramref name="sentinel"/>,
    /// which marks where reference's first new node ends. Each counts only if
    /// it's inside a real span. A line break is a <c>&lt;br&gt;</c> there, not
    /// text, so text is read in pieces between line breaks.</item>
    /// <item>Text <paramref name="pending"/> still to be written follows the
    /// render's own text and comes before the rest of the page.</item>
    /// <item>Emptied text nodes stay, as reference's do, except inside a span,
    /// whose collapse ends with <c>normalize()</c>, or inside the first node
    /// when that's a container.</item>
    /// </list>
    /// <paramref name="collapses"/> says whether the render itself collapses;
    /// null decides by whether it sits in a span, as a nested hook does.
    /// Returns false, collapsing nothing, when the page walk is over budget.
    /// </summary>
    internal static bool CollapseRendered(RenderRoot root, RenderHookNode rendering, RenderNode sentinel, bool? collapses,
      PendingText pending, IRenderContainer pendingRoot, Runtime.MacroContext ctx)
    {
      // A (hide:) of the hook while it was written has taken its content off
      // the page. Reference still collapses that content, with nothing before
      // or after it, since a detached node has no parent to search from.
      var detached = rendering.Children.Count == 0 ? rendering.Hidden?.Detached : null;
      if (rendering.Children.Count == 0 && (detached == null || detached.Count == 0)) return true;
      if (ctx != null && !ctx.TrySpendOnTargetWalk(root)) return false;
      var walk = Walk(root, rendering, sentinel, rendering, pendingRoot);
      if (walk.FocusStart < 0) return true;
      if (detached != null)
      {
        CollapseDetached(detached, collapses ?? walk.FocusInSpan);
        return true;
      }

      if (collapses ?? walk.FocusInSpan)
      {
        var nodes = new List<Slot>();
        CollectRendered(rendering, false, nodes);
        int afterFrom = walk.AfterSentinel >= 0 ? walk.AfterSentinel : walk.FocusStart;
        CollapseNodes(nodes, BeforeText(walk), HasAfterText(walk, afterFrom, pending));
        if (rendering.Children[0] is IRenderContainer first) RemoveEmptyText(first);
      }

      var spans = new List<RenderHookNode>();
      FindSpans(rendering, spans);
      for (int i = 0; i < spans.Count; i++)
      {
        if (ctx != null && !ctx.TrySpendOnTargetWalk(root)) return false;
        var spanWalk = Walk(root, spans[i], null, rendering, pendingRoot);
        if (spanWalk.FocusStart < 0) continue;
        var nodes = new List<Slot>();
        CollectRendered(spans[i], false, nodes);
        CollapseNodes(nodes, BeforeText(spanWalk), HasAfterText(spanWalk, spanWalk.FocusEnd, pending));
        RemoveEmptyText(spans[i]);
      }
      return true;
    }

    /// <summary>
    /// Collapses a render whose content a <c>(hide:)</c> took off the page, then
    /// the spans in it, each searching for before and after text only within
    /// the detached content, as reference's parent walk stops where the
    /// detached nodes end.
    /// </summary>
    private static void CollapseDetached(List<RenderNode> detached, bool collapses)
    {
      var holder = new RenderHookNode();
      holder.Children.AddRange(detached);
      if (collapses)
      {
        var nodes = new List<Slot>();
        CollectRendered(holder, false, nodes);
        CollapseNodes(nodes, null, false);
        if (detached[0] is IRenderContainer first) RemoveEmptyText(first);
      }
      // Each top-level detached node has no parent, so a span's search for
      // text before and after it ends at the edge of its own top-level node.
      for (int d = 0; d < detached.Count; d++)
      {
        var top = new RenderHookNode();
        top.Children.Add(detached[d]);
        var spans = new List<RenderHookNode>();
        FindSpans(top, spans);
        for (int i = 0; i < spans.Count; i++)
        {
          var walk = Walk(top, spans[i], null, top, null);
          var nodes = new List<Slot>();
          CollectRendered(spans[i], false, nodes);
          CollapseNodes(nodes, BeforeText(walk), HasAfterText(walk, walk.FocusEnd, PendingText.None));
          RemoveEmptyText(spans[i]);
        }
      }
    }

    private static PageWalk Walk(IRenderContainer root, RenderNode focus, RenderNode sentinel, RenderNode rendering, IRenderContainer pendingRoot)
    {
      var walk = new PageWalk { Focus = focus, Sentinel = sentinel, Rendering = rendering, PendingRoot = pendingRoot as RenderNode };
      WalkPage(root, false, walk);
      return walk;
    }

    private static void WalkPage(IRenderContainer container, bool inSpan, PageWalk walk)
    {
      if (ReferenceEquals(container, walk.PendingRoot)) walk.PendingRootInSpan = inSpan;
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        var child = children[i];
        if (ReferenceEquals(child, walk.Sentinel)) walk.AfterSentinel = walk.Texts.Count;
        if (ReferenceEquals(child, walk.Focus))
        {
          walk.FocusStart = walk.Texts.Count;
          walk.FocusInSpan = inSpan;
        }
        switch (child)
        {
          case RenderTextNode text:
            walk.Texts.Add(new PageText { Node = text, InSpan = inSpan });
            break;
          case RenderErrorNode _:
            walk.Texts.Add(new PageText { InSpan = inSpan });
            break;
          case IRenderContainer nested:
            WalkPage(nested, inSpan || (child is RenderHookNode hook && hook.IsSpan), walk);
            break;
        }
        if (ReferenceEquals(child, walk.Focus)) walk.FocusEnd = walk.Texts.Count;
        if (ReferenceEquals(child, walk.Rendering)) walk.RenderingEnd = walk.Texts.Count;
      }
    }

    /// <summary>
    /// The before node's text: the last piece of the nearest text before the
    /// focus that reference would see as a text node, or null when there's none
    /// or it isn't in a span. Reference takes the nearest one and then checks
    /// it, without looking further back.
    /// </summary>
    private static string BeforeText(PageWalk walk)
    {
      for (int i = walk.FocusStart - 1; i >= 0; i--)
      {
        var text = walk.Texts[i];
        if (!IsTextNode(text)) continue;
        if (!text.InSpan) return null;
        if (text.Node == null) return "A";
        string content = text.Node.Content;
        if (content.Length == 0) return content;
        int end = content.Length;
        while (end > 0 && content[end - 1] == '\n') end--;
        if (end == 0) continue;
        int start = content.LastIndexOf('\n', end - 1) + 1;
        return content.Substring(start, end - start);
      }
      return null;
    }

    /// <summary>
    /// Whether an after node exists: the nearest text node after
    /// <paramref name="from"/>, looked for in the render, then in text still to
    /// be written, then on the rest of the page, counts only if it's in a span.
    /// </summary>
    private static bool HasAfterText(PageWalk walk, int from, PendingText pending)
    {
      int renderingEnd = walk.RenderingEnd >= 0 ? walk.RenderingEnd : walk.FocusEnd;
      for (int i = from; i < renderingEnd; i++)
        if (IsTextNode(walk.Texts[i])) return walk.Texts[i].InSpan;
      if (pending == PendingText.InSpan) return true;
      if (pending == PendingText.AtRenderRoot) return walk.PendingRootInSpan;
      for (int i = renderingEnd; i < walk.Texts.Count; i++)
        if (IsTextNode(walk.Texts[i])) return walk.Texts[i].InSpan;
      return false;
    }

    /// <summary>True for text reference would have as a text node: anything but line breaks alone, which are <c>&lt;br&gt;</c>s there, an emptied node included.</summary>
    private static bool IsTextNode(PageText text)
    {
      if (text.Node == null) return true;
      string content = text.Node.Content;
      if (content.Length == 0) return true;
      for (int i = 0; i < content.Length; i++)
        if (content[i] != '\n') return true;
      return false;
    }

    /// <summary>
    /// The text of a later render, or of a span in one, in document order. Text
    /// in a <c>Preserve</c> hook, a link or an error is solid up to the nearest
    /// real span, as reference's <c>parentsUntil(collapsing)</c> filter reads
    /// the elements in between. A revision's collapsing layer is no element
    /// there, so it changes nothing.
    /// </summary>
    private static void CollectRendered(IRenderContainer container, bool solid, List<Slot> slots)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        switch (children[i])
        {
          case RenderTextNode text:
            if (!(solid && IsOnlyLineBreaks(text.Content)))
              slots.Add(new Slot { Parent = container, Node = text, Solid = solid });
            break;
          case RenderErrorNode _:
            slots.Add(new Slot { Parent = container, Solid = true });
            break;
          case RenderHookNode hook when hook.IsSpan:
            CollectRendered(hook, false, slots);
            break;
          case RenderHookNode hook when hook.Whitespace == WhitespaceMode.Preserve:
            CollectRendered(hook, true, slots);
            break;
          case RenderLinkNode link:
            CollectRendered(link, true, slots);
            break;
          case IRenderContainer nested:
            CollectRendered(nested, solid, slots);
            break;
        }
      }
    }

    /// <summary>
    /// The heart of reference's <c>collapse</c>: runs of whitespace become one
    /// space, a leading space goes when the text before ends in whitespace or
    /// is empty, the end is trimmed back to the last visible text, and one
    /// space comes back if anything was trimmed and an after node exists.
    /// </summary>
    private static void CollapseNodes(List<Slot> nodes, string before, bool hasAfter)
    {
      string previous = before;
      for (int i = 0; i < nodes.Count; i++)
      {
        var slot = nodes[i];
        if (slot.Solid) { previous = "A"; continue; }
        string data = CollapseRuns(slot.Node.Content);
        if (data.Length > 0 && data[0] == ' ' && (string.IsNullOrEmpty(previous) || IsJsWhitespace(previous[previous.Length - 1])))
          data = data.Substring(1);
        slot.Node.Content = data;
        previous = data;
      }

      int removed = 0;
      for (int i = nodes.Count - 1; i >= 0; i--)
      {
        var slot = nodes[i];
        if (slot.Solid) break;
        string text = slot.Node.Content;
        int visible = text.Length;
        while (visible > 0 && IsJsWhitespace(text[visible - 1])) visible--;
        removed += text.Length - visible;
        slot.Node.Content = text.Substring(0, visible);
        if (visible > 0) break;
      }

      if (removed > 0 && hasAfter)
        nodes[nodes.Count - 1].Node.Content += " ";
    }

    /// <summary>The real spans under <paramref name="container"/>, outer before inner, in document order.</summary>
    private static void FindSpans(IRenderContainer container, List<RenderHookNode> spans)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is RenderHookNode hook && hook.IsSpan) spans.Add(hook);
        if (children[i] is IRenderContainer nested) FindSpans(nested, spans);
      }
    }

    /// <summary>Removes every empty text node under <paramref name="container"/>, as <c>Node.normalize()</c> does.</summary>
    private static void RemoveEmptyText(IRenderContainer container)
    {
      var children = container.Children;
      children.RemoveAll(n => n is RenderTextNode t && t.Content.Length == 0);
      for (int i = 0; i < children.Count; i++)
        if (children[i] is IRenderContainer nested) RemoveEmptyText(nested);
    }

    /// <summary>Replaces each run of reference's <c>realWhitespace</c> with one space.</summary>
    private static string CollapseRuns(string text)
    {
      var sb = new StringBuilder(text.Length);
      bool inRun = false;
      for (int i = 0; i < text.Length; i++)
      {
        char c = text[i];
        if (CodePoints.IsRealWhitespace(c))
        {
          if (!inRun) sb.Append(' ');
          inRun = true;
        }
        else
        {
          sb.Append(c);
          inRun = false;
        }
      }
      return sb.ToString();
    }

    /// <summary>
    /// JavaScript's <c>\s</c>, which reference uses for its trimming checks. It
    /// differs from <c>realWhitespace</c> by also matching U+1680 and U+FEFF.
    /// Compared as numbers because U+2028 and U+2029 are line terminators to
    /// the C# lexer.
    /// </summary>
    private static bool IsJsWhitespace(char c)
    {
      int u = c;
      return CodePoints.IsRealWhitespace(c) || u == 0x1680 || u == 0xfeff;
    }
  }
}
