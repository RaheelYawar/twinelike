using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Aggregated description of how a single hook should render. Each
  /// <see cref="Changer"/> in the composition pipeline mutates fields here via
  /// its patches; the renderer executes against the finished descriptor.
  ///
  /// <para>
  /// Modelled after the descriptor object in the reference Harlowe JS
  /// implementation, where every changer macro is a function that takes a
  /// descriptor and updates it (styles, source content, transitions,
  /// loopVars, etc.). Keeping that shape here lets future changer kinds —
  /// transitions, source-rewriting <c>(replace:)</c>, hook-name targeting —
  /// drop in by adding fields here and a new patch type, without re-breaking
  /// <see cref="Changer.Apply"/>.
  /// </para>
  /// </summary>
  public class HookDescriptor
  {
    /// <summary>Styling layers in apply order — outermost first.</summary>
    public List<StyleSpec> Styles = new List<StyleSpec>();

    /// <summary>
    /// When set, the renderer iterates the hook contents once per item,
    /// binding <see cref="IterationParamName"/> (and the <c>it</c> slot) to
    /// each item in turn. Null for non-loop changers.
    /// </summary>
    public IterationSpec Iteration;

    /// <summary>
    /// When set, the changer is a revision changer (<c>(replace:)</c> /
    /// <c>(append:)</c> / <c>(prepend:)</c>): instead of rendering its hook
    /// inline, the renderer renders it into a detached subtree and splices that
    /// into the targeted nodes already present in the render tree. Null for
    /// ordinary changers.
    /// </summary>
    public RevisionSpec Revision;

    /// <summary>
    /// When set, the changer is an interaction changer (<c>(click:)</c>,
    /// <c>(mouseover-append:)</c>, etc.): the renderer wraps the targeted
    /// nodes' content in a <see cref="RenderInteractiveNode"/> and registers a
    /// deferred handler that the session fires on
    /// <see cref="StorySession.DispatchEvent"/>. Null for ordinary changers.
    /// </summary>
    public InteractionSpec Interaction;
  }

  /// <summary>
  /// The loop instruction a <c>(for:)</c> changer leaves on a descriptor.
  /// Names the parameter to bind (and its sigil) plus the items to iterate.
  /// </summary>
  public class IterationSpec
  {
    public LambdaValue Lambda;
    public List<HarloweValue> Items;
    public string ParamName;
    public bool ParamIsTemporary;
  }

  /// <summary>How a revision changer splices its rendered source into a target.</summary>
  public enum RevisionMode
  {
    /// <summary>Clear the target's content and insert the source — <c>(replace:)</c>.</summary>
    Replace,
    /// <summary>Insert the source after the target's existing content — <c>(append:)</c>.</summary>
    Append,
    /// <summary>Insert the source before the target's existing content — <c>(prepend:)</c>.</summary>
    Prepend
  }

  /// <summary>
  /// The revision instruction a <c>(replace:)</c> / <c>(append:)</c> /
  /// <c>(prepend:)</c> changer leaves on a descriptor. Exactly one of
  /// <see cref="HookTarget"/> / <see cref="StringTarget"/> is set: a hook-name
  /// query, or a literal substring to find among rendered text. The target is
  /// re-resolved against the live render tree when the changer applies — it is
  /// a query, not a captured node.
  /// </summary>
  public class RevisionSpec
  {
    /// <summary>Hook-name target (<c>(replace: ?cake)</c>). Null when targeting a string.</summary>
    public HookNameValue HookTarget;

    /// <summary>Literal substring target (<c>(replace: "old text")</c>). Null when targeting a hook name.</summary>
    public string StringTarget;

    /// <summary>Whether the source replaces, follows, or precedes the target's content.</summary>
    public RevisionMode Mode;
  }

  /// <summary>
  /// The interaction instruction a click/hover changer leaves on a descriptor.
  /// Combines the targeting query with the kind of event to listen for and the
  /// revision mode that decides what to do with the deferred hook on dispatch
  /// — <c>(click: ?x)</c> ≡ <c>(click-replace: ?x)</c> = Click + Replace;
  /// <c>(mouseover-append: ?x)</c> = MouseOver + Append; etc. The deferred
  /// hook itself is held by the registered <see cref="ClickHandler"/>, not
  /// here — this spec is the static description of what kind of interaction
  /// the changer represents.
  /// </summary>
  public class InteractionSpec
  {
    /// <summary>Hook-name target (the targets of the wrap and the dispatch revision are the same).</summary>
    public HookNameValue HookTarget;

    /// <summary>Which interaction kind fires the dispatch handler.</summary>
    public InteractionKind Kind;

    /// <summary>Whether the dispatched deferred hook replaces, follows, or precedes the target's existing content.</summary>
    public RevisionMode Mode;
  }
}
