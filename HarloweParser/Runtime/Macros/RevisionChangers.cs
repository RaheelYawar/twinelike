using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Shared construction logic for the revision macros <c>(replace:)</c>,
  /// <c>(append:)</c>, and <c>(prepend:)</c> — they differ only in their
  /// <see cref="RevisionMode"/>. Variadic, per reference's
  /// <c>rest(either(HookSet, String))</c> signature (the <c>revisionTypes</c>
  /// block in <c>ts/macrolib/enchantments.ts</c>): each argument — a hook name
  /// like <c>?cake</c>, or a literal string to find among rendered prose —
  /// becomes one <see cref="RevisionSpec"/> on a single <see cref="Changer"/>,
  /// so <c>(replace: ?a, "cat")[…]</c> revises every target with one call.
  /// </summary>
  internal static class RevisionChangers
  {
    /// <summary>
    /// Validate every target argument and build the revision changer, or
    /// return an in-prose error for a non-target argument or an empty string
    /// (reference's <c>!scopes.every(Boolean)</c> check — nothing can be
    /// selected by <c>""</c>, and silently matching nothing hides the bug).
    /// <paramref name="macroName"/> is the display name used in error messages
    /// (e.g. <c>"(replace:)"</c>).
    /// </summary>
    public static HarloweValue Build(List<HarloweValue> args, RevisionMode mode, string macroName)
    {
      var specs = new List<RevisionSpec>(args.Count);
      for (int i = 0; i < args.Count; i++)
      {
        var arg = args[i];
        if (arg.Kind == HarloweValueKind.HookName)
        {
          specs.Add(new RevisionSpec { HookTarget = arg.AsHookName, Mode = mode });
        }
        else if (arg.Kind == HarloweValueKind.String)
        {
          if (arg.AsString.Length == 0)
            return HarloweValue.OfError($"A string given to this {macroName} macro was empty.");
          specs.Add(new RevisionSpec { StringTarget = arg.AsString, Mode = mode });
        }
        else
        {
          return HarloweValue.OfError($"{macroName} requires a hook name or string, got {arg.Kind}");
        }
      }
      return HarloweValue.OfChanger(Changer.FromRevision(specs));
    }
  }
}
