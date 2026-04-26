using System.Collections.Generic;

namespace Harlowe.Ast.Body
{
  /// <summary>
  /// The root of a parsed passage body. <see cref="Children"/> is the ordered
  /// sequence of nodes a renderer should walk to produce the visible passage.
  /// </summary>
  public class PassageBody
  {
    public List<IBodyNode> Children;
  }
}
