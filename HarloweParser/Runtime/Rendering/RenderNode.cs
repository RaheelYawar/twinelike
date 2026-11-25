using System;
using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe.Runtime.Rendering
{
  /// <summary>
  /// Base type for a node in the internal render tree. <see cref="BodyRenderer"/>
  /// builds a tree of these (via <see cref="RenderTreeBuilder"/>) instead of
  /// pushing events straight at an <see cref="IRenderOutput"/>; the finished
  /// tree is an addressable, mutable representation of rendered passage content.
  /// <see cref="RenderTreeFlusher"/> walks it and emits the same event stream
  /// the renderer used to emit directly.
  ///
  /// <para>
  /// The tree exists so revision/enchantment macros (later sub-slices) can
  /// target content that is <em>already rendered</em> — a pure linear event
  /// push has nothing to point at. In this foundation slice nothing mutates the
  /// tree: it is flushed verbatim and the emitted stream is byte-identical to
  /// the pre-refactor renderer.
  /// </para>
  ///
  /// <para>
  /// Mirrors the reference Harlowe runtime's DOM-of-<c>&lt;tw-hook&gt;</c>
  /// elements; <see cref="RenderHookNode"/> is the analogue of a hook element,
  /// the addressable unit a <c>?name</c> reference resolves against.
  /// </para>
  /// </summary>
  public abstract class RenderNode
  {
  }

  /// <summary>A render node that holds ordered child nodes.</summary>
  public interface IRenderContainer
  {
    /// <summary>Child nodes in document order.</summary>
    List<RenderNode> Children { get; }
  }

  /// <summary>Plain prose text. Flushes to <see cref="IRenderOutput.Text"/>.</summary>
  public class RenderTextNode : RenderNode
  {
    private string _content;
    private System.Text.StringBuilder _appended;

    /// <summary>The node's text. Appends are buffered until the next read.</summary>
    public string Content
    {
      get
      {
        if (_appended != null)
        {
          _content = _appended.ToString();
          _appended = null;
        }
        return _content;
      }
      set
      {
        _content = value;
        _appended = null;
      }
    }

    /// <summary>
    /// Adds to the end of the text. The builder coalesces a whole prose run
    /// into one node through this, and repeated string concatenation there
    /// would be quadratic in the length of the passage.
    /// </summary>
    internal void Append(string text)
    {
      if (_appended == null) _appended = new System.Text.StringBuilder(_content);
      _appended.Append(text);
    }
  }

  /// <summary>Raw author HTML pass-through. Flushes to <see cref="IRenderOutput.Html"/>.</summary>
  public class RenderHtmlNode : RenderNode
  {
    public string RawHtml;
  }

  /// <summary>
  /// A passage-to-passage navigation link. A container: the display content is
  /// its <see cref="Children"/> (seeded with one text node when built), so
  /// revision macros can splice into a link's label and string targets can
  /// match inside it — the analogue of reference's <c>&lt;tw-link&gt;</c>
  /// element. Flushes as a flat <see cref="IRenderOutput.Link"/> event while
  /// the content is plain text, else as an <see cref="ILinkLabelOutput.BeginLink"/>
  /// / <see cref="ILinkLabelOutput.EndLink"/> bracket around the content's events.
  /// </summary>
  public class RenderLinkNode : RenderNode, IRenderContainer, IHideable
  {
    /// <summary>The passage-name reference the link navigates to.</summary>
    public string Target;

    public List<RenderNode> Children { get; } = new List<RenderNode>();

    /// <summary>Set while <c>(hide: ?link)</c> has this link's label hidden, which leaves an empty link, as in reference. See <see cref="RenderHookNode.Hidden"/>.</summary>
    internal HiddenHookContent Hidden;

    private bool _pendingHide;
    private RenderTreeBuilder _writer;

    HiddenHookContent IHideable.HiddenContent { get => Hidden; set => Hidden = value; }
    bool IHideable.PendingHide { get => _pendingHide; set => _pendingHide = value; }
    RenderTreeBuilder IHideable.Writer { get => _writer; set => _writer = value; }

    /// <summary>
    /// Display-text convenience over <see cref="Children"/>: get flattens every
    /// descendant text node in document order; set replaces the content with a
    /// single text node. Structural edits go through <see cref="Children"/>.
    /// </summary>
    public string Text
    {
      get
      {
        var sb = new System.Text.StringBuilder();
        AppendText(this, sb);
        return sb.ToString();
      }
      set
      {
        Children.Clear();
        Children.Add(new RenderTextNode { Content = value });
      }
    }

    private static void AppendText(IRenderContainer container, System.Text.StringBuilder sb)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is RenderTextNode t) sb.Append(t.Content);
        else if (children[i] is IRenderContainer c) AppendText(c, sb);
      }
    }
  }

  /// <summary>
  /// A node <c>(hide:)</c> can take content out of: a hook, a link, or the
  /// passage. Reference hides any element its hook names select the same way.
  /// </summary>
  internal interface IHideable : IRenderContainer
  {
    /// <summary>What <c>(show:)</c> will put back or render. Null while it's showing.</summary>
    HiddenHookContent HiddenContent { get; set; }

    /// <summary>
    /// Set when <c>(hide:)</c> named this node while it was still being built.
    /// Reference renders a hook's whole content before running its macros, so a
    /// hook hidden from inside hides everything in it, including what comes
    /// after the <c>(hide:)</c>; here it's hidden when it's finished.
    /// </summary>
    bool PendingHide { get; set; }

    /// <summary>
    /// The builder still writing this node's content, whichever builder runs
    /// the <c>(hide:)</c>, or null once it's finished. A revision of the node
    /// asks this builder to move what it has written aside first.
    /// </summary>
    RenderTreeBuilder Writer { get; set; }
  }

  /// <summary>An in-prose error message. Flushes to <see cref="IRenderOutput.Error"/>.</summary>
  public class RenderErrorNode : RenderNode
  {
    public string Message;
  }

  /// <summary>
  /// A styling layer. Replaces a <see cref="IRenderOutput.PushStyle"/> /
  /// content / <see cref="IRenderOutput.PopStyle"/> bracket: the flusher emits
  /// <c>PushStyle</c>, flushes <see cref="Children"/>, then <c>PopStyle</c>.
  /// </summary>
  public class RenderStyleNode : RenderNode, IRenderContainer
  {
    /// <summary>
    /// The style layer. <see cref="StyleSpec"/> is public-mutable, so the
    /// builder stamps a copy of the changer's, and no other node shares it.
    /// </summary>
    public StyleSpec Style;

    /// <summary>
    /// The enchantment that produced this node, or <c>null</c> for ordinary
    /// style wraps (from <c>(text-style:)</c>, <c>(change:)</c>, etc.).
    /// <see cref="EnchantmentPass.Update"/> uses this tag to disenchant —
    /// unwrap nodes from a previous pass — before re-applying, so a dispatch
    /// re-render doesn't double-wrap already-enchanted content.
    /// </summary>
    public Enchantment SourceEnchantment;

    /// <summary>
    /// The interactive region this style wrap belongs to, or <c>null</c> for
    /// style wraps that aren't tied to a region. Set when a composed
    /// interaction changer (<c>(click-append: ?m) + (text-style: "bold")</c>)
    /// folds its style layers around the <see cref="RenderInteractiveNode"/>;
    /// <see cref="StorySession.DispatchEvent"/>'s unwrap pass strips matching
    /// wraps alongside the interactive node so the original target returns to
    /// its pre-interaction styling once the region fires.
    /// </summary>
    public string SourceRegionId;

    public List<RenderNode> Children { get; } = new List<RenderNode>();
  }

  /// <summary>How a <see cref="RenderHookNode"/> takes part in collapsing whitespace.</summary>
  public enum WhitespaceMode
  {
    /// <summary>Follows whatever encloses it. The default for ordinary hooks.</summary>
    Inherit,

    /// <summary>A collapsing span: reference's <c>&lt;tw-collapsed&gt;</c>.</summary>
    Collapse,

    /// <summary>Kept as written inside a collapsing span: reference's <c>&lt;tw-expression&gt;</c> and <c>[collapsing=false]</c>.</summary>
    Preserve,
  }

  /// <summary>
  /// A hook — the addressable unit. Anonymous hooks still produce one
  /// (<see cref="Name"/> is <c>null</c>) so position/string targeting can find
  /// them; named hooks carry their name. The flusher emits no event of its own
  /// for a hook — it is structural — so flushing a hook is just flushing its
  /// <see cref="Children"/>, matching the pre-refactor renderer where a hook
  /// produced no output of its own.
  /// </summary>
  public class RenderHookNode : RenderNode, IRenderContainer, IHideable
  {
    /// <summary>The hook's name without delimiters; <c>null</c> for anonymous hooks.</summary>
    public string Name;

    /// <summary>Where the name was anchored in source.</summary>
    public HookAnchor Anchor;

    /// <summary>
    /// The enchantment whose string target created this wrap
    /// (<see cref="TextOccurrenceFinder"/> occurrence for a persistent
    /// <c>(enchant: "text", …)</c>), or <c>null</c> for authored hooks and
    /// one-shot wraps (revision macros, <c>(change:)</c>).
    /// <see cref="EnchantmentPass.Disenchant"/> unwraps tagged wraps back to
    /// plain prose before each pass re-matches — the same discipline as
    /// <see cref="RenderStyleNode.SourceEnchantment"/>.
    /// </summary>
    public Enchantment SourceEnchantment;

    /// <summary>
    /// The interactive region whose string target created this wrap
    /// (<see cref="TextOccurrenceFinder"/> occurrence for a
    /// <c>(click: "text", …)</c>-family interaction), or <c>null</c> for
    /// authored hooks and enchantment wraps. <see cref="InteractionPass.StripWraps"/>
    /// unwraps tagged wraps back to plain prose before each pass re-matches —
    /// the interaction mirror of <see cref="SourceEnchantment"/>. Also how the
    /// dispatch finds a combo's string-occurrence splice targets.
    /// </summary>
    public string SourceRegionId;

    /// <summary>
    /// Set when this node is the <em>reveal anchor</em> a plain
    /// <c>(click:)</c>-family macro planted at its own position: the region id
    /// whose dispatch fills this node with the deferred hook's render. The
    /// dispatch finds the anchor by <em>searching the live tree for this tag</em>,
    /// so one that has left the page isn't filled there. Deliberately distinct
    /// from <see cref="SourceRegionId"/>: anchors are permanent structure and
    /// must survive <see cref="InteractionPass.StripWraps"/>.
    /// </summary>
    public string RevealRegionId;

    /// <summary>
    /// Set when this node is the <em>label</em> a <c>(link:)</c>-family macro
    /// planted (the created link's text — reference's rendered
    /// <c>&lt;tw-link&gt;</c>): the region id whose interaction arms this
    /// node's children each pass. Permanent structure like
    /// <see cref="RevealRegionId"/> — survives strips, resolved by tag; also what makes the label match <c>?link</c>. The
    /// dispatch clears it when a single-use link is consumed, so a spent
    /// <c>(link-reveal:)</c> label reverts to plain prose (reference unwraps
    /// the element).
    /// </summary>
    public string LabelRegionId;

    /// <summary>
    /// How <see cref="WhitespaceCollapser"/> treats this hook's content. A
    /// <c>{…}</c> span is an anonymous hook marked
    /// <see cref="WhitespaceMode.Collapse"/>; printed values and displayed
    /// passages inside one are marked <see cref="WhitespaceMode.Preserve"/>.
    /// </summary>
    public WhitespaceMode Whitespace;

    /// <summary>
    /// Set while this hook is hidden: what <c>(show:)</c> will render into it.
    /// Reference's <c>hidden</c> and <c>originalSource</c> element data.
    /// Revision macros can still put content in a hidden hook.
    /// </summary>
    internal HiddenHookContent Hidden;

    private bool _pendingHide;
    private RenderTreeBuilder _writer;

    HiddenHookContent IHideable.HiddenContent { get => Hidden; set => Hidden = value; }
    bool IHideable.PendingHide { get => _pendingHide; set => _pendingHide = value; }
    RenderTreeBuilder IHideable.Writer { get => _writer; set => _writer = value; }

    public List<RenderNode> Children { get; } = new List<RenderNode>();
  }

  /// <summary>What a hidden hook shows when it's revealed.</summary>
  internal sealed class HiddenHookContent
  {
    /// <summary>Renders the hook into the output. The flag says whether the hook sits inside a <c>{…}</c> span.</summary>
    public Action<IRenderOutput, bool> Render;

    /// <summary>The size of the hook's content, charged to the nested-render budget each time it's revealed.</summary>
    public int Cost;

    /// <summary>
    /// For a hook <c>(hide:)</c> hid: its content as it was, put back as it is
    /// rather than rendered again. Reference keeps the detached elements in the
    /// same <c>hidden</c> data. Null for a hook hidden before it rendered.
    /// </summary>
    public List<RenderNode> Detached;
  }

  /// <summary>
  /// An interactive region — the wrap a click/hover changer places around the
  /// targeted hook's content. The flusher emits a
  /// <see cref="IRenderOutput.BeginInteractive"/> / <see cref="IRenderOutput.EndInteractive"/>
  /// bracket around the children. <see cref="StorySession.DispatchEvent"/>
  /// finds these nodes by region id and consumes them at event time, replacing
  /// each with its children before splicing the deferred prose into the
  /// underlying target.
  /// </summary>
  public class RenderInteractiveNode : RenderNode, IRenderContainer
  {
    /// <summary>The bracketing region.</summary>
    public InteractiveRegion Region;
    public List<RenderNode> Children { get; } = new List<RenderNode>();
  }

  /// <summary>Top of a passage render. The tree a <see cref="RenderTreeBuilder"/> produces.</summary>
  public class RenderRoot : RenderNode, IRenderContainer, IHideable
  {
    public List<RenderNode> Children { get; } = new List<RenderNode>();

    /// <summary>Set while <c>(hide: ?passage)</c> has the passage hidden. See <see cref="RenderHookNode.Hidden"/>.</summary>
    internal HiddenHookContent Hidden;

    private bool _pendingHide;
    private RenderTreeBuilder _writer;

    HiddenHookContent IHideable.HiddenContent { get => Hidden; set => Hidden = value; }
    bool IHideable.PendingHide { get => _pendingHide; set => _pendingHide = value; }
    RenderTreeBuilder IHideable.Writer { get => _writer; set => _writer = value; }
  }

  /// <summary>Shared helpers for working with lists of <see cref="RenderNode"/>.</summary>
  public static class RenderNodes
  {
    /// <summary>
    /// What <c>(hide:)</c> does to a hook or the passage: its content comes out
    /// of the page, kept for <c>(show:)</c> to put back, and the empty node
    /// stays, so revisions can still fill it. A node that's already hidden is
    /// left alone, as in reference.
    /// </summary>
    internal static void Hide(IHideable node)
    {
      if (node == null || node.HiddenContent != null) return;
      node.HiddenContent = new HiddenHookContent { Detached = new List<RenderNode>(node.Children) };
      node.Children.Clear();
    }

    /// <summary>Marks <paramref name="node"/> as being built by <paramref name="writer"/>, which delays a <c>(hide:)</c> of it until <see cref="FinishBuilding"/>.</summary>
    internal static void StartBuilding(IRenderContainer node, RenderTreeBuilder writer)
    {
      if (node is IHideable hideable) hideable.Writer = writer;
    }

    /// <summary>The end of <see cref="StartBuilding"/>: a <c>(hide:)</c> that named the node meanwhile takes effect now.</summary>
    internal static void FinishBuilding(IRenderContainer node)
    {
      if (!(node is IHideable hideable)) return;
      hideable.Writer = null;
      if (!hideable.PendingHide) return;
      hideable.PendingHide = false;
      Hide(hideable);
    }

    /// <summary>
    /// The indexes of <paramref name="nodes"/> in the order they sit under
    /// <paramref name="root"/>, followed by those that aren't under it, in
    /// their own order.
    /// </summary>
    internal static List<int> PageOrder(IRenderContainer root, List<RenderNode> nodes)
    {
      var wanted = new Dictionary<RenderNode, int>(nodes.Count);
      for (int i = 0; i < nodes.Count; i++) wanted[nodes[i]] = i;
      var order = new List<int>(nodes.Count);
      var seen = new bool[nodes.Count];
      if (root != null) CollectOrder(root, wanted, order, seen);
      for (int i = 0; i < nodes.Count; i++)
        if (!seen[i]) order.Add(i);
      return order;
    }

    private static void CollectOrder(IRenderContainer container, Dictionary<RenderNode, int> wanted, List<int> order, bool[] seen)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count && order.Count < seen.Length; i++)
      {
        if (wanted.TryGetValue(children[i], out int index) && !seen[index])
        {
          seen[index] = true;
          order.Add(index);
        }
        if (children[i] is IRenderContainer inner) CollectOrder(inner, wanted, order, seen);
      }
    }

    /// <summary>Replaces <paramref name="node"/> with its children when it's directly in <paramref name="container"/>, and does nothing otherwise.</summary>
    internal static void Unwrap(IRenderContainer container, RenderNode node)
    {
      var children = container.Children;
      int at = children.IndexOf(node);
      if (at < 0) return;
      children.RemoveAt(at);
      if (node is IRenderContainer inner) children.InsertRange(at, inner.Children);
    }

    /// <summary>The work one search of <paramref name="node"/>'s subtree costs, as charged by <see cref="MacroContext.TrySpendOnTargetWalk"/>.</summary>
    internal static long WalkCost(RenderNode node)
    {
      int height = 0;
      return WalkCost(node, 0, ref height);
    }

    /// <summary>As <see cref="WalkCost(RenderNode)"/>, also raising <paramref name="height"/> to the deepest level below <paramref name="node"/>.</summary>
    internal static long WalkCost(RenderNode node, int depth, ref int height)
    {
      if (depth > height) height = depth;
      long cost = MacroContext.TargetWalkNodeCost;
      if (node is RenderTextNode text) cost += text.Content?.Length ?? 0;
      if (node is IRenderContainer container)
      {
        var children = container.Children;
        for (int i = 0; i < children.Count; i++) cost += WalkCost(children[i], depth + 1, ref height);
      }
      return cost;
    }

    /// <summary>
    /// The size of <paramref name="nodes"/>: a unit per node plus one per
    /// character it holds. A revision renders its content once per target, so
    /// a recursive story can grow the tree far faster than its source, and
    /// this is what <see cref="MacroContext.TrySpendOnSplice"/> charges for
    /// each rendering.
    /// </summary>
    internal static long SpliceCost(List<RenderNode> nodes)
    {
      long cost = nodes.Count;
      for (int i = 0; i < nodes.Count; i++)
      {
        switch (nodes[i])
        {
          case RenderTextNode text: cost += text.Content?.Length ?? 0; break;
          case RenderErrorNode error: cost += error.Message?.Length ?? 0; break;
          case RenderHtmlNode html: cost += html.RawHtml?.Length ?? 0; break;
          case RenderLinkNode link: cost += link.Target?.Length ?? 0; break;
        }
        if (nodes[i] is IRenderContainer container) cost += SpliceCost(container.Children);
      }
      return cost;
    }

    /// <summary>
    /// Depth-first sweep that replaces every node where <paramref name="unwrap"/>
    /// returns true with its children, hoisting them into the parent. Recurses
    /// before rebuilding each level so descendants are unwrapped first (so a
    /// hoisted child is never re-scanned at this level). The shared skeleton
    /// behind the disenchant / interaction-strip passes — each supplies only
    /// the match predicate. A matched node that isn't a container is left in
    /// place (nothing to hoist).
    /// </summary>
    public static void UnwrapWhere(IRenderContainer container, Func<RenderNode, bool> unwrap)
    {
      if (container == null) return;
      var children = container.Children;

      for (int i = 0; i < children.Count; i++)
        if (children[i] is IRenderContainer c) UnwrapWhere(c, unwrap);

      var rebuilt = new List<RenderNode>(children.Count);
      for (int i = 0; i < children.Count; i++)
      {
        if (unwrap(children[i]) && children[i] is IRenderContainer wrap)
          rebuilt.AddRange(wrap.Children);
        else
          rebuilt.Add(children[i]);
      }
      children.Clear();
      children.AddRange(rebuilt);
    }

    /// <summary>
    /// Depth-first sweep collecting every descendant of
    /// <paramref name="container"/> (document order) for which
    /// <paramref name="match"/> returns true. How the dispatch resolves nodes
    /// by tag — reveal anchors by <see cref="RenderHookNode.RevealRegionId"/>,
    /// string-occurrence wraps by <see cref="RenderHookNode.SourceRegionId"/> —
    /// so only what's still on the page is found.
    /// </summary>
    public static void CollectWhere(IRenderContainer container, Func<RenderNode, bool> match, List<RenderNode> results)
    {
      if (container == null || results == null) return;
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (match(children[i])) results.Add(children[i]);
        if (children[i] is IRenderContainer c) CollectWhere(c, match, results);
      }
    }

    /// <summary>
    /// Find <paramref name="target"/> among <paramref name="root"/>'s descendants
    /// (by reference) and replace it in place with <paramref name="replacement"/>;
    /// returns true if found. Lets the enchant/interaction passes wrap a
    /// <see cref="RenderLinkNode"/> resolved by <c>?link</c> <em>node-and-all</em>
    /// in their style/interactive layer (the wrap goes around the link, as
    /// reference wraps <c>&lt;tw-enchantment&gt;</c> around <c>&lt;tw-link&gt;</c>),
    /// where the rest of the machinery wraps a container's own children. The
    /// inverse unwrap is just <see cref="UnwrapWhere"/>, which hoists the node
    /// back out by tag.
    /// </summary>
    public static bool ReplaceChild(IRenderContainer root, RenderNode target, RenderNode replacement)
    {
      if (root == null || target == null) return false;
      var children = root.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (ReferenceEquals(children[i], target)) { children[i] = replacement; return true; }
        if (children[i] is IRenderContainer c && ReplaceChild(c, target, replacement)) return true;
      }
      return false;
    }

    /// <summary>
    /// True when whitespace in <paramref name="target"/> collapses: it sits in a
    /// <c>{…}</c> span, with no printed content between the span and it.
    /// <paramref name="owner"/> is the root it was found under, or null when
    /// an earlier reveal has already taken it off the page.
    /// </summary>
    internal static bool IsInsideCollapse(List<RenderNode> roots, RenderNode target, out RenderRoot owner)
    {
      for (int i = 0; i < roots.Count; i++)
      {
        if (roots[i] is RenderRoot root && FindInCollapse(root, target, false, out bool inside))
        {
          owner = root;
          return inside;
        }
      }
      owner = null;
      return false;
    }

    private static bool FindInCollapse(IRenderContainer container, RenderNode target, bool inCollapse, out bool inside)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (ReferenceEquals(children[i], target))
        {
          inside = inCollapse;
          return true;
        }
        if (children[i] is IRenderContainer nested)
        {
          bool collapses = inCollapse;
          if (children[i] is RenderHookNode hook && hook.Whitespace != WhitespaceMode.Inherit)
            collapses = hook.Whitespace == WhitespaceMode.Collapse;
          if (FindInCollapse(nested, target, collapses, out inside)) return true;
        }
      }
      inside = false;
      return false;
    }

    /// <summary>Removes <paramref name="target"/> from wherever it sits under <paramref name="root"/>. Returns true if found.</summary>
    internal static bool RemoveDescendant(IRenderContainer root, RenderNode target)
    {
      var children = root.Children;
      for (int i = 0; i < children.Count; i++)
      {
        if (ReferenceEquals(children[i], target))
        {
          children.RemoveAt(i);
          return true;
        }
        if (children[i] is IRenderContainer c && RemoveDescendant(c, target)) return true;
      }
      return false;
    }
  }

  /// <summary>
  /// Where every node under a root sits, recorded in one walk, so wrapping many
  /// <c>?link</c> matches doesn't search the page once per link.
  /// </summary>
  internal sealed class NodePositions
  {
    private readonly IRenderContainer _root;
    private readonly Dictionary<RenderNode, KeyValuePair<IRenderContainer, int>> _positions =
      new Dictionary<RenderNode, KeyValuePair<IRenderContainer, int>>();

    public NodePositions(IRenderContainer root)
    {
      _root = root;
      Record(root);
    }

    private void Record(IRenderContainer container)
    {
      var children = container.Children;
      for (int i = 0; i < children.Count; i++)
      {
        _positions[children[i]] = new KeyValuePair<IRenderContainer, int>(container, i);
        if (children[i] is IRenderContainer c) Record(c);
      }
    }

    /// <summary>Replaces <paramref name="target"/> in place. A node moved since the walk, by a wrap around its parent's content, is searched for.</summary>
    public bool Replace(RenderNode target, RenderNode replacement)
    {
      if (_positions.TryGetValue(target, out var at)
          && at.Value < at.Key.Children.Count
          && ReferenceEquals(at.Key.Children[at.Value], target))
      {
        at.Key.Children[at.Value] = replacement;
        return true;
      }
      return RenderNodes.ReplaceChild(_root, target, replacement);
    }
  }
}
