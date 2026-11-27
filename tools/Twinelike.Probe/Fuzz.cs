using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using Harlowe;
using Harlowe.Runtime;
using Harlowe.Twee;

namespace Twinelike.Probe
{
  /// <summary>
  /// Mutates known passages and runs each result through the whole host surface:
  /// load, diagnostics, Twee write, render, clicks, ticks, navigation, undo, redo,
  /// save and load. Nothing should throw and nothing should take ten seconds.
  /// </summary>
  internal static class Fuzz
  {
    private const string Header = ":: StoryTitle\nProbe\n\n:: StoryData\n{\"ifid\":\"00000000-0000-4000-8000-000000000000\",\"format\":\"Harlowe\",\"format-version\":\"3.3.9\",\"start\":\"Start\"}\n\n";

    private static string Source(string body) => Header + ":: Start\n" + body + "\n\n:: P2\nSECOND\n";

    private static readonly string[] Tokens =
    {
      "(", ")", "[", "]", "[[", "]]", "->", "<-", "|", "\"", "'", "'s ", " of ", "...", "$x", "_t", "?a", "?page", " it ", "pos",
      "(set: ", " to ", " into ", "(if: ", "(else:)", "(for: each _i, ", "(a: ", "(dm: ", "(print: ", "(display: \"Start\")",
      "(goto: \"P2\")", "(click: ?a)", "(link: \"l\")", "(replace: ?a)", "(enchant: ?a, ", " where ", " via ", " making ", " each ",
      ",", ":", "//", "''", "~~", "^^", "--", "<!--", "-->", "\\", "\n", "{", "}", "0", "-1", "1e308", "\"\"", "?link",
      "(load-game: \"a\")", "(save-game: \"a\")", "(undo:)", "(move: ", "(unpack: ", "(find: ", "(altered: ", "(folded: ",
      "[=", "[==", "|a>[=", "(if: true)[=", "(if: false)[==", "<a|", "|a>", "{=", "{==", "...$x", "...(a: 1, 2)", "(set: $x to \"$x $x\")$x", "(print: \"{ a  [[b->P2]] }\")", "(print: $x)",
      "(print: \"''b'' (print: 1)\")", "(click: \"a\")[ b  c ]", "(replace: \"a\")[ b  c ]", "(append: ?a)[ b  c ]",
      "*", "**", "***", "`", "``", "```", "---", "\n---\n", "\n* ", "*a*", "**b**", "`a [b] c`", "``a`b``", "(print: \"*p* `q`\")", "]<a|", "(enchant: \"a\", (text-style: \"bold\"))",
      "(macro: ", "(macro: [", "num-type ", "str-type _a, ", "...num-type _b, ", "const-type $c", "any-type _d", "(a: num)-type $e", "-type ",
      "(output:)", "(out:)[", "(output-data: ", "(out-data: 1)", "(error: \"e\")", "(partial: \"max\", 2)", "(partial: $m, ", "($m: ", "($m:)", "(_m: ", "(_m:)",
      "(set: $m to (macro: num-type _n, [(if: _n > 0)[(output-data: ($m: _n - 1))](output-data: 0)]))($m: 50)",
      "(set: $m to (macro: [(output:)[($m:)]]))($m:)",
      "(set: $m to (macro: [(output-data: ($m:) + ($m:))]))($m:)",
      "(set: _c to (macro: [(output:)[(link: \"l\")[(_c:)]]]))(_c:)",
      "(set: $c to ($m:))$c$c", "(text-style: \"bold\")($m:)", "(set: num-type $x to 1)", "(unpack: (a: 1, 2) into (a: num-type _a, ...num-type _b))",
      "|a)[", "](a|", "|a)[=", "|b)[x]", "[y](b|", "(show: ?a)", "(show: ?a, ?b)", "(show: ?a's 1st)", "(hidden:)", "(hidden:)|a>[", "(hidden:)+(text-style: \"bold\")",
      "(text-style: \"bold\")(show: ?a)", "(link: \"l\")[(show: ?a)]", "(click: ?a)[(show: ?b)]", "(set: $x to (show: ?a))$x", "(set: $x to \"|a)[$x$x(show: ?a)]\")$x(show: ?a)",
      "(ds: ", "(ds:)", "(ds: 1, 2)", "(dataset: \"a\", (a: 1))", "(ds: ...num)", "(ds: 1, num)", " is a ds", "...(ds: 3, 1, 2)", "(set: $x to (ds: $x))", "(set: $x to $x + (ds: 1))",
      "(range: ", "(range: 1, 5)", "...(range: 1, 3)", "(range: 1, 200000)", "(range: 1, 1e300)", "(passage:)", "(passage: \"P2\")", "(passage: \"Start\")'s source", "(print: (passage:)'s source)", "(passage:)'s tags",
      "(for: each _i, ...(range: 1, 200))[", "(for: each _i, ...(range: 1, 2000))[(set: $x to (a: $x))]$x", "(for: each _i, ...(range: 1, 2000))[(set: $x to (ds: $x))]$x", "(print: (ds: ...(range: 1, 50)) matches (ds: ...num, ...(range: 1, 40), \"x\"))",
    };

    private static int _rendered, _inProseErrors, _dispatched;

    /// <summary>
    /// Runs up to <paramref name="count"/> mutated stories, for at most 150 seconds
    /// or until three hang, seeded from every passage of the feature-coverage story
    /// and every snippet in <paramref name="extra"/> case files.
    /// </summary>
    public static int Run(string featureCoverage, string report, int count, int seed, List<string> extra)
    {
      var bodies = new List<string>();
      foreach (var p in new TweeReader().Read(File.ReadAllText(featureCoverage)).Passages)
        if (!string.IsNullOrEmpty(p.RawBody)) bodies.Add(p.RawBody);
      foreach (var f in extra) AddSources(bodies, f);

      var rng = new Random(seed);
      var groups = new Dictionary<string, (int n, string example, string msg)>();
      var order = new List<string>();
      var hangs = new List<string>();
      var clock = Stopwatch.StartNew();
      string lastPath = report + ".last-case.txt";
      int ran = 0;
      for (; ran < count && clock.Elapsed.TotalSeconds < 150 && hangs.Count < 3; ran++)
      {
        string mutated = Mutate(bodies[rng.Next(bodies.Count)], rng);
        string source = Source(mutated);
        if (ran % 25 == 0) File.WriteAllText(lastPath, mutated);
        Exception caught = null;
        var t = new Thread(() =>
        {
          try { Exercise(source); }
          catch (Exception e) { caught = e; }
        }, 4 * 1024 * 1024) { IsBackground = true };
        t.Start();
        if (!t.Join(10000)) { hangs.Add(mutated); continue; }
        if (caught == null) continue;
        string key = Key(caught);
        if (groups.TryGetValue(key, out var g))
          groups[key] = (g.n + 1, mutated.Length < g.example.Length ? mutated : g.example, g.msg);
        else
        {
          groups[key] = (1, mutated, caught.Message);
          order.Add(key);
        }
      }

      var o = new StringBuilder();
      o.AppendLine($"mutated stories run: {ran} from {bodies.Count} passages in {clock.Elapsed.TotalSeconds:F0}s");
      o.AppendLine($"harness check: {_rendered} rendered non-empty, {_inProseErrors} showed an in-prose error, {_dispatched} click dispatches");
      int total = 0;
      foreach (var kv in groups) total += kv.Value.n;
      o.AppendLine($"uncaught exceptions: {total} in {groups.Count} distinct places; hangs over 10s: {hangs.Count}");
      foreach (var key in order)
      {
        var v = groups[key];
        o.AppendLine();
        o.AppendLine($"{v.n,5}  {key}");
        o.AppendLine("       " + v.msg);
        var ex = v.example.Replace("\r", "").Replace("\n", "⏎");
        o.AppendLine("       " + (ex.Length > 220 ? ex.Substring(0, 220) + "…" : ex));
      }
      foreach (var h in hangs)
      {
        var ex = h.Replace("\r", "").Replace("\n", "⏎");
        o.AppendLine();
        o.AppendLine("HANG   " + (ex.Length > 300 ? ex.Substring(0, 300) + "…" : ex));
      }
      File.WriteAllText(report, o.ToString(), new UTF8Encoding(false));
      return total == 0 && hangs.Count == 0 ? 0 : 1;
    }

    /// <summary>Snippets from a case file: an array of strings, or of objects with a <c>source</c>.</summary>
    private static void AddSources(List<string> bodies, string file)
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(file));
      foreach (var item in doc.RootElement.EnumerateArray())
      {
        if (item.ValueKind == JsonValueKind.String) bodies.Add(item.GetString());
        else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String)
          bodies.Add(s.GetString());
      }
    }

    /// <summary>Runs one saved case, with <c>⏎</c> for each line break, and reports how long it took.</summary>
    public static int TimeCase(string file)
    {
      string source = Source(File.ReadAllText(file).Replace("⏎", "\n"));
      var watch = Stopwatch.StartNew();
      Exception caught = null;
      var t = new Thread(() =>
      {
        try { Exercise(source); }
        catch (Exception e) { caught = e; }
      }, 4 * 1024 * 1024) { IsBackground = true };
      t.Start();
      bool done = t.Join(30000);
      Console.WriteLine($"{(done ? "finished" : "still running")} after {watch.ElapsedMilliseconds} ms{(caught != null ? ", threw " + caught.GetType().Name : "")}");
      return done && caught == null ? 0 : 1;
    }

    private static string Mutate(string s, Random r)
    {
      int ops = 1 + r.Next(4);
      var b = new StringBuilder(s);
      for (int i = 0; i < ops; i++)
      {
        int at = r.Next(b.Length + 1);
        switch (r.Next(3))
        {
          case 0:
            b.Remove(at, Math.Min(1 + r.Next(12), b.Length - at));
            break;
          case 1:
            b.Insert(at, Tokens[r.Next(Tokens.Length)]);
            break;
          default:
            int len = Math.Min(1 + r.Next(24), b.Length - at);
            b.Insert(r.Next(b.Length + 1), b.ToString(at, len));
            break;
        }
      }
      return b.ToString();
    }

    private static void Exercise(string source)
    {
      var story = new TweeReader().Read(source);
      story.GetBrokenLinks();
      story.GetParseErrors();
      new TweeWriter().Write(story);
      var session = new StorySession(story, 12345);
      var result = session.Render();
      if (result != null && result.Entries != null && result.Entries.Count > 0) Interlocked.Increment(ref _rendered);
      if (result != null && result.Entries != null)
        foreach (var e in result.Entries)
          if (e.Kind == BufferedRenderOutput.Kind.Error) { Interlocked.Increment(ref _inProseErrors); break; }
      for (int round = 0; round < 3 && result != null && result.Entries != null; round++)
      {
        string id = null;
        foreach (var e in result.Entries)
          if (e.Kind == BufferedRenderOutput.Kind.BeginInteractive) { id = e.Region.Id; break; }
        if (id == null) break;
        Interlocked.Increment(ref _dispatched);
        result = session.DispatchEvent(id);
      }
      // Lets (live:) and (after:) hooks render: a first frame, then a few long
      // ones, every other one with a key held.
      for (int frame = 0; frame < 6 && session.HasLiveHooks; frame++)
      {
        var ticked = session.Tick(frame == 0 ? 16 : 150, frame % 2 == 1);
        if (ticked != null) result = ticked;
      }
      session.Goto("P2");
      session.Undo();
      session.Render();
      session.Redo();
      session.SaveGame("a");
      if (session.LoadGame("a")) session.Render();
    }

    private static string Key(Exception e)
    {
      string frame = "?";
      foreach (var f in new StackTrace(e, false).GetFrames() ?? Array.Empty<StackFrame>())
      {
        var m = f.GetMethod();
        var ns = m?.DeclaringType?.Namespace;
        if (ns != null && ns.StartsWith("Harlowe", StringComparison.Ordinal)) { frame = m.DeclaringType.Name + "." + m.Name; break; }
      }
      return e.GetType().Name + " @ " + frame;
    }
  }
}
