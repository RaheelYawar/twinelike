namespace Harlowe.Tokens
{
  /// <summary>
  /// Categorises a single lexical unit produced by <see cref="HarloweTokenizer"/>.
  /// Some token types only appear inside passage-body context (markup like
  /// <see cref="HookOpen"/>, <see cref="LinkOpen"/>); others only appear inside
  /// expression context — the area between a macro's opening colon and its
  /// closing paren — where the tokenizer is reading values rather than prose
  /// (<see cref="StringLiteral"/>, <see cref="Operator"/>, etc.).
  /// </summary>
  public enum TokenType
  {
    /// <summary>Literal prose between markup. Body context only.</summary>
    Text,

    /// <summary>A line break in passage prose. Harlowe treats blank lines as paragraph breaks at render time.</summary>
    Newline,

    /// <summary>A story-scoped variable reference, e.g. <c>$hp</c>. The Value omits the leading <c>$</c>.</summary>
    Variable,

    /// <summary>A passage-scoped (temporary) variable, e.g. <c>_loop</c>. The Value omits the leading <c>_</c>.</summary>
    TempVariable,

    /// <summary>The opening of a macro call: <c>(name:</c>. The Value holds the macro name. Pushes the tokenizer into expression mode.</summary>
    MacroOpen,

    /// <summary>The closing <c>)</c> of a macro call. Pops the tokenizer back to its prior mode.</summary>
    MacroClose,

    /// <summary>The opening <c>[</c> of a hook (a bracketed content region).</summary>
    HookOpen,

    /// <summary>The closing <c>]</c> of a hook.</summary>
    HookClose,

    /// <summary>A left-anchored hook name: <c>&lt;name|</c>. The name attaches to the hook on its left.</summary>
    HookNameLeft,

    /// <summary>A right-anchored hook name: <c>|name&gt;</c>. The name attaches to the hook on its right.</summary>
    HookNameRight,

    /// <summary>A hook reference used as a value inside an expression: <c>?name</c>. The Value omits the leading <c>?</c>. Distinct from the <see cref="HookNameLeft"/>/<see cref="HookNameRight"/> hook <em>declarations</em> in body markup.</summary>
    HookRef,

    /// <summary>The <c>[[</c> that opens a Twine link.</summary>
    LinkOpen,

    /// <summary>The <c>]]</c> that closes a Twine link.</summary>
    LinkClose,

    /// <summary>The <c>-&gt;</c> separator in <c>[[text-&gt;target]]</c>.</summary>
    LinkArrowRight,

    /// <summary>The <c>&lt;-</c> separator in <c>[[target&lt;-text]]</c>.</summary>
    LinkArrowLeft,

    /// <summary>A quoted string inside a macro argument list.</summary>
    StringLiteral,

    /// <summary>A numeric literal inside a macro argument list.</summary>
    NumberLiteral,

    /// <summary>The keywords <c>true</c> or <c>false</c> inside an expression.</summary>
    BoolLiteral,

    /// <summary>
    /// A colour literal inside an expression: a built-in name (<c>red</c>,
    /// <c>navy</c> — reference's <c>colour</c> hue-name list in
    /// <c>ts/markup/patterns.ts</c>) or a hex form (<c>#a4e</c>/<c>#691212</c>).
    /// Value is the raw lexeme, preserved for round-trip printing.
    /// </summary>
    ColourLiteral,

    /// <summary>An identifier inside an expression — typically a value-returning macro name or keyword.</summary>
    Identifier,

    /// <summary>An operator inside an expression: <c>+</c>, <c>-</c>, <c>is</c>, <c>to</c>, <c>and</c>, etc.</summary>
    Operator,

    /// <summary>The <c>,</c> that separates macro arguments or collection items.</summary>
    Comma,

    /// <summary>An expression-context <c>(</c> used for grouping (not a macro opener).</summary>
    ParenOpen,

    /// <summary>An expression-context <c>)</c> matching a <see cref="ParenOpen"/>.</summary>
    ParenClose,

    /// <summary>An expression-context <c>[</c> used inside collection or indexer syntax.</summary>
    BracketOpen,

    /// <summary>An expression-context <c>]</c> matching a <see cref="BracketOpen"/>.</summary>
    BracketClose,

    /// <summary>A raw HTML tag in passage body — passed through verbatim because Harlowe permits inline HTML.</summary>
    HtmlTag,

    /// <summary>
    /// An inline text-formatting delimiter in passage body — <c>''</c> (bold) or
    /// <c>//</c> (italic). The Value is the literal delimiter (<c>"''"</c> /
    /// <c>"//"</c>). These are symmetric toggle markers (opener and closer are
    /// identical, per reference Harlowe's <c>boldOpener</c>/<c>italicOpener</c>);
    /// the body parser folds matching pairs into a <see cref="Ast.Body.FormatNode"/>
    /// and degrades any unmatched delimiter to literal text.
    /// </summary>
    FormatDelimiter,

    /// <summary>Sentinel emitted once the input is exhausted.</summary>
    EndOfFile
  }
}
