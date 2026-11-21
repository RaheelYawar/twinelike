using System;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Optional output features a host can ask to see in
  /// <see cref="RenderResult.Entries"/>, through
  /// <see cref="StorySession.OutputFeatures"/>. Only hosts that walk that list
  /// need this: anything delivered to an adapter is decided by the capability
  /// interfaces the adapter implements.
  /// </summary>
  [Flags]
  public enum OutputFeatures
  {
    /// <summary>Every entry is a core <see cref="IRenderOutput"/> channel.</summary>
    None = 0,

    /// <summary>A link with a structured label appears as a <see cref="BufferedRenderOutput.Kind.BeginLink"/> … <see cref="BufferedRenderOutput.Kind.EndLink"/> bracket instead of one flat <see cref="BufferedRenderOutput.Kind.Link"/> entry. See <see cref="ILinkLabelOutput"/>.</summary>
    LinkLabels = 1
  }
}
