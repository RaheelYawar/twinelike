using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(if: $cond)[content]</c> → Changer. Evaluates its boolean argument and
  /// returns a conditional changer whose application ANDs the value into
  /// <see cref="HookDescriptor.Enabled"/> — matching reference Harlowe, where
  /// <c>(if:)</c> is <c>new Changer(`if`, [expr])</c> with apply
  /// <c>(d, expr) =&gt; {d.enabled &amp;&amp;= expr}</c>
  /// (<c>ts/macrolib/stylechangers.ts</c>). Being a changer is what lets
  /// <c>(if: $cond) + (text-style: "bold")</c> compose and be stored in
  /// variables. The <c>(else:)</c> pairing is recorded by the renderer when the
  /// changer is applied to a hook, keyed off the applying expression's front
  /// macro name, not here — creating an unattached <c>(if:)</c> (e.g. nested in
  /// a <c>(set:)</c>) leaves the pairing alone.
  /// </summary>
  public class IfMacro : IMacro
  {
    public string Name => "if";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"(if:) requires a Boolean, got {v.Kind}");
      return HarloweValue.OfChanger(Changer.FromConditional(ConditionalKind.If, v.AsBool));
    }
  }
}
