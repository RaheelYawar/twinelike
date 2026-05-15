using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(enchant: ?target, changer)</c>. Registers a persistent enchantment:
  /// the changer is applied to every match of <c>?target</c> by
  /// <see cref="EnchantmentPass"/> after the passage finishes rendering — so
  /// it catches hooks declared after this macro and content rewritten by
  /// revision macros, unlike the one-shot <c>(change:)</c>. <c>(enchant: ?page,
  /// …)</c> is the documented whole-passage styling idiom. Produces no visible
  /// output of its own.
  /// </summary>
  public class EnchantMacro : IMacro
  {
    public string Name => "enchant";
    public int MinArgs => 2;
    public int MaxArgs => 2;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = EnchantmentMacroSupport.Validate(args, "(enchant:)", out var target, out var changer);
      if (error != null) return error;

      context.Enchantments.Add(new Enchantment { Target = target, Changer = changer });
      return null;
    }
  }
}
