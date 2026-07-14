namespace Harlowe
{
  /// <summary>
  /// One place a passage failed to parse, as reported by
  /// <see cref="Harlowe.GetParseErrors"/>. The sibling of <see cref="BrokenLink"/>,
  /// and shaped the same way — a host engine calls it at load and shows these to
  /// whoever is building the story.
  ///
  /// <para>Worth surfacing up front for the same reason a dead link is: a broken
  /// passage loads fine (the loaders substitute an error stub rather than aborting)
  /// and only complains when a player actually walks into it. So a syntax error in
  /// passage 47 of an unplayed branch ships silently.</para>
  /// </summary>
  public class ParseError
  {
    /// <summary>The passage that failed to parse.</summary>
    public string PassageName;

    /// <summary>The parser's diagnostic — e.g. <c>use 'is' instead of 'eq'</c>.</summary>
    public string Detail;

    /// <summary>The source text that couldn't be parsed, when the recovery captured it; otherwise <c>null</c>.</summary>
    public string Source;

    /// <summary>1-based line within the passage body; <c>0</c> when unknown.</summary>
    public int Line;

    /// <summary>1-based column within the line; <c>0</c> when unknown.</summary>
    public int Column;

    /// <summary>
    /// True when the <em>entire</em> passage failed and its AST is a synthetic
    /// stub — nothing in it will render but this error. False when the parser
    /// recovered around one bad construct and the rest of the passage still
    /// works. The distinction is what an engine needs to decide between "this
    /// passage is unusable" and "there's a broken macro in here."
    /// </summary>
    public bool IsWholePassage;

    /// <summary>
    /// A ready-to-display diagnostic — what a host engine logs straight to its
    /// console. Built here so every consumer says the same thing.
    /// </summary>
    public string Message
    {
      get
      {
        string tail = IsWholePassage
          ? " The whole passage failed to parse and won't render."
          : string.Empty;
        return "In passage '" + PassageName + "'" + SourcePosition.Suffix(Line, Column)
             + ": " + Detail + "." + tail;
      }
    }

    public override string ToString() => Message;
  }
}
