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
    /// Walk this list for structured rendering. Holds only core
    /// <see cref="IRenderOutput"/> channels unless the session's
    /// <see cref="StorySession.OutputFeatures"/> asks for more.
    /// </summary>
    public List<BufferedRenderOutput.Entry> Entries;

    /// <summary>
    /// Replay <see cref="Entries"/>, including any edits the host made to the
    /// list, through <paramref name="output"/>. An output implementing
    /// <see cref="ILinkLabelOutput"/> receives structured link labels as
    /// brackets whatever the session's <see cref="StorySession.OutputFeatures"/>;
    /// any other output receives them as flat links. An unmatched bracket in a
    /// hand-built list is repaired, so a label is never lost. Null-safe on
    /// <paramref name="output"/> and <see cref="Entries"/>.
    /// </summary>
    public void ReplayTo(IRenderOutput output)
    {
      if (output == null || Entries == null) return;
      var sink = new OutputFeatureAdapter(output);
      int openLinks = 0;
      for (int i = 0; i < Entries.Count; i++)
      {
        var entry = Entries[i];
        if (entry.Kind == BufferedRenderOutput.Kind.BeginLink)
        {
          openLinks++;
        }
        else if (entry.Kind == BufferedRenderOutput.Kind.EndLink)
        {
          if (openLinks == 0) continue;
          openLinks--;
        }
        entry.ReplayTo(sink);
      }
      for (; openLinks > 0; openLinks--) sink.EndLink();
    }
  }
}
