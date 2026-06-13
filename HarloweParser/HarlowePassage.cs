using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe
{
  public class HarlowePassage
  {
    public string Pid;

    /// <summary>
    /// The author-facing passage name. <strong>Do not mutate after the passage
    /// has been added to a story</strong> — the story's internal lookup is
    /// indexed by this value, and writing it directly leaves the dictionary
    /// holding a stale key. Use <see cref="Harlowe.RenamePassage"/> to change
    /// the name; that re-keys the lookup atomically.
    /// <see cref="Harlowe.GetPassage"/> still returns the entry by its
    /// original dictionary key (the runtime hot path must not throw to honour
    /// the project's in-prose error contract), so a stale-key mismatch fails
    /// silently rather than surfacing — the consumer is responsible for
    /// keeping <see cref="Name"/> coherent.
    /// </summary>
    public string Name;

    /// <summary>
    /// The passage's source body text. An alias of <see cref="RawBody"/>: the
    /// two are one underlying string, so editing either is reflected by the
    /// other and by Twee write-out. Kept as the consumer-facing name and the
    /// input for the from-scratch <see cref="Harlowe.AddPassage"/> shorthand.
    /// </summary>
    public string Body { get => RawBody; set => RawBody = value; }

    public List<string> Tags;
    public List<Branch> Branches;

    /// <summary>
    /// The parsed Harlowe body as a tree of <see cref="IBodyNode"/>s. Produced
    /// by the tokenizer + body-parser pipeline. Consumers that need structural
    /// access (rendering with macro effects, evaluation, static analysis)
    /// should walk this tree.
    /// </summary>
    public PassageBody Ast;

    /// <summary>
    /// The passage's source body text, captured before tokenization. Twee
    /// write-out reuses it verbatim for non-dirty passages, so a round-tripped
    /// file only diverges on passages a consumer actually edited (mark
    /// <see cref="IsDirty"/> to re-emit from the <see cref="Ast"/> instead).
    /// Populated by the HTML constructor (post HTML-entity decoding) and
    /// <see cref="Twee.TweeReader"/>. <see cref="Body"/> is an alias.
    /// </summary>
    public string RawBody;

    /// <summary>
    /// The raw editor metadata blob from a Twee passage header — the
    /// <c>{"position":"...","size":"..."}</c> JSON that follows the tags. Stored
    /// verbatim (without the surrounding braces) so the writer can emit it
    /// unchanged. <c>null</c> when not present, including for HTML-loaded
    /// stories where this metadata lives on attributes (<c>position</c>,
    /// <c>size</c>) instead.
    /// </summary>
    public string Position;

    /// <summary>
    /// Set by consumers when they mutate <see cref="Ast"/> (or any other
    /// structural field) and want the writer to emit the canonical printed
    /// form rather than the captured <see cref="RawBody"/>. Defaults to false;
    /// the library never flips it automatically.
    /// </summary>
    public bool IsDirty;
  }
}
