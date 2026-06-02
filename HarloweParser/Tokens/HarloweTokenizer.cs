using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harlowe.Tokens
{
  /// <summary>
  /// Default <see cref="ITokenizer"/> implementation for Harlowe passage bodies.
  /// Operates as a mode stack: the outer mode is <see cref="TokenizerMode.Body"/>
  /// (prose-and-markup), and encountering <c>(name:</c> pushes
  /// <see cref="TokenizerMode.Expression"/> until the matching <c>)</c>. Macros
  /// can nest inside macro arguments, so the stack — not a single flag —
  /// tracks the current context.
  /// </summary>
  public class HarloweTokenizer : ITokenizer
  {
    private string _src;
    private int _pos;
    private int _line;
    private int _col;
    private List<Token> _tokens;

    private Stack<Frame> _modes;
    private bool _inLink;

    private static readonly HashSet<string> WordOperators = new HashSet<string>
    {
      "is", "to", "into", "and", "or", "not", "contains", "of",
      "in", "a", "matches", "where", "when", "via", "making", "each",
      "its", "bind"
    };

    /// <summary>
    /// Common-mistake operator spellings that reference Harlowe catches at
    /// lex time and converts into a targeted authoring hint. See the
    /// <c>incorrectOperator</c> pattern in <c>ts/markup/patterns.ts</c>.
    /// Matched in <see cref="ScanIdentifierOrKeyword"/> after the multi-word
    /// fuser fails; throws <see cref="HarloweParseException"/> with the
    /// "Please say X instead of Y" hint.
    /// </summary>
    private static readonly Dictionary<string, string> IncorrectIdentifiers = new Dictionary<string, string>
    {
      { "gt",    "use '>' instead of 'gt'" },
      { "gte",   "use '>=' instead of 'gte'" },
      { "lt",    "use '<' instead of 'lt'" },
      { "lte",   "use '<=' instead of 'lte'" },
      { "eq",    "use 'is' instead of 'eq'" },
      { "neq",   "use 'is not' instead of 'neq'" },
      { "isnot", "use 'is not' instead of 'isnot'" },
      { "isa",   "use 'is a' instead of 'isa'" },
      { "are",   "use 'is' instead of 'are'" },
      { "x",     "use '*' instead of 'x'" },
    };

    /// <summary>
    /// Lex <paramref name="passageBody"/> into a flat token stream terminated by
    /// <see cref="TokenType.EndOfFile"/>. Resets all internal state, so the same
    /// instance can be reused for multiple passages. A null input is treated as
    /// an empty string.
    /// </summary>
    public IReadOnlyList<Token> Tokenize(string passageBody)
    {
      _src = passageBody ?? string.Empty;
      _pos = 0;
      _line = 1;
      _col = 1;
      _tokens = new List<Token>();
      _modes = new Stack<Frame>();
      _modes.Push(new Frame { Mode = TokenizerMode.Body });
      _inLink = false;

      while (_pos < _src.Length)
      {
        if (_modes.Peek().Mode == TokenizerMode.Body)
          ScanBody();
        else
          ScanExpression();
      }

      Emit(TokenType.EndOfFile, string.Empty, _pos, _line, _col);
      return _tokens;
    }

    /// <summary>
    /// Body-mode dispatch: emits exactly one body token (or one Text run) per
    /// call by inspecting the current character. Defers to <see cref="ScanLinkContent"/>
    /// while inside a <c>[[…]]</c> link, where prose-level markup is suppressed.
    /// Falls through to <see cref="ScanText"/> when no markup matches.
    /// </summary>
    private void ScanBody()
    {
      if (_inLink) { ScanLinkContent(); return; }

      int startPos = _pos, startLine = _line, startCol = _col;
      char c = _src[_pos];

      switch (c)
      {
        case '\n':
          Advance();
          Emit(TokenType.Newline, "\n", startPos, startLine, startCol);
          return;
        case '(':
          if (TryScanMacroOpen(startPos, startLine, startCol)) return;
          break;
        case '[':
          if (Peek(1) == '[')
          {
            AdvanceN(2);
            _inLink = true;
            Emit(TokenType.LinkOpen, "[[", startPos, startLine, startCol);
            return;
          }
          Advance();
          Emit(TokenType.HookOpen, "[", startPos, startLine, startCol);
          return;
        case ']':
          Advance();
          Emit(TokenType.HookClose, "]", startPos, startLine, startCol);
          return;
        case '<':
          if (TryScanHookNameLeft(startPos, startLine, startCol)) return;
          if (TryScanHtmlTag(startPos, startLine, startCol)) return;
          break;
        case '|':
          if (TryScanHookNameRight(startPos, startLine, startCol)) return;
          break;
        case '$':
          if (TryScanVariable(startPos, startLine, startCol)) return;
          break;
        case '_':
          if (TryScanTempVariable(startPos, startLine, startCol)) return;
          break;
      }

      ScanText(startPos, startLine, startCol);
    }

    /// <summary>
    /// Scans inside a <c>[[…]]</c> link. The only meaningful tokens here are
    /// <see cref="TokenType.LinkClose"/>, <see cref="TokenType.LinkArrowRight"/>,
    /// and <see cref="TokenType.LinkArrowLeft"/>; everything else accumulates
    /// into a single <see cref="TokenType.Text"/> run. Clears <c>_inLink</c>
    /// when the closing <c>]]</c> is consumed.
    /// </summary>
    private void ScanLinkContent()
    {
      int startPos = _pos, startLine = _line, startCol = _col;

      if (MatchString("]]"))
      {
        AdvanceN(2);
        _inLink = false;
        Emit(TokenType.LinkClose, "]]", startPos, startLine, startCol);
        return;
      }
      if (MatchString("->"))
      {
        AdvanceN(2);
        Emit(TokenType.LinkArrowRight, "->", startPos, startLine, startCol);
        return;
      }
      if (MatchString("<-"))
      {
        AdvanceN(2);
        Emit(TokenType.LinkArrowLeft, "<-", startPos, startLine, startCol);
        return;
      }

      var sb = new StringBuilder();
      while (_pos < _src.Length)
      {
        if (MatchString("]]") || MatchString("->") || MatchString("<-")) break;
        sb.Append(_src[_pos]);
        Advance();
      }
      if (sb.Length > 0)
        Emit(TokenType.Text, sb.ToString(), startPos, startLine, startCol);
    }

    /// <summary>
    /// Expression-mode dispatch: emits exactly one expression token per call.
    /// Skips leading whitespace (including newlines), then routes by the leading
    /// character to literals, identifiers/keywords, operators, brackets, and the
    /// macro/grouping paren handling that uses the current frame's
    /// <c>ParenDepth</c> to disambiguate <c>)</c> as <see cref="TokenType.ParenClose"/>
    /// vs <see cref="TokenType.MacroClose"/>.
    /// </summary>
    private void ScanExpression()
    {
      // Skip Unicode whitespace between tokens. char.IsWhiteSpace matches
      // NBSP / em-en / ideographic / line+paragraph separators — same set
      // as reference Harlowe's `ws` pattern in ts/markup/patterns.ts, which
      // is used universally in reference for syntactic whitespace.
      // Body-mode is intentionally narrower (only ASCII chars in
      // IsBodySpecial trigger token boundaries) — NBSP inside prose stays
      // as content in the surrounding Text token, matching reference's
      // `text` pattern in ts/markup/patterns.ts (defined as any-char-but-`]`).
      // Same code point plays two roles by context; this isn't an
      // inconsistency, it's the language's tokenizer model.
      while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) Advance();
      if (_pos >= _src.Length) return;

      int startPos = _pos, startLine = _line, startCol = _col;
      char c = _src[_pos];

      switch (c)
      {
        case '(':
          if (TryScanMacroOpen(startPos, startLine, startCol)) return;
          Advance();
          _modes.Peek().ParenDepth++;
          Emit(TokenType.ParenOpen, "(", startPos, startLine, startCol);
          return;
        case ')':
          Advance();
          var frame = _modes.Peek();
          if (frame.ParenDepth > 0)
          {
            frame.ParenDepth--;
            Emit(TokenType.ParenClose, ")", startPos, startLine, startCol);
          }
          else
          {
            _modes.Pop();
            Emit(TokenType.MacroClose, ")", startPos, startLine, startCol);
          }
          return;
        case '[':
          Advance();
          Emit(TokenType.BracketOpen, "[", startPos, startLine, startCol);
          return;
        case ']':
          Advance();
          Emit(TokenType.BracketClose, "]", startPos, startLine, startCol);
          return;
        case ',':
          Advance();
          Emit(TokenType.Comma, ",", startPos, startLine, startCol);
          return;
        case '$':
          if (TryScanVariable(startPos, startLine, startCol)) return;
          break;
        case '_':
          if (TryScanTempVariable(startPos, startLine, startCol)) return;
          break;
        case '"':
          ScanStringLiteral(c, startPos, startLine, startCol);
          return;
        case '\'':
          if (TryScanPossessive(startPos, startLine, startCol)) return;
          ScanStringLiteral(c, startPos, startLine, startCol);
          return;
        case '?':
          if (TryScanHookRef(startPos, startLine, startCol)) return;
          break;
      }

      if (IsAsciiDigit(c))
      {
        if (c == '2' && TryScanTwoBind(startPos, startLine, startCol)) return;
        if (TryScanOrdinal(startPos, startLine, startCol)) return;
        ScanNumberLiteral(startPos, startLine, startCol);
        return;
      }

      if (char.IsLetter(c))
      {
        ScanIdentifierOrKeyword(startPos, startLine, startCol);
        return;
      }

      if (TryScanSymbolOperator(startPos, startLine, startCol)) return;

      Advance();
    }

    /// <summary>
    /// Tries to consume <c>(name:</c>. On success, emits
    /// <see cref="TokenType.MacroOpen"/> with the bare macro name and pushes a
    /// fresh Expression frame onto the mode stack. The cursor is left just past
    /// the colon. Returns false (without consuming anything) if the lookahead
    /// does not match a valid macro opener — e.g. <c>(hello)</c>, which the
    /// caller then treats as text or as a grouping paren.
    /// </summary>
    private bool TryScanMacroOpen(int startPos, int startLine, int startCol)
    {
      int look = _pos + 1;
      int nameStart = look;
      while (look < _src.Length)
      {
        char ch = _src[look];
        if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') look++;
        else break;
      }
      if (look == nameStart) return false;
      if (look >= _src.Length || _src[look] != ':') return false;

      string name = _src.Substring(nameStart, look - nameStart);
      AdvanceN(look - _pos + 1);
      _modes.Push(new Frame { Mode = TokenizerMode.Expression });
      Emit(TokenType.MacroOpen, name, startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a story-scoped variable reference of the form
    /// <c>$name</c>. The leading <c>$</c> is dropped from the emitted token's
    /// <see cref="Token.Value"/>. Requires at least one letter immediately after
    /// the sigil; <c>$5</c> or a bare <c>$</c> returns false so the caller can
    /// treat the character as text.
    /// </summary>
    private bool TryScanVariable(int startPos, int startLine, int startCol)
    {
      if (_pos + 1 >= _src.Length || !char.IsLetter(_src[_pos + 1])) return false;
      Advance();
      int nameStart = _pos;
      while (_pos < _src.Length && IsIdContinue(_src[_pos])) Advance();
      Emit(TokenType.Variable, _src.Substring(nameStart, _pos - nameStart), startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a passage-scoped (temporary) variable reference of the
    /// form <c>_name</c>. The leading <c>_</c> is dropped from the emitted
    /// token's <see cref="Token.Value"/>. Requires a letter immediately after
    /// the underscore so a bare <c>_</c> in prose is not mistaken for a
    /// variable.
    /// </summary>
    private bool TryScanTempVariable(int startPos, int startLine, int startCol)
    {
      if (_pos + 1 >= _src.Length || !char.IsLetter(_src[_pos + 1])) return false;
      Advance();
      int nameStart = _pos;
      while (_pos < _src.Length && IsIdContinue(_src[_pos])) Advance();
      Emit(TokenType.TempVariable, _src.Substring(nameStart, _pos - nameStart), startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a left-anchored hook name <c>&lt;name|</c>, which binds
    /// to the hook on its left. The bare name (without the angle bracket or
    /// pipe) becomes the token's <see cref="Token.Value"/>. Returns false if
    /// the lookahead is not a letter or the trailing <c>|</c> is missing — in
    /// which case the caller may try <see cref="TryScanHtmlTag"/>.
    /// </summary>
    private bool TryScanHookNameLeft(int startPos, int startLine, int startCol)
    {
      int look = _pos + 1;
      if (look >= _src.Length || !char.IsLetter(_src[look])) return false;
      int nameStart = look;
      while (look < _src.Length && IsIdContinue(_src[look])) look++;
      if (look >= _src.Length || _src[look] != '|') return false;
      string name = _src.Substring(nameStart, look - nameStart);
      AdvanceN(look - _pos + 1);
      Emit(TokenType.HookNameLeft, name, startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a right-anchored hook name <c>|name&gt;</c>, which binds
    /// to the hook on its right. The bare name becomes the token's
    /// <see cref="Token.Value"/>. Returns false if the lookahead is not a
    /// letter or the trailing <c>&gt;</c> is missing — in which case the
    /// caller treats <c>|</c> as text.
    /// </summary>
    private bool TryScanHookNameRight(int startPos, int startLine, int startCol)
    {
      int look = _pos + 1;
      if (look >= _src.Length || !char.IsLetter(_src[look])) return false;
      int nameStart = look;
      while (look < _src.Length && IsIdContinue(_src[look])) look++;
      if (look >= _src.Length || _src[look] != '>') return false;
      string name = _src.Substring(nameStart, look - nameStart);
      AdvanceN(look - _pos + 1);
      Emit(TokenType.HookNameRight, name, startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a raw HTML tag (<c>&lt;tag…&gt;</c>, <c>&lt;/tag&gt;</c>,
    /// or <c>&lt;tag/&gt;</c>) and emit it verbatim — angle brackets included —
    /// as a single <see cref="TokenType.HtmlTag"/> token. Harlowe permits
    /// inline HTML in passage prose, and downstream rendering re-uses the
    /// original markup. Returns false if the leading <c>&lt;</c> is not
    /// followed by a tag-name letter, or if no closing <c>&gt;</c> is found.
    ///
    /// <para>Quote-state tracking inside the tag body matters: an attribute
    /// like <c>title="1 &gt; 0"</c> contains a literal <c>&gt;</c> that is
    /// not the tag close. Scanning skips characters while inside a single- or
    /// double-quoted attribute value so the right <c>&gt;</c> wins.</para>
    ///
    /// <para>The tag-name lead is restricted to ASCII alpha per WHATWG HTML.
    /// <see cref="char.IsLetter(char)"/> would otherwise accept every Unicode
    /// letter category (CJK, Cyrillic, etc.) and promote prose like
    /// <c>&lt;日本 hi&gt;</c> to an HtmlTag token, swallowing intervening
    /// content as raw markup.</para>
    /// </summary>
    private bool TryScanHtmlTag(int startPos, int startLine, int startCol)
    {
      int look = _pos + 1;
      if (look < _src.Length && _src[look] == '/') look++;
      if (look >= _src.Length || !IsAsciiAlpha(_src[look])) return false;
      char quote = '\0';
      while (look < _src.Length)
      {
        char ch = _src[look];
        if (quote != '\0')
        {
          if (ch == quote) quote = '\0';
        }
        else if (ch == '"' || ch == '\'')
        {
          quote = ch;
        }
        else if (ch == '>')
        {
          break;
        }
        look++;
      }
      if (look >= _src.Length) return false;
      string tag = _src.Substring(_pos, look - _pos + 1);
      AdvanceN(look - _pos + 1);
      Emit(TokenType.HtmlTag, tag, startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Consumes a single- or double-quoted string literal (the opening quote
    /// type is passed in). The emitted <see cref="Token.Value"/> holds the
    /// decoded inner characters — surrounding quotes are stripped and escape
    /// sequences are interpreted. Recognized escapes match the JavaScript
    /// string-literal set the reference Harlowe runtime inherits from its JS
    /// evaluator: <c>\n</c> <c>\r</c> <c>\t</c> <c>\b</c> <c>\f</c> <c>\v</c>
    /// <c>\0</c> <c>\\</c> <c>\'</c> <c>\"</c> <c>\xHH</c> <c>\uHHHH</c>.
    /// Unknown escapes are passed through verbatim — both the backslash and
    /// the following character survive (so <c>\q</c> tokenizes to <c>\q</c>),
    /// which preserves legacy Twee source from other tooling that embeds
    /// literal backslashes (Windows paths, JSON-like blobs).
    /// <c>\xHH</c> and <c>\uHHHH</c> require the right number of hex digits;
    /// an ill-formed hex escape falls back to the same verbatim rule.
    /// An unterminated string is silently closed at end-of-input; a trailing
    /// backslash with no follow-up character is preserved.
    /// </summary>
    private void ScanStringLiteral(char quote, int startPos, int startLine, int startCol)
    {
      Advance();
      var sb = new StringBuilder();
      while (_pos < _src.Length && _src[_pos] != quote)
      {
        char c = _src[_pos];
        if (c == '\\')
        {
          DecodeEscape(sb);
          continue;
        }
        sb.Append(c);
        Advance();
      }
      if (_pos < _src.Length) Advance();
      Emit(TokenType.StringLiteral, sb.ToString(), startPos, startLine, startCol);
    }

    /// <summary>
    /// Decode one escape sequence beginning at the current backslash. Consumes
    /// the backslash plus exactly the characters that belong to the escape
    /// (one for named/quote escapes, three for <c>\xHH</c>, five for
    /// <c>\uHHHH</c>), appending the produced character(s) to
    /// <paramref name="sb"/>. Unknown escapes are passed through verbatim —
    /// both the backslash and the following character survive, which preserves
    /// legacy Twee source from other tooling that embedded literal backslashes
    /// (Windows paths, JSON-like blobs) under a "no escapes" convention.
    /// Ill-formed hex escapes fall back to the same verbatim rule.
    ///
    /// <para>Deliberate divergence from reference Harlowe on <c>\0</c>:
    /// reference (<c>ts/twinescript/runner.ts</c> — see the strict-mode
    /// <c>\0</c> rewrite regex, commented "Remove octal literals (plus the
    /// \0 escape), as they will throw in strict mode") rewrites <c>\0</c>
    /// to the literal ASCII digit <c>'0'</c> before handing the string to
    /// <c>window.eval</c>, because JS strict mode forbids the legacy octal
    /// <c>\0</c> escape. That's a JS-engine workaround, not a designed
    /// language feature — the reference comment calls it out explicitly. We
    /// don't go through <c>window.eval</c>, so we follow the natural
    /// completion of the named-escape table and emit NUL (U+0000) instead.
    /// An author who writes <c>"\0"</c> intending NUL gets NUL in ours; in
    /// reference they silently get <c>'0'</c>. Both are obscure enough that
    /// no realistic Harlowe authoring practice relies on either shape.</para>
    /// </summary>
    private void DecodeEscape(StringBuilder sb)
    {
      Advance(); // past the backslash
      if (_pos >= _src.Length) { sb.Append('\\'); return; } // trailing backslash at EOF: keep it
      char esc = _src[_pos];
      switch (esc)
      {
        case 'n': sb.Append('\n'); Advance(); return;
        case 'r': sb.Append('\r'); Advance(); return;
        case 't': sb.Append('\t'); Advance(); return;
        case 'b': sb.Append('\b'); Advance(); return;
        case 'f': sb.Append('\f'); Advance(); return;
        case 'v': sb.Append('\v'); Advance(); return;
        case '0': sb.Append('\0'); Advance(); return;
        case '\\': sb.Append('\\'); Advance(); return;
        case '\'': sb.Append('\''); Advance(); return;
        case '"': sb.Append('"'); Advance(); return;
        case 'x':
          if (TryDecodeHex(2, out var b))
          {
            sb.Append((char)b);
            AdvanceN(3); // introducer + 2 hex digits
            return;
          }
          // ill-formed \xHH — keep both the backslash and `x` literally
          sb.Append('\\').Append(esc); Advance(); return;
        case 'u':
          if (TryDecodeHex(4, out var u))
          {
            sb.Append((char)u);
            AdvanceN(5); // introducer + 4 hex digits
            return;
          }
          sb.Append('\\').Append(esc); Advance(); return;
        default:
          // unknown escape: keep both the backslash and the next character
          // verbatim so legacy raw-backslash content round-trips intact.
          sb.Append('\\').Append(esc);
          Advance();
          return;
      }
    }

    /// <summary>
    /// Try to read <paramref name="digits"/> hex characters one past the
    /// <c>\x</c> / <c>\u</c> introducer (which sits at <c>_pos</c>). Pure —
    /// does not advance the cursor; the caller advances on success via
    /// <c>AdvanceN(digits + 1)</c>, or via the unknown-escape fallback on
    /// failure. Returns false if the source runs out before <paramref name="digits"/>
    /// hex digits are available, or if a non-hex character appears. Uses
    /// <see cref="NumberStyles.AllowHexSpecifier"/> only (not the composite
    /// <c>HexNumber</c>) so leading/trailing whitespace in the candidate
    /// substring falls back to the unknown-escape rule instead of silently
    /// parsing.
    /// </summary>
    private bool TryDecodeHex(int digits, out int value)
    {
      value = 0;
      int scan = _pos + 1;
      if (scan + digits > _src.Length) return false;
      return int.TryParse(_src.Substring(scan, digits), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Consumes an integer or simple decimal literal (e.g. <c>1</c>, <c>1.5</c>).
    /// A trailing dot without following digits is left for the caller to handle
    /// rather than absorbed. Negative numbers are not handled here — the
    /// leading <c>-</c> is emitted as a separate <see cref="TokenType.Operator"/>
    /// token and the parser builds the unary expression.
    /// </summary>
    private void ScanNumberLiteral(int startPos, int startLine, int startCol)
    {
      int start = _pos;
      while (_pos < _src.Length && IsAsciiDigit(_src[_pos])) Advance();
      if (_pos < _src.Length && _src[_pos] == '.'
          && _pos + 1 < _src.Length && IsAsciiDigit(_src[_pos + 1]))
      {
        Advance();
        while (_pos < _src.Length && IsAsciiDigit(_src[_pos])) Advance();
      }
      Emit(TokenType.NumberLiteral, _src.Substring(start, _pos - start), startPos, startLine, startCol);
    }

    /// <summary>
    /// Consumes a run of identifier characters and classifies the result:
    /// <c>true</c>/<c>false</c> become <see cref="TokenType.BoolLiteral"/>,
    /// known word operators (see <see cref="WordOperators"/>) become
    /// <see cref="TokenType.Operator"/>, and anything else becomes a bare
    /// <see cref="TokenType.Identifier"/>. Before classifying, attempts to fuse
    /// the word with following words into a single multi-word operator (e.g.
    /// <c>is not in</c>, <c>does not contain</c>) via
    /// <see cref="TryFuseMultiWordOperator"/>.
    /// </summary>
    private void ScanIdentifierOrKeyword(int startPos, int startLine, int startCol)
    {
      int start = _pos;
      while (_pos < _src.Length && IsIdContinue(_src[_pos])) Advance();
      string word = _src.Substring(start, _pos - start);

      if (word == "true" || word == "false")
      {
        Emit(TokenType.BoolLiteral, word, startPos, startLine, startCol);
        return;
      }

      if (TryFuseMultiWordOperator(word, startPos, startLine, startCol))
        return;

      // Single-word common-mistake spellings (gt, eq, isa, …). Throw an
      // authoring hint with the correct form before falling through to the
      // generic identifier emit. Reference equivalent: incorrectOperator in
      // ts/markup/patterns.ts plus the runner conversion to an error token.
      if (IncorrectIdentifiers.TryGetValue(word, out var hint))
        throw new HarloweParseException(hint, startLine, startCol);

      // Two-word case: `or a` → "use 'or' instead of 'or a'". `or` is a valid
      // word-operator on its own; we only complain when followed (after
      // whitespace) by a bare `a` that isn't part of a larger construct.
      if (word == "or")
      {
        string next = PeekNextWordFrom(_pos, out int afterNext);
        if (next == "a")
          throw new HarloweParseException(
            "use 'or' instead of 'or a'", startLine, startCol);
      }

      if (WordOperators.Contains(word))
        Emit(TokenType.Operator, word, startPos, startLine, startCol);
      else
        Emit(TokenType.Identifier, word, startPos, startLine, startCol);
    }

    /// <summary>
    /// Tries to fuse <paramref name="firstWord"/> (already consumed; cursor sits
    /// just past it) with following whitespace-separated words into one of
    /// Harlowe's multi-word operators: <c>is not</c>, <c>is in</c>, <c>is a</c>,
    /// <c>is not in</c>, <c>is not a</c>, <c>does not contain</c>, <c>does not match</c>.
    /// Greedy: prefers longer matches (<c>is not in</c> over <c>is not</c>). On
    /// a successful fusion the cursor advances past every consumed word and one
    /// <see cref="TokenType.Operator"/> token is emitted; on no match the cursor
    /// is left where the caller left it and the caller falls through to its
    /// single-word classification.
    /// </summary>
    private bool TryFuseMultiWordOperator(string firstWord, int startPos, int startLine, int startCol)
    {
      if (firstWord == "is")
      {
        string w2 = PeekNextWordFrom(_pos, out int afterW2);
        if (w2 == "not")
        {
          string w3 = PeekNextWordFrom(afterW2, out int afterW3);
          if (w3 == "in" || w3 == "a")
          {
            AdvanceTo(afterW3);
            Emit(TokenType.Operator, "is not " + w3, startPos, startLine, startCol);
            return true;
          }
          AdvanceTo(afterW2);
          Emit(TokenType.Operator, "is not", startPos, startLine, startCol);
          return true;
        }
        if (w2 == "in" || w2 == "a")
        {
          AdvanceTo(afterW2);
          Emit(TokenType.Operator, "is " + w2, startPos, startLine, startCol);
          return true;
        }
        return false;
      }

      if (firstWord == "does")
      {
        string w2 = PeekNextWordFrom(_pos, out int afterW2);
        if (w2 != "not") return false;
        string w3 = PeekNextWordFrom(afterW2, out int afterW3);
        if (w3 == "contain" || w3 == "match")
        {
          AdvanceTo(afterW3);
          Emit(TokenType.Operator, "does not " + w3, startPos, startLine, startCol);
          return true;
        }
        return false;
      }

      return false;
    }

    /// <summary>
    /// Lookahead helper: skips any whitespace starting at <paramref name="from"/>,
    /// then reads a contiguous identifier word. Returns the word (or null if no
    /// identifier is found there). <paramref name="afterPos"/> is set to the
    /// position just past the word on success, or to <paramref name="from"/> on
    /// failure. Does not move the tokenizer cursor.
    /// </summary>
    private string PeekNextWordFrom(int from, out int afterPos)
    {
      int p = from;
      while (p < _src.Length && char.IsWhiteSpace(_src[p])) p++;
      if (p >= _src.Length || !char.IsLetter(_src[p]))
      {
        afterPos = from;
        return null;
      }
      int wordStart = p;
      while (p < _src.Length && IsIdContinue(_src[p])) p++;
      afterPos = p;
      return _src.Substring(wordStart, p - wordStart);
    }

    /// <summary>
    /// Advances the cursor character-by-character to <paramref name="target"/>,
    /// preserving line/column tracking via <see cref="Advance"/>. Used by the
    /// multi-word operator fusion to commit a successful lookahead.
    /// </summary>
    private void AdvanceTo(int target)
    {
      while (_pos < target) Advance();
    }

    /// <summary>
    /// Tries to consume the <c>'s</c> possessive operator (precedence 3). Only
    /// fuses when <c>'</c> is immediately preceded by a value-producing token
    /// with no intervening whitespace, immediately followed by <c>s</c>, and
    /// that <c>s</c> is not part of a longer word. On no match returns false so
    /// the caller falls back to scanning <c>'</c> as a string literal opener.
    /// </summary>
    private bool TryScanPossessive(int startPos, int startLine, int startCol)
    {
      if (_pos == 0) return false;
      if (char.IsWhiteSpace(_src[_pos - 1])) return false;
      if (_pos + 1 >= _src.Length || _src[_pos + 1] != 's') return false;
      if (_pos + 2 < _src.Length && IsIdContinue(_src[_pos + 2])) return false;
      if (_tokens.Count == 0) return false;
      var prev = _tokens[_tokens.Count - 1].Type;
      if (prev != TokenType.Variable && prev != TokenType.TempVariable && prev != TokenType.Identifier
          && prev != TokenType.ParenClose && prev != TokenType.BracketClose && prev != TokenType.MacroClose
          && prev != TokenType.StringLiteral && prev != TokenType.NumberLiteral && prev != TokenType.BoolLiteral
          && prev != TokenType.HookRef)
        return false;
      AdvanceN(2);
      Emit(TokenType.Operator, "'s", startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a hook reference of the form <c>?name</c> — a value used
    /// inside an expression to target a named hook (or a built-in like
    /// <c>?passage</c>/<c>?link</c>). The leading <c>?</c> is dropped from the
    /// emitted token's <see cref="Token.Value"/>. Requires a letter immediately
    /// after the <c>?</c>; a bare <c>?</c> returns false so the caller skips it.
    /// Only reached in expression context — in body prose a <c>?</c> is plain
    /// text.
    /// </summary>
    private bool TryScanHookRef(int startPos, int startLine, int startCol)
    {
      if (_pos + 1 >= _src.Length || !char.IsLetter(_src[_pos + 1])) return false;
      Advance(); // consume '?'
      int nameStart = _pos;
      while (_pos < _src.Length && IsIdContinue(_src[_pos])) Advance();
      Emit(TokenType.HookRef, _src.Substring(nameStart, _pos - nameStart), startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume the <c>2bind</c> two-way bind operator (precedence 6).
    /// Required because <c>2bind</c> begins with a digit and would otherwise
    /// tokenize as <c>NumberLiteral("2") + Identifier("bind")</c>. Matches only
    /// when the literal characters <c>2bind</c> are not followed by another
    /// identifier-continuation char.
    /// </summary>
    private bool TryScanTwoBind(int startPos, int startLine, int startCol)
    {
      if (!MatchString("2bind")) return false;
      int after = _pos + 5;
      if (after < _src.Length && IsIdContinue(_src[after])) return false;
      AdvanceN(5);
      Emit(TokenType.Operator, "2bind", startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume a symbolic operator. Two-character forms
    /// (<c>&lt;=</c>, <c>&gt;=</c>, <c>==</c>, <c>!=</c>) are checked before
    /// the single-character set (<c>+ - * / % &lt; &gt; =</c>) so the longer
    /// match wins. Returns false (without consuming) on any other character so
    /// the caller can advance and continue scanning.
    /// </summary>
    private bool TryScanSymbolOperator(int startPos, int startLine, int startCol)
    {
      if (_pos + 1 < _src.Length)
      {
        string two = _src.Substring(_pos, 2);
        if (two == "<=" || two == ">=" || two == "==" || two == "!=")
        {
          AdvanceN(2);
          Emit(TokenType.Operator, two, startPos, startLine, startCol);
          return true;
        }
        // Reference Harlowe catches reversed forms with a precise hint.
        // See the `incorrectOperator` pattern in ts/markup/patterns.ts.
        if (two == "=<")
          throw new HarloweParseException(
            "use '<=' instead of '=<'", startLine, startCol);
        if (two == "=>")
          throw new HarloweParseException(
            "use '>=' instead of '=>'", startLine, startCol);
      }

      if (_pos + 2 < _src.Length && _src[_pos] == '.' && _src[_pos + 1] == '.' && _src[_pos + 2] == '.')
      {
        AdvanceN(3);
        Emit(TokenType.Operator, "...", startPos, startLine, startCol);
        return true;
      }

      char c = _src[_pos];
      switch (c)
      {
        case '+': case '*': case '/': case '%':
        case '<': case '>':
          Advance();
          Emit(TokenType.Operator, c.ToString(), startPos, startLine, startCol);
          return true;
        case '=':
          // Bare `=` is the documented Harlowe shorthand for `to` — reference
          // ts/markup/patterns.ts defines the `to` pattern as
          // `either('to'+wb, '=')`, so `(set: $x = 5)` is identical to
          // `(set: $x to 5)`. The two-char forms `==`, `<=`, `>=` are
          // handled above and don't reach here.
          Advance();
          Emit(TokenType.Operator, "to", startPos, startLine, startCol);
          return true;
        case '-':
          if (TryScanTypeSuffix(startPos, startLine, startCol)) return true;
          Advance();
          Emit(TokenType.Operator, "-", startPos, startLine, startCol);
          return true;
      }

      return false;
    }

    /// <summary>
    /// Tries to consume an ordinal accessor of the form <c>\d+(st|nd|rd|th)</c>
    /// optionally followed by <c>last</c> (e.g. <c>1st</c>, <c>2nd</c>,
    /// <c>2ndlast</c>). Required because ordinals begin with a digit and would
    /// otherwise tokenize as <see cref="TokenType.NumberLiteral"/> followed by
    /// a separate identifier. Matches only when the full ordinal text is not
    /// followed by another identifier-continuation char, so <c>1stnope</c>
    /// falls through to the number-literal path. The bare keyword <c>last</c>
    /// is not handled here — it's letter-led and tokenizes as a normal
    /// <see cref="TokenType.Identifier"/> through
    /// <see cref="ScanIdentifierOrKeyword"/>.
    /// </summary>
    private bool TryScanOrdinal(int startPos, int startLine, int startCol)
    {
      int look = _pos;
      while (look < _src.Length && IsAsciiDigit(_src[look])) look++;
      if (look == _pos) return false;
      if (look + 1 >= _src.Length) return false;
      string suffix = _src.Substring(look, 2);
      if (suffix != "st" && suffix != "nd" && suffix != "rd" && suffix != "th") return false;
      int end = look + 2;
      if (end + 4 <= _src.Length && _src.Substring(end, 4) == "last")
        end += 4;
      if (end < _src.Length && IsIdContinue(_src[end])) return false;
      string text = _src.Substring(_pos, end - _pos);
      AdvanceN(end - _pos);
      Emit(TokenType.Identifier, text, startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Tries to consume the <c>-type</c> TypedVar suffix (precedence 14). Returns
    /// true when <c>-</c> is immediately followed by the literal word <c>type</c>
    /// (no intervening whitespace; <c>type</c> not followed by an identifier
    /// continuation char). Emits one fused <see cref="TokenType.Operator"/> token
    /// with value <c>"-type"</c>. On no match, returns false so the caller emits
    /// <c>-</c> as binary subtraction or unary minus.
    /// </summary>
    private bool TryScanTypeSuffix(int startPos, int startLine, int startCol)
    {
      if (_pos + 4 >= _src.Length) return false;
      if (_src[_pos + 1] != 't' || _src[_pos + 2] != 'y' || _src[_pos + 3] != 'p' || _src[_pos + 4] != 'e') return false;
      int after = _pos + 5;
      if (after < _src.Length && IsIdContinue(_src[after])) return false;
      AdvanceN(5);
      Emit(TokenType.Operator, "-type", startPos, startLine, startCol);
      return true;
    }

    /// <summary>
    /// Consumes a run of plain prose. Always advances past at least the
    /// current character (which the caller has already determined doesn't
    /// start any markup) and then keeps appending until the next character
    /// that <see cref="IsBodySpecial"/> recognises. Adjacent <c>Text</c>
    /// tokens may result when a markup attempt fails (e.g. <c>(</c> not
    /// followed by a macro name); the parser is expected to merge them.
    /// </summary>
    private void ScanText(int startPos, int startLine, int startCol)
    {
      var sb = new StringBuilder();
      sb.Append(_src[_pos]);
      Advance();

      while (_pos < _src.Length)
      {
        char c = _src[_pos];
        if (IsBodySpecial(c)) break;
        sb.Append(c);
        Advance();
      }

      Emit(TokenType.Text, sb.ToString(), startPos, startLine, startCol);
    }

    /// <summary>
    /// Returns true if <paramref name="c"/> could start a body-mode markup
    /// construct and should therefore terminate a <see cref="ScanText"/> run.
    /// Note this is a coarse filter: a true result only means "stop here and
    /// re-dispatch" — the dispatcher may still fall back to text if the
    /// markup attempt fails.
    /// </summary>
    private static bool IsBodySpecial(char c)
    {
      switch (c)
      {
        case '(': case '[': case ']': case '<': case '|': case '$': case '_': case '\n':
          return true;
        default:
          return false;
      }
    }

    /// <summary>
    /// Returns true if <paramref name="c"/> is valid as a non-leading character
    /// in an identifier, variable name, or hook name (letters, digits, or
    /// underscore).
    /// </summary>
    private static bool IsIdContinue(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// ASCII digit test (<c>'0'</c>–<c>'9'</c>) for number-literal and ordinal
    /// scanning. Deliberately narrower than <see cref="char.IsDigit(char)"/>,
    /// which also matches non-ASCII decimal digits (Arabic-Indic U+0660–0669,
    /// fullwidth U+FF10–FF19, Devanagari, …). Harlowe's number grammar is
    /// ASCII-only (the <c>\d</c> in reference's <c>ts/markup/patterns.ts</c>),
    /// and letting a non-ASCII digit reach a <see cref="TokenType.NumberLiteral"/>
    /// token would make the parser's <c>double.Parse(…, InvariantCulture)</c>
    /// throw a <c>FormatException</c> — which escapes the loaders (they catch
    /// only <c>HarloweParseException</c>) and aborts the whole story load,
    /// violating the in-prose-errors-never-exceptions policy. With this gate the
    /// digit falls through to identifier/text handling instead.
    /// </summary>
    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    /// <summary>
    /// ASCII-alpha test used for the HTML tag-name lead. WHATWG HTML restricts
    /// element names to ASCII letters; <see cref="char.IsLetter(char)"/> would
    /// also match Unicode letter categories and let non-HTML prose get scooped
    /// up as a single HtmlTag token.
    /// </summary>
    private static bool IsAsciiAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>
    /// Returns the character at <c>_pos + offset</c>, or <c>'\0'</c> if that
    /// position is past end-of-input. Does not advance the cursor.
    /// </summary>
    private char Peek(int offset = 0)
    {
      int i = _pos + offset;
      return i < _src.Length ? _src[i] : '\0';
    }

    /// <summary>
    /// Consumes one character. Increments line and resets column on a newline,
    /// otherwise increments the column. Caller must guarantee <c>_pos</c> is
    /// in range.
    /// </summary>
    private void Advance()
    {
      char c = _src[_pos];
      _pos++;
      if (c == '\n') { _line++; _col = 1; }
      else { _col++; }
    }

    /// <summary>
    /// Consumes <paramref name="n"/> characters, preserving line/column
    /// tracking by routing through <see cref="Advance"/>.
    /// </summary>
    private void AdvanceN(int n)
    {
      for (int i = 0; i < n; i++) Advance();
    }

    /// <summary>
    /// Returns true if the source from the current position starts with
    /// <paramref name="s"/>. Does not advance the cursor and is safe near
    /// end-of-input.
    /// </summary>
    private bool MatchString(string s)
    {
      if (_pos + s.Length > _src.Length) return false;
      for (int i = 0; i < s.Length; i++)
        if (_src[_pos + i] != s[i]) return false;
      return true;
    }

    /// <summary>
    /// Appends a token to the output list. The position arguments are the
    /// start of the token, captured before any consumption — not the current
    /// cursor — so error messages point at the token's first character.
    /// </summary>
    private void Emit(TokenType type, string value, int pos, int line, int col)
    {
      _tokens.Add(new Token(type, value, pos, line, col));
    }

    /// <summary>
    /// One entry on the mode stack. <c>Mode</c> is the lexer state;
    /// <c>ParenDepth</c> counts unmatched grouping <c>(</c>s inside an
    /// Expression frame so a closing <c>)</c> can be disambiguated as
    /// <see cref="TokenType.ParenClose"/> (depth &gt; 0) vs
    /// <see cref="TokenType.MacroClose"/> (depth == 0, pop the frame).
    /// </summary>
    private class Frame
    {
      public TokenizerMode Mode;
      public int ParenDepth;
    }
  }

  /// <summary>
  /// Internal lexer state. Body mode emits prose/markup tokens; Expression
  /// mode emits literals/operators/etc. and is entered between a macro's
  /// opening colon and matching close paren.
  /// </summary>
  internal enum TokenizerMode
  {
    Body,
    Expression
  }
}
