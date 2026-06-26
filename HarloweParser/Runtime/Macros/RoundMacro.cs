using System;
using System.Collections.Generic;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(round: 1.5)</c> → <c>2</c>. Rounds a single number to the nearest whole
  /// number, with halves going up — toward +infinity, so <c>(round: -1.5)</c> is
  /// <c>-1</c>. Matches reference Harlowe's <c>Math.round</c>
  /// (<c>ts/macrolib/values.ts</c>: <c>round: [round, Number]</c>, with
  /// <c>round</c> destructured from <c>Math</c>).
  ///
  /// <para>Implemented by comparing the fractional part
  /// (<c>x - Math.Floor(x) &gt;= 0.5</c>), not C#'s <c>Math.Round</c> nor the
  /// textbook <c>Math.Floor(x + 0.5)</c>. <c>Math.Round</c>'s default is banker's
  /// rounding (<c>2.5</c> → <c>2</c>), and <c>MidpointRounding.AwayFromZero</c>
  /// rounds negative halves the wrong way (<c>-1.5</c> → <c>-2</c>, where JS gives
  /// <c>-1</c>); <c>MidpointRounding.ToPositiveInfinity</c> would match but isn't
  /// on <c>netstandard2.0</c>. <c>Math.Floor(x + 0.5)</c> is the form
  /// <c>Math.round</c> deliberately avoids — at <c>x = 0.49999999999999994</c>
  /// the <c>+ 0.5</c> rounds up to <c>1.0</c>, wrongly yielding <c>1</c>. The
  /// fractional part is exact for <c>x &gt;= 0</c>, so the half-boundary resolves
  /// correctly and the comparison reproduces JS half-up across all signs.</para>
  /// </summary>
  public class RoundMacro : IMacro
  {
    public string Name => "round";
    public int MinArgs => 1;
    public int MaxArgs => 1;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var v = args[0];
      if (v.IsError) return v;
      if (v.Kind != HarloweValueKind.Number)
        return HarloweValue.OfError($"(round:) requires a Number, got {v.Kind}");
      // Round half up (toward +infinity). Compare the fractional part rather
      // than Math.Floor(x + 0.5): the +0.5 form loses precision at the largest
      // double below 0.5 (0.49999999999999994), where x + 0.5 rounds up to
      // exactly 1.0 and would wrongly yield 1. x - Math.Floor(x) is exact for
      // x >= 0, so the half-boundary resolves correctly.
      double x = v.AsNumber, f = Math.Floor(x);
      return HarloweValue.OfNumber(x - f >= 0.5 ? f + 1 : f);
    }
  }
}
