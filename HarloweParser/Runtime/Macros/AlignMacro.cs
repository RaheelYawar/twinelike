using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(align: "==&gt;")[hook]</c>. Returns a <see cref="Changer"/> that sets
  /// <see cref="StyleSpec.Alignment"/> on its wrapped hook. Accepts Harlowe's
  /// arrow syntax:
  /// <list type="bullet">
  /// <item><c>&lt;==</c> → <see cref="TextAlignment.Left"/></item>
  /// <item><c>==&gt;</c> → <see cref="TextAlignment.Right"/></item>
  /// <item><c>=&gt;&lt;=</c> → <see cref="TextAlignment.Center"/></item>
  /// <item><c>&lt;==&gt;</c> → <see cref="TextAlignment.Justify"/></item>
  /// </list>
  /// The off-centre variants Harlowe supports (e.g. <c>==&gt;&lt;==</c>) are
  /// not modelled yet — they currently produce an in-prose error. Other strings
  /// produce an in-prose error.
  /// </summary>
  public class AlignMacro : IMacro
  {
    public string Name => "align";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(align:) requires a String, got {arg.Kind}");

      TextAlignment? alignment = null;
      switch (arg.AsString)
      {
        case "<==": alignment = TextAlignment.Left; break;
        case "==>": alignment = TextAlignment.Right; break;
        case "=><=": alignment = TextAlignment.Center; break;
        case "<==>": alignment = TextAlignment.Justify; break;
      }
      if (alignment == null)
        return HarloweValue.OfError($"(align:) does not recognise alignment '{arg.AsString}'");

      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec { Alignment = alignment }));
    }
  }
}
