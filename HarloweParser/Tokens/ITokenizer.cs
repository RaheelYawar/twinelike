using System.Collections.Generic;

namespace Harlowe.Tokens
{
  public interface ITokenizer
  {
    IList<Token> Tokenize(string passageBody);
  }
}
