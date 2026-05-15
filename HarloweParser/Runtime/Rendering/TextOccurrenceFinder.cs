using System.Collections.Generic;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Finds literal substring occurrences in a render tree's prose and wraps
  /// each one as an addressable <see cref="RenderHookNode"/>, so string-target
  /// revision macros (<c>(replace: "old text")</c>) have something concrete to
  /// splice into. The wrap is performed in place: the containing
  /// <see cref="RenderTextNode"/> is split into leading text / wrap / trailing
  /// text, and the wraps are returned as the splice targets.
  ///
  /// <para>
  /// Matching is scoped to a single <see cref="RenderTextNode"/> — an
  /// occurrence split across two adjacent text nodes is not found. This matches
  /// the practical case (a literal run of prose is one text node) and keeps the
  /// finder simple; cross-node matching can be added later if a story needs it.
  /// Shared so the click-revision macros of a later sub-slice reuse it.
  /// </para>
  /// </summary>
  public static class TextOccurrenceFinder
  {
    /// <summary>
    /// Walk <paramref name="tree"/>, wrap every occurrence of
    /// <paramref name="needle"/> in a fresh anonymous <see cref="RenderHookNode"/>,
    /// and return those wraps in document order. An empty or null needle, or a
    /// null tree, yields an empty list and leaves the tree untouched.
    /// </summary>
    public static IReadOnlyList<RenderHookNode> FindAndWrap(RenderNode tree, string needle)
    {
      var wraps = new List<RenderHookNode>();
      if (tree == null || string.IsNullOrEmpty(needle)) return wraps;
      WrapInContainer(tree, needle, wraps);
      return wraps;
    }

    private static void WrapInContainer(RenderNode node, string needle, List<RenderHookNode> wraps)
    {
      if (!(node is IRenderContainer container)) return;

      var children = container.Children;
      // Recurse into existing child containers first, before this level's list
      // is rebuilt. Recursion mutates the children of those nested containers,
      // not this list, so the node instances here stay put.
      for (int i = 0; i < children.Count; i++) WrapInContainer(children[i], needle, wraps);

      // Then split any direct text-node child that contains the needle. The
      // wraps created here are not rescanned — recursion has already run, and
      // a wrap's only content is the needle itself, which would loop forever.
      var rebuilt = new List<RenderNode>(children.Count);
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is RenderTextNode text && text.Content != null
            && text.Content.IndexOf(needle, System.StringComparison.Ordinal) >= 0)
        {
          SplitTextNode(text.Content, needle, rebuilt, wraps);
        }
        else
        {
          rebuilt.Add(children[i]);
        }
      }
      children.Clear();
      children.AddRange(rebuilt);
    }

    private static void SplitTextNode(string content, string needle, List<RenderNode> into, List<RenderHookNode> wraps)
    {
      int from = 0;
      while (true)
      {
        int at = content.IndexOf(needle, from, System.StringComparison.Ordinal);
        if (at < 0)
        {
          if (from < content.Length)
            into.Add(new RenderTextNode { Content = content.Substring(from) });
          return;
        }
        if (at > from)
          into.Add(new RenderTextNode { Content = content.Substring(from, at - from) });

        var wrap = new RenderHookNode { Name = null };
        wrap.Children.Add(new RenderTextNode { Content = needle });
        into.Add(wrap);
        wraps.Add(wrap);

        from = at + needle.Length;
      }
    }
  }
}
