using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(print: $value)</c>. In prose, a string renders as markup, so
  /// <c>(print: "''bold''")</c> is bold and <c>(print: "[[Go->Next]]")</c> is a
  /// link, as in reference (<c>ts/macrolib/commands.ts</c>). An array prints
  /// its items the same way, and other values print as their descriptive text,
  /// taken literally.
  ///
  /// <para>Values whose printed form isn't markup are returned as themselves,
  /// so one stored in a variable keeps its type through save and load. Numbers
  /// and booleans become strings instead, because an attached boolean would
  /// show or hide a following hook. A changer can't be returned as itself for
  /// the same reason, so it becomes literal text that saves as this call.</para>
  /// </summary>
  public class PrintMacro : IMacro
  {
    public string Name => "print";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      switch (v.Kind)
      {
        case HarloweValueKind.String:
        case HarloweValueKind.Array:
        case HarloweValueKind.Datamap:
        case HarloweValueKind.Colour:
        case HarloweValueKind.Datatype:
          return v;
        case HarloweValueKind.Changer:
          string source = v.AsChanger.Source;
          return HarloweValue.OfLiteralText(v.ToHarloweString(), source == null ? null : "(print:" + source + ")");
        default:
          return HarloweValue.OfString(v.ToHarloweString());
      }
    }
  }
}
