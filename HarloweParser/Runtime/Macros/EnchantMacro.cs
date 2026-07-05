using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(enchant: target, changer-or-lambda)</c>. Registers a persistent
  /// enchantment: the changer (or the changer a <c>via</c> lambda produces per
  /// match) is applied to every match of the target — a hook name, or a string
  /// matched against rendered prose — by <see cref="EnchantmentPass"/> after
  /// the passage finishes rendering. So it catches hooks declared after this
  /// macro and content rewritten by revision macros, unlike the one-shot
  /// <c>(change:)</c>. <c>(enchant: ?page, …)</c> is the documented
  /// whole-passage styling idiom. Produces no visible output of its own.
  /// </summary>
  public class EnchantMacro : IMacro
  {
    public string Name => "enchant";
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = EnchantmentMacroSupport.Validate(args, "(enchant:)", out var enchantment);
      if (error != null) return error;

      context.Enchantments.Add(enchantment);
      return null;
    }
  }
}
