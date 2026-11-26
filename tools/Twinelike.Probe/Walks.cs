using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Harlowe;
using Harlowe.Runtime;

namespace Twinelike.Probe
{
  /// <summary>
  /// Plays walks recorded on real Harlowe (<c>record walks</c>) and writes the
  /// same transcript shape: per step, the passage, error count, nested-choice
  /// count and the page as text and choice segments.
  /// </summary>
  internal sealed class Walks
  {
    private sealed class Choice
    {
      public string Label, Event, Target, Region;
    }

    private sealed class Snap
    {
      public string Passage;
      public int Errors, Nested;
      public readonly List<string[]> Segments = new List<string[]>();
      public readonly List<Choice> Choices = new List<Choice>();

      public Dictionary<string, object> ToJson() => new Dictionary<string, object>
      {
        ["passage"] = Passage, ["errors"] = Errors, ["nested"] = Nested, ["segments"] = Segments,
      };
    }

    // The virtual clock, matched to the recorder's: an animation frame every 16 ms.
    private double _now, _nextFrame = 16;
    private RenderResult _current;

    /// <summary>
    /// Cases <c>{ cases: [{ id, file | passages + start, seed, steps }] }</c>,
    /// where <c>file</c> is a story export relative to the cases file.
    /// </summary>
    public static int Run(string casesFile, string outFile)
    {
      string dir = Path.GetDirectoryName(Path.GetFullPath(casesFile));
      using var doc = JsonDocument.Parse(File.ReadAllText(casesFile));
      var output = new Dictionary<string, object>();
      foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        output[c.GetProperty("id").GetString()] = new Walks().Play(c, dir);
      Program.WriteJson(outFile, output);
      return 0;
    }

    private List<object> Play(JsonElement c, string dir)
    {
      var transcript = new List<object>();
      var session = new StorySession(LoadStory(c, dir), c.GetProperty("seed").GetInt32());
      Snap snap = null;
      foreach (var step in c.GetProperty("steps").EnumerateArray())
      {
        RenderResult result;
        try
        {
          switch (step.GetProperty("do").GetString())
          {
            case "start":
              result = session.Render();
              break;
            case "choose":
              var choice = Find(snap, step.GetProperty("label").GetString(), step.GetProperty("event").GetString(), step.GetProperty("nth").GetInt32());
              if (choice == null)
              {
                transcript.Add(new Dictionary<string, object> { ["missingChoice"] = step.GetProperty("label").GetString() });
                return transcript;
              }
              result = choice.Target != null ? session.Goto(choice.Target) : session.DispatchEvent(choice.Region);
              break;
            case "undo":
              session.Undo();
              result = session.Render();
              break;
            case "redo":
              session.Redo();
              result = session.Render();
              break;
            default: // "wait": the recorder lets a second pass and settles, once per cycle.
              result = _current;
              int cycles = step.GetProperty("cycles").GetInt32();
              for (int k = 0; k < cycles; k++)
              {
                Advance(session, 1000.0, ref result);
                // The last cycle's settle is the one every step ends with, below.
                if (k < cycles - 1) Settle(session, ref result);
              }
              break;
          }
          snap = Settle(session, ref result);
        }
        catch (Exception e)
        {
          transcript.Add(new Dictionary<string, object> { ["exception"] = e.GetType().Name });
          return transcript;
        }
        _current = result;
        transcript.Add(snap.ToJson());
      }
      return transcript;
    }

    private void Advance(StorySession session, double ms, ref RenderResult result)
    {
      double until = _now + ms;
      while (_nextFrame <= until)
      {
        var ticked = session.Tick(16);
        if (ticked != null) result = ticked;
        _nextFrame += 16;
      }
      _now = until;
    }

    // The recorder's settle(): 48 ms at a time until the page is the same four times running.
    private Snap Settle(StorySession session, ref RenderResult result)
    {
      string last = null;
      int same = 0;
      Snap snap = null;
      for (int i = 0; i < 30; i++)
      {
        Advance(session, 48, ref result);
        snap = Build(session, result);
        string key = JsonSerializer.Serialize(snap.ToJson());
        same = key == last ? same + 1 : 0;
        last = key;
        if (same >= 3) break;
      }
      return snap;
    }

    private static HarloweStory LoadStory(JsonElement c, string dir)
    {
      if (c.TryGetProperty("file", out var file)) return LoadFile(Path.Combine(dir, file.GetString()));
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      foreach (var p in c.GetProperty("passages").EnumerateObject())
        story.AddPassage(new HarlowePassage { Name = p.Name, RawBody = p.Value.GetString() });
      story.StartNode = story.GetPassage(c.GetProperty("start").GetString()).Pid;
      return story;
    }

    private static HarloweStory LoadFile(string path)
    {
      string text = File.ReadAllText(path);
      return path.EndsWith(".twee", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tw", StringComparison.OrdinalIgnoreCase)
        ? new Harlowe.Twee.TweeReader().Read(text)
        : new HarloweStory(text);
    }

    private static Choice Find(Snap snap, string label, string ev, int nth)
    {
      int seen = 0;
      foreach (var c in snap.Choices)
        if (c.Label == label && c.Event == ev && seen++ == nth) return c;
      // The comparison reports a whitespace-only difference on its own, so the walk can go on past one.
      seen = 0;
      foreach (var c in snap.Choices)
        if (Squash(c.Label) == Squash(label) && c.Event == ev && seen++ == nth) return c;
      return null;
    }

    private static string Squash(string s) => Regex.Replace(s ?? "", @"\s+", " ").Trim();

    private static Snap Build(StorySession session, RenderResult result)
    {
      var snap = new Snap { Passage = session.CurrentPassage };
      StringBuilder label = null;
      InteractiveRegion region = null;
      int depth = 0;
      bool hidden = false;

      void Text(string s)
      {
        if (string.IsNullOrEmpty(s) || hidden) return;
        if (label != null) { label.Append(s); return; }
        int last = snap.Segments.Count - 1;
        if (last >= 0 && snap.Segments[last][0] == "text") snap.Segments[last][1] += s;
        else snap.Segments.Add(new[] { "text", s });
      }

      foreach (var e in result.Entries)
      {
        switch (e.Kind)
        {
          case BufferedRenderOutput.Kind.Text:
            Text(e.Content);
            break;
          case BufferedRenderOutput.Kind.Html:
            string html = e.Content ?? "";
            var open = Regex.Match(html, @"^<\s*(/?)\s*(script|style)\b", RegexOptions.IgnoreCase);
            if (open.Success && !Regex.IsMatch(html, @"</\s*(script|style)\s*>\s*$", RegexOptions.IgnoreCase) | open.Groups[1].Value == "/")
            {
              hidden = open.Groups[1].Value != "/";
              break;
            }
            html = Regex.Replace(html, @"<(script|style)\b.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<br\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            Text(Regex.Replace(html, @"<[^>]+>", ""));
            break;
          case BufferedRenderOutput.Kind.Link:
            if (label != null) { snap.Nested++; label.Append(e.Content); break; }
            snap.Segments.Add(new[] { "choice", e.Content, "click" });
            snap.Choices.Add(new Choice { Label = e.Content, Event = "click", Target = e.Target });
            break;
          case BufferedRenderOutput.Kind.BeginInteractive:
            if (depth++ == 0) { region = e.Region; label = new StringBuilder(); }
            else snap.Nested++;
            break;
          case BufferedRenderOutput.Kind.EndInteractive:
            if (--depth == 0)
            {
              string ev = region.Kind == InteractionKind.Click ? "click" : region.Kind == InteractionKind.MouseOver ? "mouseover" : "mouseout";
              string text = label.ToString();
              label = null;
              snap.Segments.Add(new[] { "choice", text, ev });
              snap.Choices.Add(new Choice { Label = text, Event = ev, Region = region.Id });
            }
            break;
          case BufferedRenderOutput.Kind.Error:
            snap.Errors++;
            break;
        }
      }
      return snap;
    }
  }
}
