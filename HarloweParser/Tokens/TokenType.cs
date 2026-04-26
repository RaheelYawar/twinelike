namespace Harlowe.Tokens
{
  public enum TokenType
  {
    Text,
    Newline,

    Variable,
    TempVariable,

    MacroOpen,
    MacroClose,

    HookOpen,
    HookClose,
    HookNameLeft,
    HookNameRight,

    LinkOpen,
    LinkClose,
    LinkArrowRight,
    LinkArrowLeft,

    StringLiteral,
    NumberLiteral,
    BoolLiteral,
    Identifier,
    Operator,
    Comma,
    ParenOpen,
    ParenClose,
    BracketOpen,
    BracketClose,

    HtmlTag,
    EndOfFile
  }
}
