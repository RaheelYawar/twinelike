using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// One <c>(link:)</c>-family changer: <c>(name: text, [changer])[deferred]</c>.
  /// Unlike the click/hover family — which arms <em>existing</em> content —
  /// these create their own link-shaped armed label in place of the attached
  /// hook, and the click reveals the hook at that spot. The four variants
  /// differ in what survives the click (reference's <c>clickEvent</c> branches
  /// in <c>ts/macrolib/links.ts</c>):
  /// <list type="bullet">
  /// <item><c>(link:)</c>/<c>(link-replace:)</c> — the content replaces the
  /// link (label nested inside the reveal anchor). Single-use.</item>
  /// <item><c>(link-reveal:)</c>/<c>(link-append:)</c> — the label stays as
  /// plain text, content appends after it. Single-use.</item>
  /// <item><c>(link-repeat:)</c> — stays clickable; each activation appends
  /// another run of the hook.</item>
  /// <item><c>(link-rerun:)</c> — stays clickable; each activation replaces
  /// the previous run's content, label untouched.</item>
  /// </list>
  /// Signature <c>[String, optional(Changer)]</c>: the optional changer styles
  /// the <em>link</em> (the armed region), while changers composed onto the
  /// macro style only the revealed hook — reference: "exempt from all changers
  /// that would normally apply to the hook". Registered six times in
  /// <see cref="StandardMacros"/>.
  /// </summary>
  public class LinkMacro : IMacro
  {
    private readonly string _name;
    private readonly RevisionMode _revealMode;
    private readonly bool _once;
    private readonly bool _replacesLabel;

    public LinkMacro(string name, RevisionMode revealMode, bool once, bool replacesLabel)
    {
      _name = name;
      _revealMode = revealMode;
      _once = once;
      _replacesLabel = replacesLabel;
    }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      string display = "(" + _name + ":)";
      var error = LinkMacroSupport.ValidateText(args[0], display, out var text);
      if (error != null) return error;

      Changer armChanger = null;
      if (args.Count > 1)
      {
        var v = args[1];
        if (v.Kind != HarloweValueKind.Changer)
          return HarloweValue.OfError($"{display} requires a changer as its optional second argument, got {v.Kind}");
        if (!v.AsChanger.CanEnchant)
          return HarloweValue.OfError($"The changer given to {display} can't be (or include) a revision, enchantment, or interaction changer like (replace:), (click:), or (link:).");
        armChanger = v.AsChanger;
      }

      return HarloweValue.OfChanger(Changer.FromInteraction(new InteractionSpec
      {
        Kind = InteractionKind.Click,
        Once = _once,
        LinkText = text,
        LinkReplacesLabel = _replacesLabel,
        RevealMode = _revealMode,
        ArmChanger = armChanger
      }));
    }
  }

  /// <summary>
  /// <c>(link-goto: text, [passage])</c>. Emits a plain navigation link at the
  /// macro's position — reference: "the link syntax is, essentially, a
  /// syntactic shorthand for (link-goto:)", and this engine keeps that exact
  /// symmetry by emitting the same <see cref="IRenderOutput.Link"/> event
  /// <c>[[text-&gt;passage]]</c> produces. The passage name defaults to the
  /// text (<c>(link-goto: "Cellar")</c> ≡ <c>[[Cellar]]</c>).
  ///
  /// <para>A dangling target emits the label as plain prose plus an in-prose
  /// error and <em>no</em> link event — the same treatment
  /// <see cref="BodyRenderer.Visit(Ast.Body.LinkNode)"/> gives
  /// <c>[[text-&gt;passage]]</c>, so the shorthand symmetry holds for broken
  /// links too. Reference gates on <c>Passages.hasValid</c> and renders an
  /// unclickable <c>&lt;tw-broken-link&gt;</c>; see that method for why we spend
  /// the error channel rather than a broken-link one.</para>
  /// </summary>
  public class LinkGotoMacro : IMacro
  {
    public string Name => "link-goto";
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = LinkMacroSupport.ValidateText(args[0], "(link-goto:)", out var text);
      if (error != null) return error;

      string passage = text;
      if (args.Count > 1)
      {
        var v = args[1];
        if (v.Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"(link-goto:) requires a passage name String as its second argument, got {v.Kind}");
        passage = v.AsString;
      }

      if (context.Output == null)
        return HarloweValue.OfError("(link-goto:) can only be used in passage prose in this engine");

      // Existence check skipped when no story is wired (standalone renderer
      // tests leave PassageExists null), as in (goto:).
      if (context.PassageExists != null && !context.PassageExists(passage))
      {
        if (!string.IsNullOrEmpty(text)) context.Output.Text(text);
        return HarloweValue.OfError($"I can't link to the passage '{passage}' because it doesn't exist.");
      }

      context.Output.Link(text, passage);
      return null;
    }
  }

  /// <summary>
  /// <c>(link-undo: text, [alt])</c>. Renders a link-shaped armed label that
  /// undoes the current turn when activated — the label-only sibling of the
  /// <c>(link:)</c> changers, dispatched through the same undo branch as
  /// <c>(click-undo:)</c>. When there is no past turn, renders the optional
  /// <paramref name="alt"/> text instead (or nothing) — reference shows the
  /// second string rather than erroring, unlike <c>(click-undo:)</c>.
  /// </summary>
  public class LinkUndoMacro : IMacro
  {
    public string Name => "link-undo";
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = LinkMacroSupport.ValidateText(args[0], "(link-undo:)", out var text);
      if (error != null) return error;

      string alt = string.Empty;
      if (args.Count > 1)
      {
        var v = args[1];
        if (v.Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"(link-undo:) requires a String as its second argument, got {v.Kind}");
        alt = v.AsString;
      }

      if (context.Output == null)
        return HarloweValue.OfError("(link-undo:) can only be used in passage prose in this engine");

      if (context.CanUndo != null && !context.CanUndo())
      {
        if (alt.Length > 0) context.Output.Text(alt);
        return null;
      }

      // Outside a tree-building render (a bare buffer in standalone tests)
      // there is nothing to arm — degrade to the visible label.
      if (!(context.Output is Rendering.RenderTreeBuilder builder))
      {
        context.Output.Text(text);
        return null;
      }

      string regionId = context.AllocateRegionId();
      builder.PlantLinkLabel(regionId, text, revealReplacesLabel: false);
      context.Interactions.Add(new Interaction
      {
        LabelTarget = true,
        Kind = InteractionKind.Click,
        Undo = true,
        RegionId = regionId
      });
      return null;
    }
  }

  /// <summary>Shared first-argument validation for the <c>(link:)</c> family.</summary>
  internal static class LinkMacroSupport
  {
    /// <summary>
    /// Validate the link-text argument: a non-empty String. Reference's
    /// <c>emptyLinkTextMessages</c> error on <c>""</c>.
    /// </summary>
    public static HarloweValue ValidateText(HarloweValue value, string macroName, out string text)
    {
      text = null;
      if (value.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"{macroName} requires a String of link text as its first argument, got {value.Kind}");
      if (value.AsString.Length == 0)
        return HarloweValue.OfError("Links can't have empty strings for their displayed text.");
      text = value.AsString;
      return null;
    }
  }
}
