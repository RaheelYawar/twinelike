using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Harlowe;
using Harlowe.Runtime;

namespace Twinelike.Probe
{
  /// <summary>
  /// Replays the checks <c>specs extract</c> pulled out of reference Harlowe's
  /// Jasmine specs, under one profile. Writes <c>results-&lt;label&gt;.dat</c>
  /// (one row per check, keyed so two runs compare check by check) and a
  /// summary.
  /// </summary>
  internal static class Specs
  {
    private sealed class Outcome
    {
      public string File, Test, Source, Expected, Actual, Kind, Error;
      public bool Passed;
    }

    public static int Run(string input, string version, string outDir, string label)
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(input));
      var outcomes = new List<Outcome>();
      int testsRun = 0, hung = 0, crashedTests = 0;
      var clock = Stopwatch.StartNew();

      foreach (var test in doc.RootElement.EnumerateArray())
      {
        if (test.GetProperty("checks").GetInt32() == 0) continue;
        testsRun++;
        var local = new List<Outcome>();
        Exception crash = null;
        var thread = new Thread(() =>
        {
          try { RunTest(test, version, local); }
          catch (Exception e) { crash = e; }
        }, 8 * 1024 * 1024) { IsBackground = true };
        thread.Start();
        if (!thread.Join(5000)) { hung++; Console.Error.WriteLine("HUNG: " + test.GetProperty("title").GetString()); continue; }
        if (crash != null)
        {
          crashedTests++;
          local.Add(new Outcome
          {
            File = test.GetProperty("file").GetString(), Test = test.GetProperty("title").GetString(),
            Kind = "exception", Error = crash.GetType().Name + ": " + crash.Message, Passed = false,
          });
        }
        outcomes.AddRange(local);
      }

      int passed = 0;
      foreach (var o in outcomes) if (o.Passed) passed++;
      var summary = new StringBuilder();
      summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
        "profile {0}: tests {1}, checks {2}, passed {3} ({4:F1}%), hung {5}, tests that threw {6}, {7:F1}s",
        version, testsRun, outcomes.Count, passed, 100.0 * passed / Math.Max(1, outcomes.Count), hung, crashedTests, clock.Elapsed.TotalSeconds));
      AppendGroups(summary, outcomes, true);
      summary.AppendLine();
      summary.AppendLine("by spec file (passed / checks):");
      AppendGroups(summary, outcomes, false);

      var seen = new Dictionary<string, int>();
      var rows = new StringBuilder();
      foreach (var x in outcomes)
      {
        string key = x.File + "\u0001" + x.Test + "\u0001" + x.Kind + "\u0001" + x.Source;
        seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 0;
        rows.Append(key).Append('\u0001').Append(seen[key]).Append('\u0002').Append(x.Passed ? '1' : '0')
          .Append('\u0002').Append(x.Actual ?? "").Append('\u0002').Append(x.Error ?? "").Append('\u0003');
      }
      Directory.CreateDirectory(outDir);
      File.WriteAllText(Path.Combine(outDir, "results-" + label + ".dat"), rows.ToString(), new UTF8Encoding(false));
      File.WriteAllText(Path.Combine(outDir, "summary-" + label + ".txt"), summary.ToString(), new UTF8Encoding(false));
      Console.Write(summary.ToString());
      return 0;
    }

    /// <summary>Passed / total per check kind, or per spec file from the worst-passing up.</summary>
    private static void AppendGroups(StringBuilder sb, List<Outcome> outcomes, bool byKind)
    {
      var order = new List<string>();
      var pass = new Dictionary<string, int>();
      var total = new Dictionary<string, int>();
      foreach (var o in outcomes)
      {
        string k = byKind ? o.Kind : o.File;
        if (!total.ContainsKey(k)) { order.Add(k); total[k] = 0; pass[k] = 0; }
        total[k]++;
        if (o.Passed) pass[k]++;
      }
      if (byKind) order.Sort((a, b) => total[b].CompareTo(total[a]));
      else order.Sort((a, b) => ((double)pass[a] / total[a]).CompareTo((double)pass[b] / total[b]));
      foreach (var k in order)
      {
        if (byKind) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,-10} {1,5} / {2}", k, pass[k], total[k]));
        else sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  {0,5:F0}%  {1,5} / {2,-5} {3}", 100.0 * pass[k] / total[k], pass[k], total[k], k));
      }
    }

    private static void RunTest(JsonElement test, string version, List<Outcome> results)
    {
      var story = new HarloweStory { FormatVersion = version };
      StorySession session = null;
      string file = test.GetProperty("file").GetString(), title = test.GetProperty("title").GetString();

      void Put(string name, string body, JsonElement step)
      {
        story.RemovePassage(name);
        var p = new HarlowePassage { Name = name, RawBody = body };
        if (step.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
        {
          p.Tags = new List<string>();
          foreach (var t in tags.EnumerateArray()) p.Tags.Add(t.GetString());
        }
        else if (step.TryGetProperty("tags", out tags) && tags.ValueKind == JsonValueKind.String)
          p.Tags = new List<string> { tags.GetString() };
        story.AddPassage(p);
      }

      RenderResult Go(string name)
      {
        if (session == null)
        {
          story.StartNode = story.GetPassage(name).Pid;
          session = new StorySession(story, 12345);
          return session.Render();
        }
        return session.Goto(name);
      }

      foreach (var step in test.GetProperty("steps").EnumerateArray())
      {
        string op = step.GetProperty("op").GetString();
        switch (op)
        {
          case "reset":
            story = new HarloweStory { FormatVersion = version };
            session = null;
            break;
          case "option": // Reference's debug flag. This library has no debug mode.
            break;
          case "create":
            Put(step.GetProperty("name").GetString(), step.GetProperty("source").GetString(), step);
            break;
          case "delete":
            story.RemovePassage(step.GetProperty("name").GetString());
            break;
          case "run":
            Put(step.GetProperty("name").GetString(), step.GetProperty("source").GetString(), step);
            Go(step.GetProperty("name").GetString());
            break;
          default:
            string source = step.GetProperty("source").GetString();
            Put("test", source, step);
            var result = Go("test");
            results.Add(Check(file, title, op, source, step, result));
            break;
        }
      }
    }

    private static Outcome Check(string file, string title, string op, string source, JsonElement step, RenderResult result)
    {
      var text = new StringBuilder();
      var errors = new List<string>();
      foreach (var e in result.Entries)
      {
        if (e.Kind == BufferedRenderOutput.Kind.Text || e.Kind == BufferedRenderOutput.Kind.Link) text.Append(e.Content);
        else if (e.Kind == BufferedRenderOutput.Kind.Error) errors.Add(e.Content);
        // The real-Harlowe run reads the page's text with each <br> as a line break.
        else if (e.Kind == BufferedRenderOutput.Kind.Html && Regex.IsMatch(e.Content ?? "", @"^<\s*br\b", RegexOptions.IgnoreCase)) text.Append('\n');
      }
      string actual = text.ToString();
      string error = errors.Count > 0 ? string.Join(" | ", errors) : null;
      var o = new Outcome { File = file, Test = title, Kind = op, Source = source, Actual = actual, Error = error };

      bool Matches()
      {
        var exp = step.GetProperty("expected");
        if (exp.TryGetProperty("regex", out var rx))
        {
          o.Expected = "/" + rx.GetString() + "/";
          var opts = RegexOptions.ECMAScript;
          string flags = exp.GetProperty("flags").GetString();
          if (flags.Contains('i')) opts |= RegexOptions.IgnoreCase;
          try { return Regex.IsMatch(actual, rx.GetString(), opts); }
          catch (ArgumentException) { return Regex.IsMatch(actual, rx.GetString()); }
        }
        o.Expected = exp.GetProperty("text").ValueKind == JsonValueKind.String ? exp.GetProperty("text").GetString() : exp.GetProperty("text").ToString();
        return actual == o.Expected;
      }

      switch (op)
      {
        // Reference compares .text(), which includes an error's text, so an exact
        // match can't pass beside an error. A regex can, so only that form tolerates one.
        case "print":
          o.Passed = Matches() && (error == null || step.GetProperty("expected").TryGetProperty("regex", out _));
          break;
        case "notPrint": o.Passed = !Matches(); break;
        case "error": o.Passed = error != null; o.Expected = "<error>"; break;
        case "notError": o.Passed = error == null; o.Expected = "<no error>"; break;
      }
      return o;
    }
  }
}
