using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(align: "==&gt;")[hook]</c>. Returns a <see cref="Changer"/> that sets
  /// <see cref="StyleSpec.Alignment"/> on its wrapped hook. Accepts Harlowe's
  /// arrow syntax — the same regex reference Harlowe uses,
  /// <c>^(==+&gt;|&lt;=+|=+&gt;&lt;=+|&lt;==+&gt;)$</c>, so arrows of any length
  /// work:
  /// <list type="bullet">
  /// <item><c>&lt;==</c>, <c>&lt;====</c> → <see cref="TextAlignment.Left"/></item>
  /// <item><c>==&gt;</c>, <c>====&gt;</c> → <see cref="TextAlignment.Right"/></item>
  /// <item><c>=&gt;&lt;=</c> → <see cref="TextAlignment.Center"/> (true centre)</item>
  /// <item><c>&lt;==&gt;</c>, <c>&lt;====&gt;</c> → <see cref="TextAlignment.Justify"/></item>
  /// </list>
  /// Off-centre centre arrows (e.g. <c>=&gt;&lt;==</c>, <c>==&gt;&lt;=</c>) map to
  /// <see cref="TextAlignment.Center"/> with a calibrated
  /// <see cref="StyleSpec.AlignCenterOffsetPercent"/> (the reference's
  /// <c>round(centerIndex / (length - 2) * 50)</c> margin-left percentage; the
  /// balanced 25% case is true centre and leaves the offset null). Any string the
  /// regex rejects produces an in-prose error.
  /// </summary>
  public class AlignMacro : IMacro
  {
    private static readonly Regex ArrowPattern = new Regex(@"^(==+>|<=+|=+><=+|<==+>)$");

    public string Name => "align";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var arg = args[0];
      if (arg.IsError) return arg;
      if (arg.Kind != HarloweValueKind.String)
        return HarloweValue.OfError($"(align:) requires a String, got {arg.Kind}");

      string arrow = arg.AsString;
      if (!ArrowPattern.IsMatch(arrow))
        return HarloweValue.OfError(
          $"(align:) requires an alignment arrow (e.g. \"==>\", \"<==\", \"=><=\"), not \"{arrow}\"");

      TextAlignment alignment;
      int? offset = null;

      int center = arrow.IndexOf("><", StringComparison.Ordinal);
      if (center >= 0)
      {
        alignment = TextAlignment.Center;
        // Reference: round(centerIndex / (length - 2) * 50). 25% is balanced
        // (true centre); other values shift the half-width block left.
        int pct = (int)Math.Round(center / (double)(arrow.Length - 2) * 50, MidpointRounding.AwayFromZero);
        if (pct != 25) offset = pct;
      }
      else if (arrow[0] == '<' && arrow[arrow.Length - 1] == '>')
      {
        alignment = TextAlignment.Justify;
      }
      else if (arrow.IndexOf('>') >= 0)
      {
        alignment = TextAlignment.Right;
      }
      else
      {
        alignment = TextAlignment.Left;
      }

      return HarloweValue.OfChanger(Changer.FromStyle(new StyleSpec
      {
        Alignment = alignment,
        AlignCenterOffsetPercent = offset,
      }));
    }
  }
}
