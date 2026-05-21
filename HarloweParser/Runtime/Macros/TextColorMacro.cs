using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(text-color: "red")[hook]</c>. Returns a <see cref="Changer"/> that
  /// sets <see cref="StyleSpec.Color"/> on its wrapped hook. Registered under
  /// four spellings: <c>text-color</c>, <c>text-colour</c>, <c>color</c>,
  /// <c>colour</c> — the registered <see cref="Name"/> is set by the
  /// constructor. The value string is passed through unmodified; engines map
  /// it to whatever their text renderer accepts.
  /// </summary>
  public class TextColorMacro : IMacro
  {
    private readonly string _name;

    public TextColorMacro() : this("text-color") { }
    public TextColorMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a String, got {arg.Kind}");
      var invalid = StyleValueValidator.Validate(_name, arg.AsString);
      if (invalid != null) return invalid;
      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Color = arg.AsString }));
    }
  }
}
