using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// The click/hover interaction macros — one parameterized
  /// <see cref="InteractionMacro"/> over the shared
  /// <see cref="InteractionChangers.Build"/>. Each registered name is an
  /// (<see cref="InteractionKind"/>, combo-mode, once) triple:
  /// <list type="bullet">
  /// <item>Plain <c>(click:)</c> / <c>(mouseover:)</c> / <c>(mouseout:)</c>
  /// (combo mode null) arm the target and <em>reveal</em> the attached hook at
  /// the macro's own position when the event fires.</item>
  /// <item><c>(click-rerun:)</c> is <c>(click:)</c> with <c>once</c> false:
  /// the target stays armed and each activation re-renders the hook over the
  /// previous run's content. Click only, as in reference.</item>
  /// <item>The combos (<c>-replace</c>/<c>-append</c>/<c>-prepend</c>, each
  /// kind) splice the hook into the <em>target</em> instead — the shorthand
  /// for <c>(click: ?1)[(replace: ?1)[…]]</c>.</item>
  /// </list>
  /// All take a hook-name or string target plus an optional armed-region
  /// changer or <c>via</c>-lambda, per reference's shared signature
  /// <c>[either(HookSet,String), optional(either(Changer,
  /// Lambda.TypeSignature(`via`)))]</c> (<c>newEnchantmentMacroFns</c> in
  /// <c>ts/macrolib/enchantments.ts</c>). Registered thirteen times in
  /// <see cref="StandardMacros"/>.
  /// </summary>
  internal static class InteractionChangers
  {
    /// <summary>
    /// Validate the argument list and build the interaction changer, or return
    /// an in-prose error: first argument a hook name or non-empty string,
    /// optional second argument an enchantable changer or <c>via</c>-lambda
    /// styling the armed region.
    /// </summary>
    public static HarloweValue Build(List<HarloweValue> args, InteractionKind kind, RevisionMode? mode, bool once, string macroName)
    {
      var spec = new InteractionSpec { Kind = kind, Mode = mode, Once = once };

      var error = EnchantmentMacroSupport.ValidateTarget(args[0], macroName, out var hookTarget, out var stringTarget);
      if (error != null) return error;
      // Reference: "A string given to this (click:) macro was empty." — the
      // click family errors here (newEnchantmentMacroFns); (enchant:)/(change:)
      // deliberately don't, so the check sits on this side of the shared helper.
      if (hookTarget == null && string.IsNullOrEmpty(stringTarget))
        return HarloweValue.OfError($"A string given to this {macroName} macro was empty.");
      spec.HookTarget = hookTarget;
      spec.StringTarget = stringTarget;

      if (args.Count > 1)
      {
        error = EnchantmentMacroSupport.ValidateStyler(args[1], macroName, out var armChanger, out var armLambda);
        if (error != null) return error;
        spec.ArmChanger = armChanger;
        spec.ArmLambda = armLambda;
      }

      return HarloweValue.OfChanger(Changer.FromInteraction(spec));
    }
  }

  /// <summary>
  /// One click/hover interaction macro:
  /// <c>(name: target, [changer])[deferred]</c>. Carries its registered name,
  /// the <see cref="InteractionKind"/> (which user event fires it), the combo
  /// splice mode (null = plain reveal-at-macro-position), and whether the
  /// interaction is consumed on first fire. Registered once per combination in
  /// <see cref="StandardMacros"/>, the same pattern as
  /// <c>TextColorMacro</c>/<c>ForMacro</c>.
  /// </summary>
  public class InteractionMacro : IMacro
  {
    private readonly string _name;
    private readonly InteractionKind _kind;
    private readonly RevisionMode? _mode;
    private readonly bool _once;

    public InteractionMacro(string name, InteractionKind kind, RevisionMode? mode = null, bool once = true)
    {
      _name = name;
      _kind = kind;
      _mode = mode;
      _once = once;
    }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args, _kind, _mode, _once, "(" + _name + ":)");
  }

  /// <summary>
  /// One click/hover <em>command</em> macro — <c>(click-goto: target,
  /// "Passage")</c> / <c>(click-undo: target)</c> and the mouseover/mouseout
  /// mirrors. Unlike <see cref="InteractionMacro"/> these take no attached
  /// hook: used bare in prose, they register an <see cref="Interaction"/>
  /// directly (the way <c>(enchant:)</c> registers its enchantment) whose
  /// dispatch <em>navigates</em> — reference: "<c>(click-goto: ?1, 'Passage
  /// Name')</c> is equivalent to <c>(click: ?1)[(goto:'Passage Name')]</c>" —
  /// or undoes the turn, rather than revealing deferred content. Reference
  /// registers them via <c>Macros.addCommand</c> over the same enchant
  /// machinery (the <c>[`goto`,`undo`].forEach</c> block in
  /// <c>ts/macrolib/enchantments.ts</c>): signature
  /// <c>[either(HookSet,String), String]</c> for <c>-goto</c> (passage
  /// existence-checked at call time), <c>[either(HookSet,String)]</c> for
  /// <c>-undo</c> (errors on the first turn). Registered six times in
  /// <see cref="StandardMacros"/>.
  /// </summary>
  public class InteractionCommandMacro : IMacro
  {
    private readonly string _name;
    private readonly InteractionKind _kind;
    private readonly bool _undo;

    public InteractionCommandMacro(string name, InteractionKind kind, bool undo)
    {
      _name = name;
      _kind = kind;
      _undo = undo;
    }

    public string Name => _name;
    public int MinArgs => _undo ? 1 : 2;
    public int MaxArgs => _undo ? 1 : 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      string display = "(" + _name + ":)";

      var error = EnchantmentMacroSupport.ValidateTarget(args[0], display, out var hookTarget, out var stringTarget);
      if (error != null) return error;
      if (hookTarget == null && string.IsNullOrEmpty(stringTarget))
        return HarloweValue.OfError($"A string given to this {display} macro was empty.");

      string passage = null;
      if (_undo)
      {
        // Reference checks State.pastLength when the command runs — so the
        // error is in-prose on the first turn, not deferred to click time.
        if (context.CanUndo != null && !context.CanUndo())
          return HarloweValue.OfError("I can't (undo:) on the first turn.");
      }
      else
      {
        var v = args[1];
        if (v.Kind != HarloweValueKind.String)
          return HarloweValue.OfError($"{display} requires a passage name String as its second argument, got {v.Kind}");
        passage = v.AsString;
        if (passage.Length == 0)
          return HarloweValue.OfError($"A string given to this {display} macro was empty.");
        if (context.PassageExists != null && !context.PassageExists(passage))
          return HarloweValue.OfError($"I can't {display} the passage '{passage}' because it doesn't exist.");
      }

      // No reveal anchor and no deferred renderer: the dispatch navigates
      // instead of filling anything, so the only render-tree footprint is the
      // armed wrap InteractionPass puts around each target match.
      context.Interactions.Add(new Interaction
      {
        Target = hookTarget,
        StringTarget = stringTarget,
        Kind = _kind,
        GotoPassage = passage,
        Undo = _undo,
        RegionId = context.AllocateRegionId()
      });
      return null;
    }
  }
}
