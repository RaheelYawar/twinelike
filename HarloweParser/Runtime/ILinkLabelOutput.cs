namespace Harlowe.Runtime
{
  /// <summary>
  /// Optional output feature: receive a link whose label carries structure a
  /// flat <see cref="IRenderOutput.Link"/> string can't express — styled or
  /// revision-spliced content, armed regions, inline HTML. The label arrives
  /// as ordinary events between <see cref="BeginLink"/> and
  /// <see cref="EndLink"/>; render both link shapes as the same navigable link.
  ///
  /// <para>An output that doesn't implement this still gets a working link: the
  /// library flattens the label into one <see cref="IRenderOutput.Link"/>
  /// event, dropping its styling, regions and HTML and moving any errors to
  /// just after the link. A host that walks <see cref="RenderResult.Entries"/>
  /// has no adapter for the library to inspect, and opts in through
  /// <see cref="StorySession.OutputFeatures"/> instead.</para>
  ///
  /// <para>An adapter that wraps another output must implement and forward
  /// this interface too, or the output behind it only receives flat links.</para>
  /// </summary>
  public interface ILinkLabelOutput : IRenderOutput
  {
    /// <summary>Open a navigation link with a structured label. The label arrives as ordinary events until the matching <see cref="EndLink"/>. <paramref name="target"/> follows <see cref="IRenderOutput.Link"/>'s raw-target contract: forwarded unescaped.</summary>
    void BeginLink(string target);

    /// <summary>Close the most recently opened link. Always paired with a prior <see cref="BeginLink"/> on the same render path.</summary>
    void EndLink();
  }
}
