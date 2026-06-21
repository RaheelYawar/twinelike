using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// One turn on the session timeline — the unit <see cref="StorySession"/>'s
  /// <c>_past</c>/<c>_present</c>/<c>_future</c> stacks move between. A Moment
  /// records the game state for its turn: the resting passage, the forward delta
  /// of story (<c>$</c>) variables changed during the turn, the intra-turn
  /// redirect trail, and the PRNG position. Mirrors reference Harlowe's Moment
  /// (ts/state/moment.ts).
  ///
  /// <para>Public fields per the project's DTO style. Several fields are wired in
  /// over the course of the save/load slice (see SAVE-LOAD-PLAN.md): the redirect
  /// <see cref="Visits"/> trail and derived visit counts (which will replace the
  /// per-Moment <see cref="VisitCounts"/>), the sparse <see cref="Seed"/>/
  /// <see cref="SeedIter"/> RNG state, and the reserved debug fields.</para>
  /// </summary>
  public class Moment
  {
    /// <summary>The turn's resting passage — where navigation and undo land. Equals the last redirect target when <see cref="Visits"/> is set.</summary>
    public string PassageName;

    /// <summary>
    /// Forward delta of story (<c>$</c>) variables changed during this turn —
    /// only the changed vars, not a full store clone. Flattened oldest-first to
    /// reconstruct full state on undo/redo/load. Captured when the turn leaves the
    /// present slot.
    /// </summary>
    public Dictionary<string, HarloweValue> StoreDelta;

    /// <summary>
    /// Visit counts as of this turn. Carried per-Moment for now; a later step of
    /// the slice derives visit counts from the timeline's passage/redirect trails
    /// instead of snapshotting them.
    /// </summary>
    public Dictionary<string, int> VisitCounts;

    /// <summary>
    /// The ordered passages entered during this turn when it spanned more than one
    /// (an auto-<c>(goto:)</c> redirect chain): entry first, each redirect target
    /// following, ending at <see cref="PassageName"/>. Null for a plain
    /// single-passage turn (entered = just <see cref="PassageName"/>), which lets
    /// such a Moment compress to a bare passage name in a save blob. Feeds the
    /// derived <c>(history:)</c>/<c>visits</c>. (Wired in a later step.)
    /// </summary>
    public List<string> Visits;

    /// <summary>PRNG seed string in effect for this turn. Set only on session start and the future <c>(seed:)</c> macro; null otherwise. (Wired in a later step.)</summary>
    public string Seed;

    /// <summary>PRNG draw position recorded for this turn — set only when the turn drew a random number, so a no-draw turn stays compressible. (Wired in a later step.)</summary>
    public int? SeedIter;

    // Reserved for the (mock-visits:)/(mock-turns:)/(forget-visits:) debug slices.
    public List<string> MockVisits;
    public int? MockTurns;
    public int? ForgetVisits;
  }
}
