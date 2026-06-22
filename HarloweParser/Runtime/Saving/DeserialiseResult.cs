using System.Collections.Generic;

namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Outcome of <see cref="SaveSerializer.DeserialiseTimeline"/>: either a
  /// reconstructed timeline (<see cref="Past"/> + <see cref="Present"/>) or an
  /// <see cref="Error"/> message. Deserialisation is <b>atomic</b> — every moment is
  /// parsed and validated (passages still exist, values re-evaluate) before anything
  /// is returned, so a partial/corrupt blob yields an error and no half-built
  /// timeline. Mirrors reference's <c>State.deserialise</c>, which swaps the timeline
  /// only on full success.
  /// </summary>
  public class DeserialiseResult
  {
    /// <summary>Completed turns, oldest first (the loaded timeline minus its present). Null on failure.</summary>
    public List<Moment> Past;

    /// <summary>The live turn the load lands on (the blob's last moment). Null on failure.</summary>
    public Moment Present;

    /// <summary>Human-readable failure reason, or null on success.</summary>
    public string Error;

    /// <summary>True when the load succeeded (<see cref="Error"/> is null).</summary>
    public bool Ok => Error == null;
  }
}
