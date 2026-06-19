namespace Harlowe.Runtime
{
  /// <summary>
  /// Semantic text alignment, set by <c>(align:)</c> via its arrow-syntax
  /// argument. Engine integrations translate to whatever their text renderer
  /// accepts — CSS <c>text-align</c>, TMP <c>&lt;align&gt;</c>, BBCode
  /// <c>[center]</c>, etc.
  ///
  /// <para>This enum is just the four base alignments. The off-centre centre
  /// variants Harlowe supports (e.g. <c>=&gt;&lt;==</c>, <c>==&gt;&lt;=</c>)
  /// aren't extra enum values — <c>(align:)</c> maps them to <see cref="Center"/>
  /// plus a <see cref="StyleSpec.AlignCenterOffsetPercent"/> margin (see
  /// <see cref="Macros.AlignMacro"/>), keeping this enum engine-agnostic.</para>
  /// </summary>
  public enum TextAlignment
  {
    Left,
    Center,
    Right,
    Justify
  }
}
