using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Shared argument validation for the enchantment macros <c>(change:)</c>
  /// and <c>(enchant:)</c> — both take a hook-name-or-string target and a
  /// changer-or-<c>via</c>-lambda, and differ only in <em>when</em> the result
  /// is applied (once now, vs. persistently after every render pass). Mirrors
  /// reference's signature <c>[either(HookSet, String), either(Changer,
  /// Lambda.TypeSignature(`via`))]</c> plus its <c>notRevisionChanger</c> check
  /// (<c>ts/macrolib/enchantments.ts</c>).
  /// </summary>
  internal static class EnchantmentMacroSupport
  {
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

      if (args[0].IsError) return args[0];
      if (args[1].IsError) return args[1];

      var result = new Enchantment();

      if (args[0].Kind == HarloweValueKind.HookName)
        result.Target = args[0].AsHookName;
      else if (args[0].Kind == HarloweValueKind.String)
        result.StringTarget = args[0].AsString;
      else
        return HarloweValue.OfError($"{macroName} requires a hook name or string as its first argument, got {args[0].Kind}");

      if (args[1].Kind == HarloweValueKind.Changer)
      {
        if (!args[1].AsChanger.CanEnchant)
          return HarloweValue.OfError($"The changer given to {macroName} can't include a revision, enchantment, or interaction changer like (replace:), (click:), or (link:)");
        result.Changer = args[1].AsChanger;
      }
      else if (args[1].Kind == HarloweValueKind.Lambda)
      {
        // Reference's Lambda.TypeSignature(`via`) demands exactly a via clause:
        // where/each/making/when lambdas are a different shape and rejected.
        var node = args[1].AsLambda?.Node;
        if (node == null || node.ViaClause == null || node.WhereClause != null
            || node.MakingName != null || node.WhenClause != null || node.IsEach)
          return HarloweValue.OfError($"{macroName} requires a changer or a 'via' lambda as its second argument");
        result.Lambda = args[1].AsLambda;
      }
      else
      {
        return HarloweValue.OfError($"{macroName} requires a changer or a 'via' lambda as its second argument, got {args[1].Kind}");
      }

      enchantment = result;
      return null;
    }
  }
}
