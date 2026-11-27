using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Harlowe;
using Harlowe.Runtime;

namespace Twinelike.Probe
{
  /// <summary>
  /// Cases played a step at a time: the page's text after each click, each
  /// navigation, or each stretch of time. Every command writes, per case, the
  /// list of texts <c>record</c> writes for the same case on real Harlowe, with
  /// errors as <c>⟦error⟧</c>.
  /// </summary>
  internal static class Steps
  {
    private const string NoSuchLink = "(no such link)";

    private sealed class ClickCase
    {
      public string source { get; set; }
      public List<int> clicks { get; set; }
    }

    private sealed class LiveCase
    {
      public string source { get; set; }
      public List<double> steps { get; set; }
      public Dictionary<string, int> clicks { get; set; }
      public Dictionary<string, bool> holds { get; set; }
    }

    /// <summary>Cases <c>{ source, clicks: [linkIndex, …] }</c>, passage <c>test</c> alone, as <c>record clicks</c> plays them.</summary>
    public static int Clicks(string casesFile, string outFile)
    {
      var cases = JsonSerializer.Deserialize<List<ClickCase>>(File.ReadAllText(casesFile));
      var output = new List<List<string>>();
      foreach (var c in cases)
      {
        var story = new HarloweStory { FormatVersion = "3.3.9" };
        story.AddPassage(new HarlowePassage { Name = "test", RawBody = c.source });
        story.StartNode = story.GetPassage("test").Pid;
        var session = new StorySession(story, 42);
        var result = session.Render();
        var log = new List<string> { Text(result) };
        foreach (int n in c.clicks ?? new List<int>())
        {
          var links = Links(result);
          if (n >= links.Count) { log.Add(NoSuchLink); continue; }
          result = Click(session, links[n]);
          log.Add(Text(result));
        }
        output.Add(log);
      }
      Program.WriteJson(outFile, output);
      return 0;
    }

    /// <summary>
    /// Cases <c>{ passages: { name: [tags, text] }, go: [name, …] }</c>: render
    /// the first, then go to each in turn, where <c>&lt;undo&gt;</c> undoes.
    /// </summary>
    public static int Nav(string casesFile, string outFile)
    {
      using var doc = JsonDocument.Parse(File.ReadAllText(casesFile));
      var output = new List<List<string>>();
      foreach (var c in doc.RootElement.EnumerateArray())
      {
        var story = new HarloweStory { FormatVersion = "3.3.9" };
        foreach (var p in c.GetProperty("passages").EnumerateObject())
        {
          var tags = new List<string>(p.Value[0].GetString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
          story.AddPassage(new HarlowePassage { Name = p.Name, RawBody = p.Value[1].GetString(), Tags = tags });
        }
        var go = new List<string>();
        foreach (var g in c.GetProperty("go").EnumerateArray()) go.Add(g.GetString());
        story.StartNode = story.GetPassage(go[0]).Pid;
        var session = new StorySession(story, 42);
        var log = new List<string>();
        for (int i = 0; i < go.Count; i++)
        {
          RenderResult r;
          if (i == 0) r = session.Render();
          else if (go[i] == "<undo>") { session.Undo(); r = session.Render(); }
          else r = session.Goto(go[i]);
          log.Add(Text(r));
        }
        output.Add(log);
      }
      Program.WriteJson(outFile, output);
      return 0;
    }

    /// <summary>
    /// Cases <c>{ source, steps: [ms, …], clicks?: { step: regionIndex }, holds?: { step: true } }</c>,
    /// ticking in 16 ms frames as the browser's fake clock runs animation frames.
    /// A click fires at the start of its step; a hold presses input for the whole step.
    /// </summary>
    public static int Live(string casesFile, string outFile)
    {
      var cases = JsonSerializer.Deserialize<List<LiveCase>>(File.ReadAllText(casesFile));
      var output = new List<List<string>>();
      foreach (var c in cases)
      {
        var story = new HarloweStory { FormatVersion = "3.3.9" };
        story.AddPassage(new HarlowePassage { Name = "test", RawBody = c.source });
        story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B(print: time)" });
        story.AddPassage(new HarlowePassage { Name = "c", RawBody = "(live: 100ms)[c(stop:)]" });
        story.StartNode = story.GetPassage("test").Pid;
        var session = new StorySession(story, 42);
        var current = session.Render();
        var log = new List<string> { Text(current) };
        double now = 0, nextFrame = 16;
        for (int i = 0; i < c.steps.Count; i++)
        {
          string step = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
          if (c.clicks != null && c.clicks.TryGetValue(step, out int n))
          {
            var links = Links(current);
            if (n < links.Count) current = Click(session, links[n]);
            else log.Add(NoSuchLink);
          }
          double until = now + c.steps[i];
          bool held = c.holds != null && c.holds.TryGetValue(step, out bool h) && h;
          while (nextFrame <= until)
          {
            var r = session.Tick(16, held);
            if (r != null) current = r;
            nextFrame += 16;
          }
          now = until;
          log.Add(Text(current));
        }
        output.Add(log);
      }
      Program.WriteJson(outFile, output);
      return 0;
    }

    private static string Text(RenderResult result)
    {
      var sb = new StringBuilder();
      foreach (var e in result.Entries)
      {
        if (e.Kind == BufferedRenderOutput.Kind.Text || e.Kind == BufferedRenderOutput.Kind.Link) sb.Append(e.Content);
        else if (e.Kind == BufferedRenderOutput.Kind.Error) sb.Append("⟦error⟧");
      }
      return sb.ToString();
    }

    /// <summary>A passage link (<see cref="Target"/>) or a click region (<see cref="Region"/>).</summary>
    private sealed class Link
    {
      public string Target, Region;
    }

    /// <summary>
    /// What the recorder clicks by index, in page order: its
    /// <c>tw-link, .enchantment-link</c> query finds passage links and click
    /// regions, so hover regions don't count. Each match of a click region is
    /// its own element there, so each counts here too.
    /// </summary>
    private static List<Link> Links(RenderResult result)
    {
      var links = new List<Link>();
      foreach (var e in result.Entries)
      {
        if (e.Kind == BufferedRenderOutput.Kind.Link) links.Add(new Link { Target = e.Target });
        else if (e.Kind == BufferedRenderOutput.Kind.BeginInteractive && e.Region.Kind == InteractionKind.Click)
          links.Add(new Link { Region = e.Region.Id });
      }
      return links;
    }

    private static RenderResult Click(StorySession session, Link link)
      => link.Target != null ? session.Goto(link.Target) : session.DispatchEvent(link.Region);
  }
}
