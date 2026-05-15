using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// An <see cref="IRenderOutput"/> that builds a <see cref="RenderRoot"/> tree
  /// instead of forwarding events to a host engine. Sits between
  /// <see cref="BodyRenderer"/> and the real output: the renderer pushes the
  /// same <c>Text</c>/<c>Link</c>/<c>PushStyle</c>/… calls it always has, and
  /// each lands as a node on the tree. <see cref="RenderTreeFlusher"/> later
  /// walks the finished tree and replays the identical event stream at the real
  /// <see cref="IRenderOutput"/>.
  ///
  /// <para>
  /// Because it <em>is</em> an <see cref="IRenderOutput"/>, <see cref="Changer.Apply"/>
  /// retargets onto the tree for free — a <c>PushStyle</c> from a changer
  /// becomes a <see cref="RenderStyleNode"/> with no change to the changer code.
  /// The two channels <see cref="IRenderOutput"/> can't express — hook structure
  /// — are added as <see cref="BeginHook"/> / <see cref="EndHook"/>, which
  /// <see cref="BodyRenderer"/> calls when (and only when) its output is a
  /// builder. An output that is a plain buffer simply renders hooks flat, which
  /// is the pre-refactor behaviour.
  /// </para>
  /// </summary>
  public class RenderTreeBuilder : IRenderOutput
  {
    private readonly RenderRoot _root = new RenderRoot();
    private readonly Stack<IRenderContainer> _stack = new Stack<IRenderContainer>();

    public RenderTreeBuilder()
    {
      _stack.Push(_root);
    }

    /// <summary>The tree built so far. Mutated in place by later sub-slices' revision macros.</summary>
    public RenderRoot Root => _root;

    private IRenderContainer Current => _stack.Peek();

    public void Text(string content) => Current.Children.Add(new RenderTextNode { Content = content });

    public void Html(string rawHtml) => Current.Children.Add(new RenderHtmlNode { RawHtml = rawHtml });

    public void Link(string text, string target)
      => Current.Children.Add(new RenderLinkNode { Text = text, Target = target });

    public void Error(string message) => Current.Children.Add(new RenderErrorNode { Message = message });

    public void PushStyle(StyleSpec style)
    {
      var node = new RenderStyleNode { Style = style };
      Current.Children.Add(node);
      _stack.Push(node);
    }

    public void PopStyle()
    {
      // Defensive: never pop past the root. Well-formed render paths always
      // balance push/pop, so this guard should never fire.
      if (_stack.Count > 1) _stack.Pop();
    }

    /// <summary>
    /// Open a hook scope. Subsequent nodes accumulate as the hook's children
    /// until the matching <see cref="EndHook"/>. Called by
    /// <see cref="BodyRenderer.Visit(HookNode)"/>; not part of
    /// <see cref="IRenderOutput"/> because hook structure is builder-only.
    /// </summary>
    public void BeginHook(string name, HookAnchor anchor)
    {
      var node = new RenderHookNode { Name = name, Anchor = anchor };
      Current.Children.Add(node);
      _stack.Push(node);
    }

    /// <summary>Close the most recently opened hook scope.</summary>
    public void EndHook()
    {
      if (_stack.Count > 1) _stack.Pop();
    }
  }
}
