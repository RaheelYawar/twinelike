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
  }
}