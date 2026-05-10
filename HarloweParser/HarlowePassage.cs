using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe
{
  public class HarlowePassage
  {
    public string Pid;
    public string Name;
    public string Body;
    public List<string> Tags;
    public List<Branch> Branches;

    /// <summary>
    /// The parsed Harlowe body as a tree of <see cref="IBodyNode"/>s. Produced
    /// by the new tokenizer + body-parser pipeline. Consumers that need
    /// structural access (rendering with macro effects, evaluation, static
    /// analysis) should walk this tree rather than the legacy <see cref="Body"/>
    /// string.
    /// </summary>
    public PassageBody Ast;

    /// <summary>
    /// The original source text of the passage body, captured before
    /// tokenization. Used by Twee write-out for lazy reserialization: clean
    /// passages emit their <see cref="RawBody"/> verbatim so a Twee file
    /// round-tripped through the library only diverges on passages a consumer
    /// actually edited. Populated by both the HTML constructor (post
    /// HTML-entity decoding) and <see cref="Twee.TweeReader"/>.
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
