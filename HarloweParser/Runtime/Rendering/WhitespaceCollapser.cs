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
