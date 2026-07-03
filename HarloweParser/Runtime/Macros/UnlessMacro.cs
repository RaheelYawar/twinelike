using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(unless: $cond)[content]</c> → Changer. Inverse of <see cref="IfMacro"/>:
  /// the returned conditional changer shows the hook iff the value is false —
  /// reference's <c>(d, expr) =&gt; {d.enabled &amp;&amp;= !expr}</c>
  /// (<c>ts/macrolib/stylechangers.ts</c>). The negation lives in the patch
  /// (keyed by <see cref="ConditionalKind.Unless"/>), so the raw argument is
  /// preserved for changer equality and source stamping.
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
      return HarloweValue.OfChanger(Changer.FromConditional(ConditionalKind.Unless, v.AsBool));
    }
  }
}
