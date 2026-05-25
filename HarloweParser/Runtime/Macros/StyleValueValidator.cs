namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Validates author-supplied strings before they land in a value-bearing
  /// <see cref="StyleSpec"/> field (Color, BackgroundColor, BackgroundImage,
  /// FontFamily, FontSize). Rejects characters that have structural meaning in
  /// CSS — <c>;</c> (declaration separator), <c>{</c> / <c>}</c> (rule blocks),
  /// <c>\</c> (CSS string escapes), newline characters — to keep a story
  /// variable from injecting extra declarations into the
  /// <c>style="..."</c> attribute the HTML adapter emits. Also blocks a small
  /// set of payload-level CSS feature keywords that have a history of being
  /// abused for code execution: <c>expression(</c> (IE6/7 legacy),
  /// <c>javascript:</c>, <c>vbscript:</c>, <c>behavior:</c>, <c>binding:</c>.
  /// Modern browsers ignore them, but non-browser CSS consumers
  /// (offline tools, legacy renderers, email clients) might still execute, and
  /// keeping the denylist makes the security boundary explicit.
  ///
  /// <para>Macro layer is the right place to reject: it gives the author a
  /// clear in-prose error pointing at the offending macro, instead of silently
  /// emitting bad CSS or stripping characters the author meant to use. Macros
  /// that accept arbitrary strings (<c>(text-color:)</c>, <c>(background:)</c>,
  /// <c>(font:)</c>) call <see cref="Validate"/>; macros taking typed values
  /// (<c>(text-size:)</c> from a Number, <c>(opacity:)</c>, <c>(align:)</c>)
  /// don't need it.</para>
  /// </summary>
  internal static class StyleValueValidator
  {
    /// <summary>
    /// Case-insensitively flagged substrings. <c>expression(</c> needs the open
    /// paren so it doesn't catch a hex colour like <c>#express123</c>; the
    /// scheme entries need the trailing colon so they don't trip on the words
    /// alone.
    /// </summary>
    private static readonly string[] DeniedKeywords = new[]
    {
      "expression(", "javascript:", "vbscript:", "behavior:", "binding:",
    };

    /// <summary>
    /// Returns null when <paramref name="value"/> is safe to embed in a CSS
    /// declaration; otherwise returns a ready-to-return error
    /// <see cref="HarloweValue"/> mentioning the macro name and the offending
    /// character or keyword (so the in-prose message tells the author what to
    /// fix). Returning the error value (rather than throwing) keeps the macro
    /// on the project's in-prose error contract.
    /// </summary>
    public static HarloweValue Validate(string macroName, string value)
    {
      if (value == null) return null;

      // Normalize once and run BOTH checks against the normalized form. Without
      // this, the structural-char pass sees only raw ASCII boundary chars and
      // misses NFKC-equivalent variants like full-width `；` (U+FF1B → ASCII
      // `;`) — a value the keyword pass *would* normalize but doesn't have a
      // matching denylist entry for. With shared normalization, structural
      // separators are blocked regardless of which Unicode form the author
      // wrote them in, matching the keyword pass's existing coverage.
      //
      // Normalize can throw ArgumentException when the string contains an
      // unpaired surrogate (e.g. a `\uD800` escape that the tokenizer decoded
      // verbatim). Reject — falling back to the raw value would skip the
      // NFKC-equivalence defense entirely, letting an attacker pair an
      // unpaired surrogate with a full-width separator like `；` (U+FF1B) to
      // smuggle a `;` past the structural-char check.
      string normalized;
      try
      {
        normalized = value.Normalize(System.Text.NormalizationForm.FormKC);
      }
      catch (System.ArgumentException)
      {
        return HarloweValue.OfError(
          $"({macroName}:) value contains malformed Unicode (an unpaired surrogate)");
      }

      for (int i = 0; i < normalized.Length; i++)
      {
        char c = normalized[i];
        if (c == ';' || c == '{' || c == '}' || c == '\\' || c == '\n' || c == '\r')
        {
          return HarloweValue.OfError(
            $"({macroName}:) value contains a character that isn't allowed in a style value: '{Describe(c)}'");
        }
      }

      // Keyword check on the lowercased normalized copy. NFKC folds
      // compatibility variants — full-width Latin (Ｊ → j), ligatures (ﬁ → fi),
      // Roman numeral characters (Ⅰ → I), half-width katakana — so an attacker
      // can't bypass the ASCII denylist by substituting any of those for the
      // canonical ASCII spelling. ToLowerInvariant then absorbs `JaVaScRiPt:`
      // mixing.
      //
      // What this does NOT catch: cross-script confusables (Cyrillic 'а'
      // U+0430, Greek 'ο' U+03BF) — those don't decompose to Latin under
      // NFKC. We don't expand to a full Unicode-confusables map here because
      // (a) the browser is the actual interpreter of CSS values and won't
      // recognise `аvascript:` as the `javascript:` URL scheme either, and
      // (b) the structural-char check above blocks the boundary chars that
      // a confusable would need to escape the declaration. The denylist is
      // defense-in-depth against ASCII-keyword payloads; cross-script attacks
      // are out of scope here and stop at the browser layer.
      string lower = normalized.ToLowerInvariant();
      for (int i = 0; i < DeniedKeywords.Length; i++)
      {
        if (lower.IndexOf(DeniedKeywords[i], System.StringComparison.Ordinal) >= 0)
        {
          return HarloweValue.OfError(
            $"({macroName}:) value contains a disallowed CSS keyword: '{DeniedKeywords[i]}'");
        }
      }
      return null;
    }

    private static string Describe(char c)
    {
      switch (c)
      {
        case '\n': return "\\n";
        case '\r': return "\\r";
        default: return c.ToString();
      }
    }
  }
}
