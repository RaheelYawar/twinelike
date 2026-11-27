namespace Harlowe.Runtime
{
  /// <summary>
  /// One turn of a <see cref="StorySession"/>'s timeline as the host sees it, from
  /// <see cref="StorySession.GetTimeline"/>: the turn's resting passage and any
  /// host <see cref="TurnData"/> attached to it. A detached copy — changing it
  /// doesn't touch the session. Public fields per the project's DTO style.
  /// </summary>
  public class TurnRecord
  {
    /// <summary>The passage the turn rested on — the last redirect target when a <c>(goto:)</c> chain ran during the turn.</summary>
    public string PassageName;

    /// <summary>The host data attached to the turn through <see cref="StorySession.TurnData"/>, or null if none.</summary>
    public string TurnData;
  }
}
