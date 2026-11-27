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
  /// Renders each snippet as passage <c>test</c>, beside a passage <c>b</c>, and
  /// writes the normalised text the recorder reads off real Harlowe's page, in
  /// the style of the recorder kind it's compared with:
  /// <list type="bullet">
  /// <item><c>markup</c>: formatting as <c>⟦b:…⟧</c>-style brackets, links as
  /// <c>⟦link:…⟧</c>, errors as <c>⟦error⟧</c>, a list as <c>⟦list:…⟧</c>.</item>
  /// <item><c>lists</c>: the same, with list structure as <c>⟦ul:⟦li:…⟧⟧</c>.</item>
  /// <item><c>plain</c>, for <c>record errors</c>: text and link labels only,
  /// errors as <c>⟦error⟧</c>. The recorder keeps each error's message, which
  /// the comparison drops.</item>
  /// </list>
  /// </summary>
  internal static class Markup
  {
    public static int Run(string casesFile, string outFile, string style)
    {
      var cases = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(casesFile));
      var output = new List<string>(cases.Count);
      foreach (var source in cases)
      {
        try
        {
          var story = new HarloweStory { FormatVersion = "3.3.9" };
          story.AddPassage(new HarlowePassage { Name = "b", RawBody = "B" });
          story.AddPassage(new HarlowePassage { Name = "test", RawBody = source });
          story.StartNode = story.GetPassage("test").Pid;
          output.Add(Normalise(new StorySession(story, 42).Render(), style));
        }
        catch (Exception e)
        {
          output.Add("⟦threw:" + e.GetType().Name + "⟧");
        }
      }
      Program.WriteJson(outFile, output);
      return 0;
    }

    private static string Normalise(RenderResult result, string style)
    {
      bool plain = style == "plain";
      var sb = new StringBuilder();
      var closers = new Stack<int>();
      foreach (var e in result.Entries)
      {
        switch (e.Kind)
        {
          case BufferedRenderOutput.Kind.Text:
            sb.Append(e.Content);
            break;
          case BufferedRenderOutput.Kind.Link:
            if (plain) sb.Append(e.Content);
            else sb.Append("⟦link:").Append(e.Content).Append('⟧');
            break;
          case BufferedRenderOutput.Kind.Error:
            sb.Append("⟦error⟧");
            break;
          case BufferedRenderOutput.Kind.PushStyle:
            int n = 0;
            if (!plain)
            {
              if (e.Style.Bold) { sb.Append("⟦b:"); n++; }
              if (e.Style.Italic) { sb.Append("⟦i:"); n++; }
              if (e.Style.Strikethrough) { sb.Append("⟦s:"); n++; }
              if (e.Style.Superscript) { sb.Append("⟦sup:"); n++; }
            }
            closers.Push(n);
            break;
          case BufferedRenderOutput.Kind.PopStyle:
            sb.Append('⟧', closers.Count > 0 ? closers.Pop() : 0);
            break;
          case BufferedRenderOutput.Kind.Html:
            AppendHtml(sb, e.Content ?? "", style);
            break;
        }
      }
      return sb.ToString();
    }

    /// <summary>The tags the recorder marks, as it marks them; any other tag shows nothing.</summary>
    private static void AppendHtml(StringBuilder sb, string html, string style)
    {
      var m = Regex.Match(html, @"^<\s*(/?)\s*([a-zA-Z0-9-]+)");
      if (!m.Success) return;
      bool closing = m.Groups[1].Value == "/";
      string tag = m.Groups[2].Value.ToLowerInvariant();
      if (style == "plain")
      {
        if (tag == "br" && !closing) sb.Append('\n');
        return;
      }
      bool lists = style == "lists";
      switch (tag)
      {
        case "hr": if (!closing) sb.Append("⟦hr⟧"); break;
        case "br": if (!closing) sb.Append('\n'); break;
        case "b": case "strong": sb.Append(closing ? "⟧" : "⟦b:"); break;
        case "i": case "em": sb.Append(closing ? "⟧" : "⟦i:"); break;
        case "s": sb.Append(closing ? "⟧" : "⟦s:"); break;
        case "sup": sb.Append(closing ? "⟧" : "⟦sup:"); break;
        case "ul": case "ol": sb.Append(closing ? "⟧" : lists ? "⟦" + tag + ":" : "⟦list:"); break;
        case "li": if (lists) sb.Append(closing ? "⟧" : "⟦li:"); break;
      }
    }
  }
}
