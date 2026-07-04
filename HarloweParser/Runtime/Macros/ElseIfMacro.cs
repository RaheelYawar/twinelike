using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(else-if: $cond)[content]</c> → Changer. Reads the pairing state at
  /// <em>call</em> time and bakes the combined decision — preceding conditional
  /// hook hidden AND its own Boolean true — into a conditional changer:
  /// reference's <c>new Changer(`elseif`, [section.stackTop.lastHookShown ===
  /// false &amp;&amp; !!expr])</c> (<c>ts/macrolib/stylechangers.ts</c>). With
  /// no preceding conditional in scope it errors, matching reference's
  /// <c>lastHookShown === undefined</c> check.
  ///
  /// <para>Registered once under <c>else-if</c> (the documented spelling); the
  /// registry normalizes names case- and dash/underscore-insensitively
  /// (<see cref="MacroNames"/>), so <c>(elseif:)</c> resolves to the same entry.
  /// Genuinely different spellings like <c>text-colour</c>/<c>colour</c> still
  /// need explicit aliases since they don't differ only by dashes.</para>
  ///
  /// <para><b>The hide-preserves-state special case</b> lives in the renderer's
  /// pairing update: when the expression applying a hidden hook is an
  /// <c>(else-if:)</c>, the pairing keeps the value the original <c>(if:)</c>
  /// left there, so an <c>(if:)…(else-if:)…(else:)</c> ladder behaves as one
  /// chain. Mirrors reference's <c>section.ts</c> ("(else-if:) must be
  /// special-cased, so that it doesn't affect lastHookShown, instead preserving
  /// the value of the original (if:)").</para>
  ///
  /// <para>Returns a baked <c>(if:)</c> changer — see <see cref="ElseMacro"/>
  /// and <see cref="Changer.FromBakedConditional"/>.</para>
  /// </summary>
  public class ElseIfMacro : IMacro
  {
    private readonly string _name;

    public ElseIfMacro() : this("else-if") { }
    public ElseIfMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      // No conditional in scope: structural mistake, surfaced as an in-prose
      // error (matches reference's "lastHookShown === undefined" check).
      if (context.LastConditional == null)
        return HarloweValue.OfError("There's no (if:) or something else before this to do (else-if:) with.");

      var v = args[0];
      if (v.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"(else-if:) requires a Boolean, got {v.Kind}");

      bool show = context.LastConditional == false && v.AsBool;
      return HarloweValue.OfChanger(Changer.FromBakedConditional(show));
    }
  }
}
