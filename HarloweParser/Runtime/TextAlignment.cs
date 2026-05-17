namespace Harlowe.Runtime
{
  /// <summary>
  /// Semantic text alignment, set by <c>(align:)</c> via its arrow-syntax
  /// argument. Engine integrations translate to whatever their text renderer
  /// accepts — CSS <c>text-align</c>, TMP <c>&lt;align&gt;</c>, BBCode
  /// <c>[center]</c>, etc.
  ///
  /// <para>The off-centre variants Harlowe supports (<c>==&gt;&lt;==</c>,
  /// <c>&lt;==&gt;</c> with asymmetric offsets) are not modelled here — the
  /// macro reports an in-prose error for those for now. Adding offset support
  /// would mean a separate <see cref="StyleSpec"/> field rather than more enum
  /// values.</para>
  /// </summary>
  public enum TextAlignment
  {
    Left,
    Center,
    Right,
    Justify
  }
}
