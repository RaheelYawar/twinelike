using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Shared argument validation for the macros built on reference's enchant
  /// signature <c>[either(HookSet, String), either(Changer,
  /// Lambda.TypeSignature(`via`))]</c> (<c>ts/macrolib/enchantments.ts</c>):
  /// <see cref="Validate"/> is the <c>(change:)</c>/<c>(enchant:)</c> pair
  /// check, and the <see cref="ValidateTarget"/>/<see cref="ValidateStyler"/>
  /// halves are shared with the <c>(click:)</c> family
  /// (<see cref="InteractionChangers.Build"/>), whose second argument is
  /// optional and whose string target must additionally be non-empty.
  /// </summary>
  internal static class EnchantmentMacroSupport
  {
    /// <summary>
    /// Validate a hook-name-or-string target argument. On success returns
    /// <c>null</c> and sets exactly one of <paramref name="hookTarget"/> /
    /// <paramref name="stringTarget"/>; on failure returns the in-prose error.
    /// The empty-string rule is the caller's: reference errors on <c>""</c>
    /// for the click family but not for <c>(enchant:)</c>/<c>(change:)</c>,
    /// whose empty string simply selects nothing.
    /// </summary>
    public static HarloweValue ValidateTarget(HarloweValue value, string macroName,
                                              out HookNameValue hookTarget, out string stringTarget)
    {
      hookTarget = null;
      stringTarget = null;

      if (value.IsError) return value;
      if (value.Kind == HarloweValueKind.HookName)
      {
        hookTarget = value.AsHookName;
        return null;
      }
      if (value.Kind == HarloweValueKind.String)
      {
        stringTarget = value.AsString;
        return null;
      }
      return HarloweValue.OfError($"{macroName} requires a hook name or string as its first argument, got {value.Kind}");
    }

    /// <summary>
    /// Validate a changer-or-<c>via</c>-lambda styler argument. On success
    /// returns <c>null</c> and sets exactly one of <paramref name="changer"/> /
    /// <paramref name="lambda"/>; on failure returns the in-prose error.
    /// Applies reference's <c>notRevisionChanger</c> check to a changer.
    /// </summary>
    public static HarloweValue ValidateStyler(HarloweValue value, string macroName,
                                              out Changer changer, out LambdaValue lambda)
    {
      changer = null;
      lambda = null;

      if (value.IsError) return value;
      if (value.Kind == HarloweValueKind.Changer)
      {
        if (!value.AsChanger.CanEnchant)
          return HarloweValue.OfError($"The changer given to {macroName} can't include a revision, enchantment, or interaction changer like (replace:), (click:), or (link:)");
        changer = value.AsChanger;
        return null;
      }
      if (value.Kind == HarloweValueKind.Lambda)
      {
        // Reference's Lambda.TypeSignature(`via`) demands exactly a via clause:
        // where/each/making/when lambdas are a different shape and rejected.
        var node = value.AsLambda?.Node;
        if (node == null || node.ViaClause == null || node.WhereClause != null
            || node.MakingName != null || node.WhenClause != null || node.IsEach)
          return HarloweValue.OfError($"{macroName} requires a changer or a 'via' lambda as its second argument");
        lambda = value.AsLambda;
        return null;
      }
      return HarloweValue.OfError($"{macroName} requires a changer or a 'via' lambda as its second argument, got {value.Kind}");
    }

    /// <summary>
    /// Validate the <c>(target, changer-or-lambda)</c> argument pair. On
    /// success returns <c>null</c> and sets <paramref name="enchantment"/> to a
    /// populated descriptor (one target field + one changer/lambda field set);
    /// on failure returns the in-prose error to surface and leaves it null.
    /// <paramref name="macroName"/> is the display name used in error messages
    /// (e.g. <c>"(change:)"</c>).
    /// </summary>
    public static HarloweValue Validate(List<HarloweValue> args, string macroName, out Enchantment enchantment)
    {
      enchantment = null;

      var error = ValidateTarget(args[0], macroName, out var hookTarget, out var stringTarget);
      if (error != null) return error;

      error = ValidateStyler(args[1], macroName, out var changer, out var lambda);
      if (error != null) return error;

      enchantment = new Enchantment
      {
        Target = hookTarget,
        StringTarget = stringTarget,
        Changer = changer,
        Lambda = lambda
      };
      return null;
    }
  }
}
