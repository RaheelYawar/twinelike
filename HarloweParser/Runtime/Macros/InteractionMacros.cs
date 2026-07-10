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
}
