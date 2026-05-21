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

  /// <summary>A passage-to-passage navigation link. Flushes to <see cref="IRenderOutput.Link"/>.</summary>
  public class RenderLinkNode : RenderNode
  {
    public string Text;
    public string Target;

    public override RenderNode Clone() => new RenderLinkNode { Text = Text, Target = Target };
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

    public List<RenderNode> Children { get; } = new List<RenderNode>();

    public override RenderNode Clone()
    {
      var copy = new RenderHookNode { Name = Name, Anchor = Anchor };
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
  }
}
