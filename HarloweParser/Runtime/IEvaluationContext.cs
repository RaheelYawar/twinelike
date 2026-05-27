namespace Harlowe.Runtime
{
  /// <summary>
  /// Read-only view of session-level state that the evaluator needs to resolve
  /// the built-in identifiers <c>time</c>, <c>visit</c>, <c>visits</c>,
  /// <c>passage</c>, <c>turn</c>, and <c>turns</c>. Kept as a separate
  /// interface so the evaluator does not take a hard dependency on
  /// <see cref="StorySession"/>; tests can provide a static stub.
  /// </summary>
  public interface IEvaluationContext
  {
    /// <summary>Milliseconds the current passage has been on screen, as a Number value.</summary>
    HarloweValue Time { get; }

    /// <summary>How many times the current passage has been entered, as a Number value. Backs both <c>visit</c> and <c>visits</c> identifiers (Harlowe treats them as synonyms).</summary>
    HarloweValue Visits { get; }

    /// <summary>A datamap describing the current passage (name, tags, etc.).</summary>
    HarloweValue Passage { get; }

    /// <summary>
    /// Array of past passage names in visit order (oldest first), excluding
    /// the current passage. May contain duplicates if the player revisited a
    /// passage. Empty before the first <c>Goto</c>. Backs the
    /// <c>(history:)</c> macro.
    /// </summary>
    HarloweValue History { get; }

    /// <summary>
    /// Total number of passage transitions in the current session, as a
    /// Number value. Counts the current passage — so the first passage's
    /// render sees <c>turns = 1</c>, the second sees 2, etc. Zero before any
    /// passage has been entered. Backs both <c>turn</c> and <c>turns</c>
    /// identifiers (reference Harlowe treats them as synonyms — see the
    /// <c>turns</c> getter in <c>ts/section.ts</c> and the matching one in
    /// <c>ts/state.ts</c>).
    /// </summary>
    HarloweValue Turns { get; }
  }
}
