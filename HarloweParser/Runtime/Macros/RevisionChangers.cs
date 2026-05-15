namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Shared construction logic for the revision macros <c>(replace:)</c>,
  /// <c>(append:)</c>, and <c>(prepend:)</c> — they differ only in their
  /// <see cref="RevisionMode"/>. Each turns a single target argument (a hook
  /// name like <c>?cake</c>, or a literal string to find among rendered prose)
  /// into a <see cref="Changer"/> carrying one <see cref="RevisionPatch"/>.
  /// </summary>
  internal static class RevisionChangers
  {
    /// <summary>
    /// Validate <paramref name="arg"/> and build the revision changer, or
    /// return an in-prose error if the argument is neither a hook name nor a
    /// string. <paramref name="macroName"/> is the display name used in the
    /// error message (e.g. <c>"(replace:)"</c>).
    /// </summary>
    public static HarloweValue Build(HarloweValue arg, RevisionMode mode, string macroName)
    {
      if (arg.IsError) return arg;

      var spec = new RevisionSpec { Mode = mode };
      if (arg.Kind == HarloweValueKind.HookName)
      {
        spec.HookTarget = arg.AsHookName;
      }
      else if (arg.Kind == HarloweValueKind.String)
      {
        spec.StringTarget = arg.AsString;
      }
      else
      {
        return HarloweValue.OfError($"{macroName} requires a hook name or string, got {arg.Kind}");
      }

      return HarloweValue.OfChanger(Changer.FromRevision(spec));
    }
  }
}
