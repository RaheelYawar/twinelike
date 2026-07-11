using System.Collections.Generic;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Walks a finished render tree and replays it as the flat
  /// <see cref="IRenderOutput"/> event stream a host engine consumes. The
  /// inverse of <see cref="RenderTreeBuilder"/>.
  ///
  /// <para>
  /// <b>Flush parity.</b> For any tree built from content that uses no
  /// tree-mutating macro, the emitted stream is byte-identical to what the
  /// pre-refactor <see cref="BodyRenderer"/> pushed directly. Text/Html/Link/
  /// Error map one-to-one; a <see cref="RenderStyleNode"/> brackets its children
  /// with <c>PushStyle</c>/<c>PopStyle</c>; a <see cref="RenderHookNode"/> is
  /// structural and emits only its children — exactly as a hook produced no
  /// output of its own before.
  /// </para>
  /// </summary>
  public static class RenderTreeFlusher
  {
    /// <summary>Flush <paramref name="root"/> into <paramref name="output"/>. Null-safe on both arguments.</summary>
    public static void Flush(RenderRoot root, IRenderOutput output)
    {
      if (root == null || output == null) return;
      FlushChildren(root.Children, output);
    }

    private static void FlushChildren(List<RenderNode> children, IRenderOutput output)
    {
      for (int i = 0; i < children.Count; i++) FlushNode(children[i], output);
    }

    /// <summary>
    /// True when a link label is expressible as one flat string: every node is
    /// a text node or a hook wrap (structural — flushes nothing of its own)
    /// recursively holding only such content. Vacuously true when empty, so an
    /// emptied link flushes flat with a <c>""</c> label.
    /// </summary>
    private static bool IsPlainTextLabel(List<RenderNode> children)
    {
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is RenderTextNode) continue;
        if (children[i] is RenderHookNode hk && IsPlainTextLabel(hk.Children)) continue;
        return false;
      }
      return true;
    }

    private static void FlushNode(RenderNode node, IRenderOutput output)
    {
      switch (node)
      {
        case RenderTextNode t:
          output.Text(t.Content);
          break;
        case RenderHtmlNode h:
          output.Html(h.RawHtml);
          break;
        case RenderLinkNode l:
          // Flush parity: a label that is plain prose — text nodes, possibly
          // inside flush-transparent hook wraps (revision sources, string-
          // occurrence wraps) — keeps the flat Link event, an emptied label
          // included. Content the host must receive as events (styles, armed
          // regions, html, errors) brackets with BeginLink/EndLink instead.
          if (IsPlainTextLabel(l.Children))
            output.Link(l.Text, l.Target);
          else
          {
            output.BeginLink(l.Target);
            FlushChildren(l.Children, output);
            output.EndLink();
          }
          break;
        case RenderErrorNode e:
          output.Error(e.Message);
          break;
        case RenderStyleNode s:
          output.PushStyle(s.Style);
          FlushChildren(s.Children, output);
          output.PopStyle();
          break;
        case RenderInteractiveNode iv:
          output.BeginInteractive(iv.Region);
          FlushChildren(iv.Children, output);
          output.EndInteractive();
          break;
        case RenderHookNode hk:
          FlushChildren(hk.Children, output);
          break;
        case RenderRoot r:
          FlushChildren(r.Children, output);
          break;
      }
    }
  }
}
