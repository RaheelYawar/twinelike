using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(else-if: $cond)[content]</c>. A convenient combination of
  /// <c>(else:)</c> and <c>(if:)</c>: it renders its hook iff the preceding
  /// conditional hook was <em>hidden</em> AND its own Boolean argument is true.
  /// Stores the render decision on <see cref="MacroContext.LastConditional"/>
  /// so a following <c>(else-if:)</c>/<c>(else:)</c> chains correctly.
  ///
  /// <para>Registered under both <c>else-if</c> (the documented spelling) and
  /// <c>elseif</c> — this codebase does no macro-name normalization, so each
  /// spelling is wired explicitly, the same way <c>text-colour</c>/<c>colour</c>
  /// are.</para>
  ///
  /// <para><b>The hide-preserves-state special case.</b> When an
  /// <c>(else-if:)</c> hides its hook it deliberately <em>does not</em> overwrite
  /// <see cref="MacroContext.LastConditional"/>, preserving the value the
  /// original <c>(if:)</c> left there. This is what makes a
  /// <c>(if:)…(else-if:)…(else:)</c> ladder behave as one chain rather than each
  /// clause only seeing its immediate predecessor — without it, a true
  /// <c>(if:)</c> followed by a non-matching <c>(else-if:)</c> would wrongly let
  /// a trailing <c>(else:)</c> render. Mirrors reference Harlowe, which
  /// special-cases <c>elseif</c> in <c>section.ts</c> ("(else-if:) must be
  /// special-cased, so that it doesn't affect lastHookShown, instead preserving
  /// the value of the original (if:)").</para>
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
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.Bool)
        return HarloweValue.OfError($"(else-if:) requires a Boolean, got {v.Kind}");

      bool render = context.LastConditional == false && v.AsBool;
      // Shown → record it so a following (else:)/(else-if:) sees "matched".
      // Hidden → preserve the prior value (the special case above); leaving
      // LastConditional untouched is exactly that preservation.
      if (render) context.LastConditional = true;
      return HarloweValue.OfBool(render);
    }
  }
}
