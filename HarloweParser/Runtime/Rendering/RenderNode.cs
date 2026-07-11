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
    /// <summary>
    /// Deep copy of this node (and, for containers, its whole subtree). Revision
    /// macros splice a rendered source subtree into every match of a target;
    /// each match gets its own copy so the tree stays a tree, never a DAG.
    /// </summary>
    public abstract RenderNode Clone();
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
    public string Content;

    public override RenderNode Clone() => new RenderTextNode { Content = Content };
  }

  /// <summary>Raw author HTML pass-through. Flushes to <see cref="IRenderOutput.Html"/>.</summary>
  public class RenderHtmlNode : RenderNode
  {
    public string RawHtml;

    public override RenderNode Clone() => new RenderHtmlNode { RawHtml = RawHtml };
  }

  /// <summary>
  /// A passage-to-passage navigation link. A container: the display content is
  /// its <see cref="Children"/> (seeded with one text node when built), so
  /// revision macros can splice into a link's label and string targets can
  /// match inside it — the analogue of reference's <c>&lt;tw-link&gt;</c>
  /// element. Flushes as a flat <see cref="IRenderOutput.Link"/> event while
  /// the content is plain text, else as a <see cref="IRenderOutput.BeginLink"/>
  /// / <see cref="IRenderOutput.EndLink"/> bracket around the content's events.
  /// </summary>
  public class RenderLinkNode : RenderNode, IRenderContainer
  {
    /// <summary>The passage-name reference the link navigates to.</summary>
    public string Target;

    public List<RenderNode> Children { get; } = new List<RenderNode>();

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

    public override RenderNode Clone()
    {
      var copy = new RenderLinkNode { Target = Target };
      RenderNodes.CloneInto(Children, copy.Children);
      return copy;
    }
  }

  /// <summary>An in-prose error message. Flushes to <see cref="IRenderOutput.Error"/>.</summary>
  public class RenderErrorNode : RenderNode
  {
    public string Message;

    public override RenderNode Clone() => new RenderErrorNode { Message = Message };
  }

  /// <summary>
  /// A styling layer. Replaces a <see cref="IRenderOutput.PushStyle"/> /
  /// content / <see cref="IRenderOutput.PopStyle"/> bracket: the flusher emits
  /// <c>PushStyle</c>, flushes <see cref="Children"/>, then <c>PopStyle</c>.
  /// </summary>
  public class RenderStyleNode : RenderNode, IRenderContainer
  {
    /// <summary>
    /// The style layer. <see cref="StyleSpec"/> is public-mutable, so
    /// <see cref="Clone"/> deep-copies it to keep the clone and original
    /// independently observable — without that, mutating one node's style
    /// would change every node ever cloned from it.
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

    public override RenderNode Clone()
    {
      var copy = new RenderStyleNode
      {
        Style = Style?.Clone(),
        SourceEnchantment = SourceEnchantment,
        SourceRegionId = SourceRegionId
      };
      RenderNodes.CloneInto(Children, copy.Children);
      return copy;
    }
  }

  /// <summary>
  /// A hook — the addressable unit. Anonymous hooks still produce one
  /// (<see cref="Name"/> is <c>null</c>) so position/string targeting can find
  /// them; named hooks carry their name. The flusher emits no event of its own
  /// for a hook — it is structural — so flushing a hook is just flushing its
  /// <see cref="Children"/>, matching the pre-refactor renderer where a hook
  /// produced no output of its own.
  /// </summary>
  public class RenderHookNode : RenderNode, IRenderContainer
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
    /// whose dispatch fills this node with the deferred hook's render.
    /// <see cref="RenderNode.Clone"/> copies it, and the dispatch resolves the
    /// anchor by <em>searching the live tree for this tag</em> rather than by
    /// node reference — so an anchor that reaches the tree as a clone (its
    /// macro ran inside a revision-deferred render that was spliced) still
    /// receives its reveal. Deliberately distinct from
    /// <see cref="SourceRegionId"/>: anchors are permanent structure and must
    /// survive <see cref="InteractionPass.StripWraps"/>.
    /// </summary>
    public string RevealRegionId;

    /// <summary>
    /// Set when this node is the <em>label</em> a <c>(link:)</c>-family macro
    /// planted (the created link's text — reference's rendered
    /// <c>&lt;tw-link&gt;</c>): the region id whose interaction arms this
    /// node's children each pass. Permanent structure like
    /// <see cref="RevealRegionId"/> — survives strips, copied by <c>Clone</c>,
    /// resolved by tag; also what makes the label match <c>?link</c>. The
    /// dispatch clears it when a single-use link is consumed, so a spent
    /// <c>(link-reveal:)</c> label reverts to plain prose (reference unwraps
    /// the element).
    /// </summary>
    public string LabelRegionId;

    public List<RenderNode> Children { get; } = new List<RenderNode>();

    public override RenderNode Clone()
    {
      var copy = new RenderHookNode { Name = Name, Anchor = Anchor, SourceEnchantment = SourceEnchantment, SourceRegionId = SourceRegionId, RevealRegionId = RevealRegionId, LabelRegionId = LabelRegionId };
      RenderNodes.CloneInto(Children, copy.Children);
      return copy;
    }
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
    /// <summary>
    /// The bracketing region. Deep-copied on <see cref="Clone"/> for the same
    /// reason <see cref="RenderStyleNode.Style"/> is — the value class is
    /// public-mutable.
    /// </summary>
    public InteractiveRegion Region;
    public List<RenderNode> Children { get; } = new List<RenderNode>();

    public override RenderNode Clone()
    {
      var copy = new RenderInteractiveNode { Region = Region?.Clone() };
      RenderNodes.CloneInto(Children, copy.Children);
      return copy;
    }
  }

  /// <summary>Top of a passage render. The tree a <see cref="RenderTreeBuilder"/> produces.</summary>
  public class RenderRoot : RenderNode, IRenderContainer
  {
    public List<RenderNode> Children { get; } = new List<RenderNode>();

    public override RenderNode Clone()
    {
      var copy = new RenderRoot();
      RenderNodes.CloneInto(Children, copy.Children);
      return copy;
    }
  }

  /// <summary>Shared helpers for working with lists of <see cref="RenderNode"/>.</summary>
  public static class RenderNodes
  {
    /// <summary>Deep-clone every node in <paramref name="source"/> into <paramref name="destination"/>.</summary>
    public static void CloneInto(List<RenderNode> source, List<RenderNode> destination)
    {
      for (int i = 0; i < source.Count; i++) destination.Add(source[i].Clone());
    }

    /// <summary>Return a fresh list holding deep clones of every node in <paramref name="source"/>.</summary>
    public static List<RenderNode> CloneAll(List<RenderNode> source)
    {
      var copy = new List<RenderNode>(source.Count);
      CloneInto(source, copy);
      return copy;
    }

    /// <summary>
    /// Splice deep clones of <paramref name="source"/> into
    /// <paramref name="target"/>'s children per <paramref name="mode"/>:
    /// <see cref="RevisionMode.Replace"/> clears the target first; append /
    /// prepend add at the end / start. The single revision-splice primitive
    /// shared by <c>(replace:)</c>/<c>(append:)</c>/<c>(prepend:)</c> and the
    /// click/hover dispatch path, so both stay in lockstep.
    /// </summary>
    public static void Splice(IRenderContainer target, List<RenderNode> source, RevisionMode mode)
    {
      var copy = CloneAll(source);
      switch (mode)
      {
        case RevisionMode.Replace:
          target.Children.Clear();
          target.Children.AddRange(copy);
          break;
        case RevisionMode.Append:
          target.Children.AddRange(copy);
          break;
        case RevisionMode.Prepend:
          target.Children.InsertRange(0, copy);
          break;
      }
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
    /// where a held node reference would go stale the moment a splice clones
    /// the subtree.
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
  }
}
