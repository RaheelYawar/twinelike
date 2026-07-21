namespace Harlowe
{
  /// <summary>
  /// One remark about how a story's declared format version was interpreted, as
  /// reported by <see cref="Harlowe.GetCompatibilityNotices"/>. The third
  /// sibling to <see cref="BrokenLink"/> and <see cref="ParseError"/>, shaped
  /// the same way — a host engine calls it at load and shows these to whoever
  /// is building the story.
  ///
  /// <para>Worth surfacing for the same reason those two are: nothing forces
  /// the question "which Harlowe am I running under?" to come up. A story with
  /// no <c>format-version</c>, or one from a major this library doesn't
  /// implement, still loads and still plays — under a profile nobody chose
  /// deliberately. That is fine until it isn't, and by then the symptom
  /// (prose vanishing at an em-dash, say) looks nothing like its cause.</para>
  /// </summary>
  public class CompatibilityNotice
  {
    /// <summary>How much the reader should care. See <see cref="NoticeSeverity"/>.</summary>
    public NoticeSeverity Severity;

    /// <summary>The story's declared <c>format-version</c> verbatim, or the empty string when it declared none.</summary>
    public string DeclaredVersion;

    /// <summary>The profile actually selected.</summary>
    public HarloweProfile Profile;

    /// <summary>The reason, as a sentence — e.g. <c>no format-version was declared</c>.</summary>
    public string Detail;

    /// <summary>
    /// A ready-to-display diagnostic — what a host engine logs straight to its
    /// console. Built here so every consumer says the same thing.
    /// </summary>
    public string Message
      => "Compatibility: " + Detail + "; running under " + Profile + " semantics.";

    public override string ToString() => Message;
  }

  /// <summary>How much attention a <see cref="CompatibilityNotice"/> deserves.</summary>
  public enum NoticeSeverity
  {
    /// <summary>Worth knowing, not worth acting on — the story is running under a defensible default.</summary>
    Info,

    /// <summary>The declared version could not be honoured as written, so the story is running under semantics it did not ask for.</summary>
    Warning,
  }
}
