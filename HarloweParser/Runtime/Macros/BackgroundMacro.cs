using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(background: "navy")[hook]</c> or <c>(background: "art/sky.png")[hook]</c>.
  /// Returns a <see cref="Changer"/> that sets either
  /// <see cref="StyleSpec.BackgroundColor"/> or
  /// <see cref="StyleSpec.BackgroundImage"/> depending on the shape of the
  /// argument string. Registered under <c>background</c> and the alias
  /// <c>bg</c>.
  ///
  /// <para>The image vs. colour distinction is a heuristic on the string —
  /// values that look like image references (ending in a common image
  /// extension, starting with <c>http://</c>/<c>https://</c>/<c>data:image/</c>,
  /// or wrapped in <c>url(...)</c>) are treated as images; anything else as a
  /// colour. The reference impl has a typed <c>Colour</c> value; we make do
  /// with a string heuristic until/unless a typed colour value lands.</para>
  /// </summary>
  public class BackgroundMacro : IMacro
  {
    private readonly string _name;

    public BackgroundMacro() : this("background") { }
    public BackgroundMacro(string name) { _name = name; }

    public string Name => _name;
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"({_name}:) requires a String, got {arg.Kind}");

      var value = arg.AsString;
      var spec = LooksLikeImage(value)
        ? new StyleSpec { BackgroundImage = value }
        : new StyleSpec { BackgroundColor = value };
      return HarloweValue.OfChanger(Changer.FromStyle(spec));
    }

    private static bool LooksLikeImage(string s)
    {
      if (string.IsNullOrEmpty(s)) return false;
      if (s.StartsWith("http://") || s.StartsWith("https://")
       || s.StartsWith("data:image/") || s.StartsWith("url("))
        return true;
      var lower = s.ToLowerInvariant();
      return lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")
          || lower.EndsWith(".gif") || lower.EndsWith(".svg") || lower.EndsWith(".webp")
          || lower.EndsWith(".bmp");
    }
  }
}
