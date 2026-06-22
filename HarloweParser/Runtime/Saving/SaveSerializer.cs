using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Tokens;

namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Converts a single <see cref="HarloweValue"/> to and from Harlowe source — the
  /// value-level half of the save model (the timeline/blob serialiser builds on
  /// this). Source-based, matching reference Harlowe's variable store
  /// (<c>varscope.ts</c> saves each variable as <c>toSource(value)</c> and
  /// re-<c>eval</c>s it on load) rather than a tagged-JSON encoding.
  /// </summary>
  public static class SaveSerializer
  {
    /// <summary>
    /// Value → Harlowe source, or <c>null</c> when the value has no source form — a
    /// non-finite number, an <see cref="HarloweValueKind.Error"/>, or a Changer whose
    /// source was never stamped (see <see cref="HarloweValue.ToSource"/>). A caller
    /// fails the save loudly on null rather than emitting unparseable source.
    /// </summary>
    public static string Serialise(HarloweValue value) => value?.ToSource();

    /// <summary>
    /// Source → value: re-lex, parse, and evaluate the source back to a value.
    /// <paramref name="registry"/> supplies the macros that collection / changer
    /// source (<c>(a:…)</c>, <c>(dm:…)</c>, <c>(text-style:…)</c>, …) dispatches
    /// through; its <see cref="MacroRegistry.Context"/> is set to
    /// <paramref name="context"/> for the evaluation — the evaluator dispatches nested
    /// macro calls through the registry's context-less <c>Invoke</c>, which requires a
    /// non-null Context — and restored afterwards. A malformed or un-evaluable source
    /// yields an <see cref="HarloweValueKind.Error"/> value rather than throwing, so
    /// the caller surfaces it (our in-prose error policy).
    ///
    /// <para>The source is wrapped in a throwaway macro call so the tokenizer enters
    /// expression mode — top-level values like <c>"x"</c> or <c>42</c> lex as prose
    /// otherwise. The wrapper's name is consumed before argument parsing and never
    /// invoked; its single argument is the value expression.</para>
    /// </summary>
    public static HarloweValue Deserialise(string source, MacroRegistry registry, MacroContext context)
    {
      if (source == null) return HarloweValue.OfError("missing save value");
      if (registry == null) return HarloweValue.OfError("no macro registry for deserialisation");

      IExpressionNode node;
      try
      {
        var tokens = new HarloweTokenizer().Tokenize("(v:" + source + ")");
        var cursor = new TokenCursor(tokens);
        cursor.Advance(); // consume the wrapper's MacroOpen so the parser is in expression mode
        var args = new HarloweExpressionParser().ParseArgumentList(cursor, false);
        if (args.Count != 1)
          return HarloweValue.OfError("save value did not parse to a single expression");
        node = args[0];
      }
      catch (HarloweParseException ex)
      {
        return HarloweValue.OfError("malformed save value: " + ex.Message);
      }

      var prior = registry.Context;
      registry.Context = context;
      try
      {
        var evaluator = new ExpressionEvaluator(context?.Store, context?.EvaluationContext, registry);
        return evaluator.Evaluate(node);
      }
      catch (System.Exception ex)
      {
        // The evaluator is meant to return Error values, never throw — but this is the
        // load boundary re-evaluating possibly-tampered blob source, so degrade any
        // unexpected throw (a buggy macro, or a var-ref behind a null store) to one
        // in-prose error rather than letting it crash the whole load. Mirrors the
        // parse guard above, honouring the non-throwing contract end to end.
        return HarloweValue.OfError("could not evaluate save value: " + ex.Message);
      }
      finally
      {
        registry.Context = prior;
      }
    }
  }
}
