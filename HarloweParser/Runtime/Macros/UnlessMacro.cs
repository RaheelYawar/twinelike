using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(unless: $cond)[content]</c> → Changer. Inverse of <see cref="IfMacro"/>:
  /// the returned conditional changer shows the hook iff the value is false —
  /// reference's <c>(d, expr) =&gt; {d.enabled &amp;&amp;= !expr}</c>
  /// (<c>ts/macrolib/stylechangers.ts</c>). The negated decision is baked into
  /// the patch's Value; <see cref="ConditionalKind.Unless"/> only keeps
  /// <c>(unless:false)</c> structurally distinct from <c>(if:true)</c>, as
  /// reference changer equality does via <c>macroName</c>.
  /// </summary>
  public class UnlessMacro : IMacro
  {
    public string Name => "unless";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"(unless:) requires a Boolean, got {v.Kind}");
      return HarloweValue.OfChanger(Changer.FromConditional(ConditionalKind.Unless, !v.AsBool));
    }
  }
}
