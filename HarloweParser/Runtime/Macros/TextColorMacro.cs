using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text-colour: red)[hook]</c> or <c>(text-colour: "#a4e")[hook]</c>.
  /// Returns a <see cref="Changer"/> that sets <see cref="StyleSpec.Color"/> on
  /// its wrapped hook. Registered under four spellings: <c>text-color</c>,
  /// <c>text-colour</c>, <c>color</c>, <c>colour</c> — the registered
  /// <see cref="Name"/> is set by the constructor.
  ///
  /// <para>Takes a typed <see cref="HarloweValueKind.Colour"/> (a built-in name,
  /// a hex literal, or the product of <c>(rgb:)</c>/<c>(hsl:)</c>), which is
  /// emitted as its CSS <c>rgba()</c> form; a String is still accepted and
  /// passed through unmodified, so engines that speak their own colour
  /// vocabulary keep working. Reference takes only a Colour here
  /// (<c>ts/macrolib/stylechangers.ts</c>); the String arm is a deliberate
  /// superset, and the one that carries the author's own value to the engine
  /// verbatim.</para>
  /// </summary>
  public class TextColorMacro : IMacro
  {
    private readonly string _name;

    public TextColorMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;

      // A typed Colour is engine-generated from numeric components, so it
      // skips the author-string validator — there is no author text in the
      // rgba() form it emits.
      if (arg.Kind == HarloweValueKind.Colour)
        return HarloweValue.OfChanger(
          Changer.FromStyle(new StyleSpec { Color = arg.AsColour.ToCssString() }));

      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a Colour or String, got {arg.Kind}");
      var invalid = StyleValueValidator.Validate(_name, arg.AsString);
      if (invalid != null) return invalid;
      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Color = arg.AsString }));
    }
  }
}
