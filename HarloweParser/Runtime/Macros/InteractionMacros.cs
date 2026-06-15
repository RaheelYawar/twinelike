using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// The click/hover interaction macros — one parameterized
  /// <see cref="InteractionMacro"/> over the shared
  /// <see cref="InteractionChangers.Build"/>. Each registered name is an
  /// (<see cref="InteractionKind"/>, <see cref="RevisionMode"/>) pair:
  /// <list type="bullet">
  /// <item><c>(click:)</c> = <c>(click-replace:)</c> = Click + Replace;
  /// <c>-append</c>/<c>-prepend</c> are the other two modes.</item>
  /// <item><c>(mouseover:)</c> / <c>(mouseout:)</c> mirror Click with
  /// <c>MouseOver</c> / <c>MouseOut</c>, each with the same four modes.</item>
  /// </list>
  /// All take a hook-name target and an attached hook (the deferred prose fired
  /// on the user event). Registered twelve times in <see cref="StandardMacros"/>.
  /// </summary>
  internal static class InteractionChangers
  {
    /// <summary>
    /// Validate <paramref name="arg"/> and build the interaction changer, or
    /// return an in-prose error if the argument isn't a hook name. String
    /// targets are deferred to a follow-up — click/hover macros take a
    /// hook-name target only.
    /// </summary>
    public static HarloweValue Build(HarloweValue arg, InteractionKind kind, RevisionMode mode, string macroName)
    {
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.HookName)
        return HarloweValue.OfError($"{macroName} requires a hook name, got {arg.Kind}");
      var spec = new InteractionSpec { HookTarget = arg.AsHookName, Kind = kind, Mode = mode };
      return HarloweValue.OfChanger(Changer.FromInteraction(spec));
    }
  }

  /// <summary>
  /// One click/hover interaction macro: <c>(name: ?target)[deferred]</c>. Carries
  /// its registered name plus the <see cref="InteractionKind"/> (which user
  /// event fires it) and <see cref="RevisionMode"/> (how the deferred hook
  /// splices into the target). Registered once per (name, kind, mode)
  /// combination in <see cref="StandardMacros"/>, the same pattern as
  /// <c>TextColorMacro</c>/<c>ForMacro</c>.
  /// </summary>
  public class InteractionMacro : IMacro
  {
    private readonly string _name;
    private readonly InteractionKind _kind;
    private readonly RevisionMode _mode;

    public InteractionMacro(string name, InteractionKind kind, RevisionMode mode)
    {
      _name = name;
      _kind = kind;
      _mode = mode;
    }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => InteractionChangers.Build(args[0], _kind, _mode, "(" + _name + ":)");
  }
}
