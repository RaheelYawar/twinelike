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
    /// <summary>
    /// Whether the hook renders at all. Conditional changers AND their decision
    /// into this (reference's <c>d.enabled &amp;&amp;= expr</c> in
    /// <c>ts/macrolib/stylechangers.ts</c>); a false value suppresses the whole
    /// application — styles, iteration, revision, and interaction alike.
    /// </summary>
    public bool Enabled = true;

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
  /// Combines the targeting query (hook name or literal string) with the kind
  /// of event to listen for and what happens to the deferred hook on dispatch:
  /// a plain macro (<c>(click: ?x)</c>, <see cref="Mode"/> null) <em>reveals</em>
  /// the attached hook at the macro's own position, while a combo
  /// (<c>(click-replace: ?x)</c>, <c>(mouseover-append: ?x)</c>, …) splices it
  /// into the target — reference's <c>enchantDesc.rerender</c> distinction in
  /// <c>ts/macrolib/enchantments.ts</c>. The deferred hook itself is held by
  /// the recorded <see cref="Interaction"/>, not here — this spec is the
  /// static description of what kind of interaction the changer represents.
  /// </summary>
  public class InteractionSpec
  {
    /// <summary>Hook-name target. Null when <see cref="StringTarget"/> is set.</summary>
    public HookNameValue HookTarget;

    /// <summary>Literal prose to match (<c>(click: "gold")</c>); each occurrence is wrapped as an armed region per pass. Null when <see cref="HookTarget"/> is set.</summary>
    public string StringTarget;

    /// <summary>Which interaction kind fires the dispatch handler.</summary>
    public InteractionKind Kind;

    /// <summary>
    /// Combo splice mode, or <c>null</c> for the plain macros, whose deferred
    /// hook reveals at the macro's own position instead of rewriting the target.
    /// </summary>
    public RevisionMode? Mode;

    /// <summary>
    /// False for <c>(click-rerun:)</c>: the interaction survives dispatch and
    /// each activation re-renders the deferred hook in place of the previous
    /// run's content (reference's <c>once: false</c> + <c>append: 'replace'</c>).
    /// </summary>
    public bool Once = true;

    /// <summary>
    /// Optional second-argument changer styling the <em>armed</em> region while
    /// it waits (<c>(click: ?hat, (text-style: "bold"))</c>). Must be
    /// enchantable. Null when absent or when <see cref="ArmLambda"/> is set.
    /// </summary>
    public Changer ArmChanger;

    /// <summary>
    /// Optional second-argument <c>via</c> lambda producing the armed-region
    /// changer per match, with <c>pos</c> bound 1-based — the same machinery
    /// as <c>(enchant:)</c>'s lambda form. Null when absent or when
    /// <see cref="ArmChanger"/> is set.
    /// </summary>
    public LambdaValue ArmLambda;

    /// <summary>
    /// Link text for the <c>(link:)</c>-family changers, which <em>create</em>
    /// their own armed label at the macro's position instead of targeting
    /// existing content — reference renders <c>&lt;tw-link&gt;${text}&lt;/tw-link&gt;</c>
    /// in place of the hook (<c>ts/macrolib/links.ts</c>). Null for the
    /// click/hover family; when set, <see cref="HookTarget"/> and
    /// <see cref="StringTarget"/> are null and <see cref="ArmChanger"/> styles
    /// the label.
    /// </summary>
    public string LinkText;

    /// <summary>
    /// True for <c>(link:)</c>/<c>(link-replace:)</c>: the reveal anchor wraps
    /// the label, so the dispatch's fill removes the link along with showing
    /// the content. False for the reveal/repeat/rerun variants, whose label
    /// stays and whose content lands in a sibling anchor.
    /// </summary>
    public bool LinkReplacesLabel;

    /// <summary>
    /// How the dispatch splices each activation's render into the reveal
    /// anchor. Null derives from <see cref="Once"/> (the click family's
    /// append-once / rerun-replace); the link family sets it explicitly —
    /// reveal/repeat append, replace/rerun replace.
    /// </summary>
    public RevisionMode? RevealMode;
  }
}
