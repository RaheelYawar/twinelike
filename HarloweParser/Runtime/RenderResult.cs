using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// The output produced by <see cref="StorySession.Render"/> or
  /// <see cref="StorySession.Goto"/>. Carries the name of the passage that was
  /// rendered and the structured output from the body renderer.
  ///
  /// <para>
  /// <see cref="Text"/> is a convenience accessor for engines that only need
  /// the prose string. Engines that need structured rendering — separate link
  /// buttons, inline error styling, raw HTML pass-through — should walk
  /// <see cref="Entries"/> instead.
  /// </para>
  /// </summary>
  public class RenderResult
  {
    /// <summary>The passage that was rendered to produce this result.</summary>
    public string PassageName;

    /// <summary>
    /// Concatenated prose: all <c>Text</c> and <c>Html</c> fragments joined in
    /// source order. Links and errors are not included here; inspect
    /// <see cref="Entries"/> to find them.
    /// </summary>
    public string Text;

    /// <summary>
    /// Ordered log of every render event emitted during this passage render:
    /// prose text, HTML pass-through, navigation links, and in-prose errors.
    /// Walk this list for structured rendering.
    /// </summary>
    public List<BufferedRenderOutput.Entry> Entries;
  }
}
