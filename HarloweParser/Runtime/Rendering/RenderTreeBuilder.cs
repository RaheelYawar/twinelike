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
  public class RenderTreeBuilder : ILinkLabelOutput
  {
    private readonly RenderRoot _root;

    // The open containers, innermost last. A list rather than a stack, because
    // a revision of a node still being written swaps that node's entry.
    private readonly List<IRenderContainer> _stack = new List<IRenderContainer>();

    // Each placeholder this builder writes into, with the node it stands in.
    private Dictionary<IRenderContainer, IRenderContainer> _placeholders;

    // Whether the base was already being built when this builder took it on.
    private readonly bool _baseWasBuilding;

    // How many entries at the bottom of the stack are the base and the layers
    // a revision opens on it before its content: its styles, its whitespace
    // layer and its own hook. Content directly in those is directly in the
    // base, as reference inserts it straight into the target.
    private int _layers = 1;

    /// <summary>Set just before a revision's own hook renders here, so the hook it opens counts as a layer.</summary>
    internal bool NextHookIsLayer;

    public RenderTreeBuilder()
    {
      _root = new RenderRoot();
      _stack.Add(_root);
    }

    /// <summary>
    /// A builder that writes straight into an existing node of the tree under
    /// <paramref name="root"/>, so macros running in that content can see and
    /// target the rest of the page. The node counts as being built until
    /// <see cref="FinishBase"/>.
    /// </summary>
    internal RenderTreeBuilder(IRenderContainer into, RenderRoot root)
    {
      _root = root ?? new RenderRoot();
      _stack.Add(into);
      _baseWasBuilding = into is IHideable hideable && hideable.Writer != null;
      if (!_baseWasBuilding) RenderNodes.StartBuilding(into, this);
    }

    /// <summary>
    /// A builder for a revision's placeholder: it writes into
    /// <paramref name="placeholder"/>, which stands in <paramref name="target"/>.
    /// </summary>
    internal static RenderTreeBuilder ForPlaceholder(RenderHookNode placeholder, IRenderContainer target, RenderRoot root)
    {
      var builder = new RenderTreeBuilder(placeholder, root);
      builder._placeholders = new Dictionary<IRenderContainer, IRenderContainer> { [placeholder] = target };
      return builder;
    }

    /// <summary>The tree built so far. Mutated in place by later sub-slices' revision macros.</summary>
    public RenderRoot Root => _root;

    /// <summary>
    /// True when every <see cref="PushStyle"/>/<see cref="BeginHook"/>/<see cref="BeginInteractive"/>
    /// emitted into this builder has been matched by a corresponding pop. The
    /// builder's pop methods deliberately no-op on imbalance to keep
    /// engine-facing flushes resilient, but tests and contributors instrumenting
    /// a new render path can sample this property after a render to catch a
    /// missing pop without crawling the tree.
    /// </summary>
    public bool IsBalanced => _stack.Count == 1;

    /// <summary>
    /// Throws <see cref="System.InvalidOperationException"/> when
    /// <see cref="IsBalanced"/> is false, with the count of unmatched opens.
    /// Opt-in safety check for callers who want a hard failure rather than a
    /// silent shrug — typically tests and CI render-correctness passes.
    /// </summary>
    public void AssertBalanced()
    {
      if (_stack.Count > 1)
        throw new System.InvalidOperationException(
          $"RenderTreeBuilder is unbalanced: {_stack.Count - 1} container(s) still open");
    }

    private IRenderContainer Current => _stack[_stack.Count - 1];

    /// <summary>The container being written into, such as a hook just opened.</summary>
    internal IRenderContainer CurrentContainer => Current;

    /// <summary>
    /// Closes the innermost container. One that stood in for a node being
    /// revised is unwrapped back into it if it's still there, and then the node
    /// itself is finished, so a <c>(hide:)</c> of it waiting meanwhile happens.
    /// </summary>
    private IRenderContainer Pop()
    {
      var node = _stack[_stack.Count - 1];
      _stack.RemoveAt(_stack.Count - 1);
      if (_layers > _stack.Count) _layers = _stack.Count;
      FinishContainer(node);
      return node;
    }

    /// <summary>Counts everything open now as the base's layers, such as a revision's styles.</summary>
    internal void MarkLayers() => _layers = _stack.Count;

    /// <summary>Counts the container just opened as a layer, when it sits directly on the layers.</summary>
    internal void ExtendLayers()
    {
      if (_stack.Count - 1 == _layers) _layers++;
    }

    private void FinishContainer(IRenderContainer node)
    {
      RenderNodes.FinishBuilding(node);
      if (_placeholders == null || !_placeholders.TryGetValue(node, out var stoodIn)) return;
      _placeholders.Remove(node);
      RenderNodes.Unwrap(stoodIn, (RenderNode)node);
      FinishContainer(stoodIn);
    }

    /// <summary>
    /// The end of writing the base this builder started with: it's finished as
    /// if it had been closed, unless another builder was already building it.
    /// </summary>
    internal void FinishBase()
    {
      while (_stack.Count > 1) Pop();
      var node = _stack[0];
      if (_placeholders != null && _placeholders.TryGetValue(node, out var stoodIn))
      {
        // A placeholder base unwraps into its target, which this builder never
        // started, so only the placeholder itself is finished.
        RenderNodes.FinishBuilding(node);
        _placeholders.Remove(node);
        RenderNodes.Unwrap(stoodIn, (RenderNode)node);
        if (!_baseWasBuilding && stoodIn is IHideable moved && ReferenceEquals(moved.Writer, this)) FinishContainer(stoodIn);
        return;
      }
      if (!_baseWasBuilding) RenderNodes.FinishBuilding(node);
    }

    /// <summary>
    /// Before a revision of <paramref name="node"/>, which this builder is
    /// still writing: what's written so far moves into a placeholder that
    /// stays in the node, hooks still open in it included, and the rest of the
    /// node's content goes there too. Reference has already inserted a hook's
    /// whole text when a revision in it runs, so the revision lands after or
    /// before all of it, or clears all of it.
    /// </summary>
    internal void MoveAside(IHideable node)
    {
      int at = _stack.IndexOf(node);
      if (at < 0) return;
      var placeholder = new RenderHookNode();
      placeholder.Children.AddRange(node.Children);
      node.Children.Clear();
      node.Children.Add(placeholder);
      _stack[at] = placeholder;
      RenderNodes.StartBuilding(placeholder, this);
      if (_placeholders == null) _placeholders = new Dictionary<IRenderContainer, IRenderContainer>();
      _placeholders[placeholder] = node;
    }

    /// <summary>
    /// True when what this builder writes next is directly in a placeholder
    /// a revision has taken off its target. Reference inserts a revision's
    /// content as the target's own children, so emptying the target separates
    /// each from its neighbours, and a changer there can no longer find its
    /// hook. The layers the revision opened before its content don't count as
    /// a level, since reference has no element for them. Anything the content
    /// opens does, a hook, a link, a span or inline formatting, because its
    /// own children stay together.
    /// </summary>
    internal bool WritesIntoCutContent()
    {
      if (_placeholders == null) return false;
      var top = _stack[_stack.Count - 1];
      if (_placeholders.TryGetValue(top, out var stoodIn)) return !stoodIn.Children.Contains((RenderNode)top);
      var bottom = _stack[0];
      if (_stack.Count <= _layers && _placeholders.TryGetValue(bottom, out stoodIn)) return !stoodIn.Children.Contains((RenderNode)bottom);
      return false;
    }

    public void Text(string content)
    {
      // Coalesce consecutive prose into a single text node. The tokenizer
      // splits a logical run at every ( [ ] < | $ _ and newline, so a line
      // like "Hello (world)" arrives as several Text() calls; merging them
      // lets string-target revision (TextOccurrenceFinder) match needles that
      // span those boundaries — including across newlines — and trims node
      // count on the render hot path.
      var children = Current.Children;
      if (children.Count > 0 && children[children.Count - 1] is RenderTextNode last)
        last.Append(content);
      else
        children.Add(new RenderTextNode { Content = content });
    }

    public void Html(string rawHtml) => Current.Children.Add(new RenderHtmlNode { RawHtml = rawHtml });

    public void Link(string text, string target)
      => Current.Children.Add(new RenderLinkNode { Text = text, Target = target });

    public void BeginLink(string target)
    {
      var node = new RenderLinkNode { Target = target };
      Current.Children.Add(node);
      RenderNodes.StartBuilding(node, this);
      _stack.Add(node);
    }

    public void EndLink()
    {
      if (_stack.Count > 1) Pop();
    }

    public void Error(string message) => Current.Children.Add(new RenderErrorNode { Message = message });

    public void PushStyle(StyleSpec style)
    {
      // Clone the spec when stamping it onto the tree so a downstream pass
      // that mutates one RenderStyleNode.Style (enchantment overrides,
      // future patches) can't bleed back into the changer's descriptor or
      // every other sibling node sharing the same reference. Centralized
      // here so every render path is protected — RunStyles, RunRevision,
      // and RunInteraction all share this invariant for free.
      var node = new RenderStyleNode { Style = style?.Clone() };
      Current.Children.Add(node);
      _stack.Add(node);
    }

    public void PopStyle()
    {
      // Defensive: never pop past the root. Well-formed render paths always
      // balance push/pop, so this guard should never fire.
      if (_stack.Count > 1) Pop();
    }

    /// <summary>
    /// Open a hook scope. Subsequent nodes accumulate as the hook's children
    /// until the matching <see cref="EndHook"/>. Called by
    /// <see cref="BodyRenderer.Visit(HookNode)"/>; not part of
    /// <see cref="IRenderOutput"/> because hook structure is builder-only.
    /// </summary>
    public void BeginHook(string name, HookAnchor anchor) => BeginHook(name, anchor, null);

    /// <summary>Opens a hook that stays hidden until <paramref name="hidden"/> is revealed. Null for an ordinary hook.</summary>
    internal void BeginHook(string name, HookAnchor anchor, HiddenHookContent hidden)
    {
      var node = new RenderHookNode { Name = name, Anchor = anchor, Hidden = hidden };
      Current.Children.Add(node);
      RenderNodes.StartBuilding(node, this);
      _stack.Add(node);
      if (NextHookIsLayer)
      {
        NextHookIsLayer = false;
        ExtendLayers();
      }
    }

    /// <summary>Close the most recently opened hook scope.</summary>
    public void EndHook()
    {
      if (_stack.Count > 1) Pop();
    }

    /// <summary>
    /// Open an anonymous hook carrying a <see cref="WhitespaceMode"/>, for a
    /// <c>{…}</c> span or for printed content inside one. Close it with
    /// <see cref="EndWhitespace"/>.
    /// </summary>
    public void BeginWhitespace(WhitespaceMode mode)
    {
      var node = new RenderHookNode { Whitespace = mode };
      Current.Children.Add(node);
      _stack.Add(node);
    }

    /// <summary>
    /// Close a <see cref="BeginWhitespace"/> hook, removing it when nothing was
    /// written into it, so a <c>(set:)</c> inside <c>{…}</c> leaves no trace.
    /// </summary>
    public void EndWhitespace()
    {
      if (_stack.Count <= 1) return;
      var node = Pop();
      if (node.Children.Count > 0 || !(node is RenderNode empty)) return;
      var siblings = Current.Children;
      int at = siblings.LastIndexOf(empty);
      if (at >= 0) siblings.RemoveAt(at);
    }

    /// <summary>
    /// Plant an empty anonymous hook node at the current position, tagged with
    /// <paramref name="revealRegionId"/>. The reveal anchor for a plain
    /// <c>(click:)</c>-family macro: the analogue of reference Harlowe's hidden
    /// attached-hook element (<c>desc.enabled = false</c> leaves an empty
    /// <c>&lt;tw-hook&gt;</c> in the DOM), which
    /// <see cref="StorySession.DispatchEvent"/> fills with the deferred hook's
    /// render when the region fires, found in the live tree by tag.
    /// Anonymous, so no <c>?name</c> query can match it while empty.
    /// </summary>
    public RenderHookNode PlantAnchor(string revealRegionId)
    {
      var node = new RenderHookNode { RevealRegionId = revealRegionId };
      Current.Children.Add(node);
      return node;
    }

    /// <summary>
    /// Plant a <c>(link:)</c>-family label at the current position: an
    /// anonymous hook tagged <see cref="RenderHookNode.LabelRegionId"/> holding
    /// the label text, which <see cref="InteractionPass"/> arms each pass —
    /// the analogue of reference's created <c>&lt;tw-link&gt;</c>. When
    /// <paramref name="revealReplacesLabel"/> (<c>(link:)</c>/<c>(link-replace:)</c>),
    /// the label nests inside a reveal anchor so the dispatch's Replace fill
    /// removes the link along with showing the content; otherwise the caller
    /// plants any sibling anchor itself (<c>(link-undo:)</c> plants none — it
    /// never reveals).
    /// </summary>
    public void PlantLinkLabel(string regionId, string label, bool revealReplacesLabel)
    {
      var labelHook = new RenderHookNode { LabelRegionId = regionId };
      labelHook.Children.Add(new RenderTextNode { Content = label });
      if (revealReplacesLabel)
      {
        var anchor = new RenderHookNode { RevealRegionId = regionId };
        anchor.Children.Add(labelHook);
        Current.Children.Add(anchor);
      }
      else
      {
        Current.Children.Add(labelHook);
      }
    }

    public void BeginInteractive(InteractiveRegion region)
    {
      var node = new RenderInteractiveNode { Region = region };
      Current.Children.Add(node);
      _stack.Add(node);
    }

    public void EndInteractive()
    {
      if (_stack.Count > 1) Pop();
    }
  }
}
