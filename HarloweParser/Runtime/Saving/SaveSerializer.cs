using System.Collections.Generic;
using Harlowe.Ast.Expression;
using Harlowe.Parsing;
using Harlowe.Tokens;
using Harlowe.Twee;

namespace Harlowe.Runtime.Saving
{
  /// <summary>
  /// Converts Harlowe save state to and from a JSON blob. Two levels: a single
  /// <see cref="HarloweValue"/> ↔ Harlowe source (<see cref="Serialise(HarloweValue)"/>
  /// / <see cref="Deserialise(string, MacroRegistry, MacroContext)"/>), and a whole
  /// timeline ↔ blob (<see cref="SerialiseTimeline"/> / <see cref="DeserialiseTimeline"/>).
  /// Source-based, matching reference Harlowe's variable store (<c>varscope.ts</c>
  /// saves each variable as <c>toSource(value)</c> and re-<c>eval</c>s it on load)
  /// rather than a tagged-JSON encoding; the blob layer reuses the project's
  /// <see cref="JsonWriter"/>/<see cref="JsonReader"/>.
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
    ///
    /// <para>Re-lexed under <see cref="HarloweProfile.SaveFormat"/>, never the
    /// story's profile: the source came from <see cref="HarloweValue.ToSource"/>,
    /// so it is engine-emitted and author compatibility policy has no bearing
    /// on it. A blob must read back the same way it was written, whatever
    /// major the story later declares.</para>
    /// </summary>
    public static HarloweValue Deserialise(string source, MacroRegistry registry, MacroContext context)
    {
      if (source == null) return HarloweValue.OfError("missing save value");
      if (registry == null) return HarloweValue.OfError("no macro registry for deserialisation");

      IExpressionNode node;
      try
      {
        var tokens = new HarloweTokenizer(HarloweProfile.SaveFormat).Tokenize("(v:" + source + ")");
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
        var evaluator = new ExpressionEvaluator(context?.Store, context?.EvaluationContext, registry, context?.Rng);
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

    // ===== Timeline ↔ blob =====

    /// <summary>
    /// Serialise a timeline (completed turns <paramref name="past"/> then the live
    /// <paramref name="present"/>) to a JSON blob: a <see cref="SaveBlobVersion"/>
    /// wrapper around a moments array. The redo future is deliberately excluded — it
    /// lies ahead of the present and isn't part of saved history. Each moment with no
    /// variable changes, redirect trail, or recorded RNG state compresses to a bare
    /// passage-name string (reference's <c>#isEmpty()</c>); otherwise it's an object.
    /// Returns <c>null</c> if any stored value has no source form (a non-finite
    /// number, an unstamped changer, …) so the caller can fail the save loudly.
    /// </summary>
    public static string SerialiseTimeline(IReadOnlyList<Moment> past, Moment present)
    {
      var moments = new List<object>();
      if (past != null)
        for (int i = 0; i < past.Count; i++)
        {
          var m = SerialiseMoment(past[i]);
          if (m == null) return null;
          moments.Add(m);
        }
      if (present != null)
      {
        var m = SerialiseMoment(present);
        if (m == null) return null;
        moments.Add(m);
      }

      var root = new Dictionary<string, object>
      {
        { "version", (long)SaveBlobVersion.Current },
        { "moments", moments },
      };
      return new JsonWriter().Write(root);
    }

    /// <summary>One moment → a compressed passage-name string or a full object; null if a value can't be serialised.</summary>
    private static object SerialiseMoment(Moment m)
    {
      bool hasVars = m.StoreDelta != null && m.StoreDelta.Count > 0;
      bool hasVisits = m.Visits != null && m.Visits.Count > 0;
      bool hasSeed = m.Seed != null;
      bool hasSeedIter = m.SeedIter.HasValue;

      if (!hasVars && !hasVisits && !hasSeed && !hasSeedIter)
        return m.PassageName ?? string.Empty;

      var obj = new Dictionary<string, object> { { "passage", m.PassageName ?? string.Empty } };
      if (hasVars)
      {
        var vars = new Dictionary<string, object>();
        foreach (var kv in m.StoreDelta)
        {
          string src = Serialise(kv.Value);
          if (src == null) return null; // value has no source form → fail the whole save
          vars[kv.Key] = src;
        }
        obj["vars"] = vars;
      }
      if (hasVisits)
      {
        var visits = new List<object>(m.Visits.Count);
        for (int i = 0; i < m.Visits.Count; i++) visits.Add(m.Visits[i]);
        obj["visits"] = visits;
      }
      if (hasSeed) obj["seed"] = m.Seed;
      if (hasSeedIter) obj["seedIter"] = (long)m.SeedIter.Value;
      return obj;
    }

    /// <summary>
    /// Parse a blob back into a timeline, validating against <paramref name="story"/>
    /// (every referenced passage must still exist) and re-evaluating each saved value
    /// through <paramref name="registry"/>/<paramref name="context"/>. Atomic: a
    /// malformed blob, a newer format version, a vanished passage, or an
    /// un-restorable value yields a <see cref="DeserialiseResult"/> with
    /// <see cref="DeserialiseResult.Error"/> set and nothing half-built. On success
    /// the blob's last moment is the <see cref="DeserialiseResult.Present"/> and the
    /// rest are <see cref="DeserialiseResult.Past"/>.
    /// </summary>
    public static DeserialiseResult DeserialiseTimeline(string blob, Harlowe story, MacroRegistry registry, MacroContext context)
    {
      if (blob == null) return Fail("empty save data");
      if (story == null) return Fail("no story to load into");

      object root;
      try { root = new JsonReader().Read(blob); }
      catch (HarloweParseException ex) { return Fail("corrupt save data: " + ex.Message); }

      if (!(root is Dictionary<string, object> dict)) return Fail("save data is not an object");
      if (!dict.TryGetValue("version", out var vObj) || !(vObj is double vNum))
        return Fail("save data has no version");
      // Compare the double directly — (int)vNum on an out-of-range value (e.g. 1e20)
      // is unspecified in C# and can wrap to a value that slips past this gate.
      if (vNum > SaveBlobVersion.Current)
        return Fail($"save data is from a newer version ({HarloweValue.FormatNumber(vNum)} > {SaveBlobVersion.Current})");
      if (!dict.TryGetValue("moments", out var mObj) || !(mObj is List<object> momentList) || momentList.Count == 0)
        return Fail("save data has no moments");

      var moments = new List<Moment>(momentList.Count);
      for (int i = 0; i < momentList.Count; i++)
      {
        var parsed = DeserialiseMoment(momentList[i], story, registry, context, out string error);
        if (error != null) return Fail(error);
        moments.Add(parsed);
      }

      var result = new DeserialiseResult
      {
        Present = moments[moments.Count - 1],
        Past = new List<Moment>(moments.Count - 1),
      };
      for (int i = 0; i < moments.Count - 1; i++) result.Past.Add(moments[i]);
      return result;
    }

    /// <summary>One blob moment → a <see cref="Moment"/>, or null with <paramref name="error"/> set on any validation/value failure.</summary>
    private static Moment DeserialiseMoment(object raw, Harlowe story, MacroRegistry registry, MacroContext context, out string error)
    {
      error = null;

      if (raw is string compressedPassage)
      {
        if (story.GetPassage(compressedPassage) == null)
        { error = $"saved passage '{compressedPassage}' no longer exists"; return null; }
        return new Moment { PassageName = compressedPassage };
      }

      if (!(raw is Dictionary<string, object> obj)) { error = "malformed moment in save data"; return null; }

      if (!obj.TryGetValue("passage", out var pObj) || !(pObj is string passage))
      { error = "moment is missing its passage name"; return null; }
      if (story.GetPassage(passage) == null)
      { error = $"saved passage '{passage}' no longer exists"; return null; }

      var moment = new Moment { PassageName = passage };

      if (obj.TryGetValue("vars", out var varsObj))
      {
        if (!(varsObj is Dictionary<string, object> varsDict)) { error = "moment variables are malformed"; return null; }
        moment.StoreDelta = new Dictionary<string, HarloweValue>();
        foreach (var kv in varsDict)
        {
          if (!(kv.Value is string src)) { error = $"variable '{kv.Key}' is not a source string"; return null; }
          var value = Deserialise(src, registry, context);
          if (value.IsError) { error = $"could not restore variable '{kv.Key}': {value.ErrorMessage}"; return null; }
          moment.StoreDelta[kv.Key] = value;
        }
      }

      if (obj.TryGetValue("visits", out var visitsObj))
      {
        if (!(visitsObj is List<object> visitsList)) { error = "moment redirect trail is malformed"; return null; }
        moment.Visits = new List<string>(visitsList.Count);
        for (int i = 0; i < visitsList.Count; i++)
        {
          if (!(visitsList[i] is string v)) { error = "redirect trail entry is not a passage name"; return null; }
          if (story.GetPassage(v) == null) { error = $"saved passage '{v}' no longer exists"; return null; }
          moment.Visits.Add(v);
        }
      }

      if (obj.TryGetValue("seed", out var seedObj))
      {
        if (!(seedObj is string seed)) { error = "moment RNG seed is malformed"; return null; }
        moment.Seed = seed;
      }
      if (obj.TryGetValue("seedIter", out var iterObj))
      {
        if (!(iterObj is double iterNum)) { error = "moment RNG position is malformed"; return null; }
        // A draw count is a non-negative int; reject out-of-range before the cast
        // (unspecified for an out-of-range double) so a corrupt blob can't feed
        // garbage into the RNG restore.
        if (iterNum < 0 || iterNum > int.MaxValue) { error = "moment RNG position is out of range"; return null; }
        moment.SeedIter = (int)iterNum;
      }

      return moment;
    }

    private static DeserialiseResult Fail(string error) => new DeserialiseResult { Error = error };
  }
}
