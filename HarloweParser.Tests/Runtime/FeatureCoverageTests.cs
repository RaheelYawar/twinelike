using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Harlowe.Runtime;
using Harlowe.Twee;
using Xunit;
using Xunit.Abstractions;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// Feature-coverage smoke + readiness harness. Loads
  /// <c>TestFiles/feature-coverage.twee</c> and renders every feature passage in a
  /// fresh session: asserts <b>nothing throws</b> (a guard against uncatchable
  /// crashes across the common-idiom surface — per the error policy, missing/broken
  /// features must surface as in-prose <c>Error</c> entries, never exceptions) and
  /// emits a per-passage works/broken report (text + error messages + style/link/
  /// interactive counts) to the test output and to <c>feature-coverage-report.txt</c>
  /// in the test output directory.
  ///
  /// <para>Runs under <b>every compatibility profile</b>. The crash policy is
  /// not profile-specific — a story declaring any supported major must render
  /// without throwing — and the idiom surface is where a profile switch is
  /// most likely to have unintended reach. Each profile writes its own report
  /// file so the two runs don't clobber each other.</para>
  /// </summary>
  public class FeatureCoverageTests
  {
    private readonly ITestOutputHelper _output;
    public FeatureCoverageTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> Profiles()
    {
      yield return new object[] { "V3", HarloweProfile.V3 };
      yield return new object[] { "V4", HarloweProfile.V4 };
    }

    [Theory]
    [MemberData(nameof(Profiles))]
    public void EveryFeaturePassage_RendersWithoutThrowing(string profileName, HarloweProfile profile)
    {
      var path = Path.Combine(AppContext.BaseDirectory, "TestFiles", "feature-coverage.twee");
      // The profile must be supplied to the reader, not set afterwards: by the
      // time Read returns, every passage body has already been tokenized.
      var story = new TweeReader(profile).Read(File.ReadAllText(path));

      var names = new List<string>();
      foreach (var p in story.Passages) names.Add(p.Name);
      names.Sort(StringComparer.Ordinal);

      var sb = new StringBuilder();
      int threw = 0;
      foreach (var name in names)
      {
        var session = new StorySession(story, 1); // fixed seed → deterministic (random:)/(either:)
        RenderResult r;
        try
        {
          r = session.Goto(name);
        }
        catch (Exception ex)
        {
          threw++;
          sb.AppendLine($"{name,-18} THREW {ex.GetType().Name}: {ex.Message}");
          continue;
        }

        int err = 0, style = 0, link = 0, interactive = 0;
        var errs = new List<string>();
        for (int i = 0; i < r.Entries.Count; i++)
        {
          switch (r.Entries[i].Kind)
          {
            case BufferedRenderOutput.Kind.Error: err++; errs.Add(r.Entries[i].Content); break;
            case BufferedRenderOutput.Kind.PushStyle: style++; break;
            case BufferedRenderOutput.Kind.Link: link++; break;
            case BufferedRenderOutput.Kind.BeginInteractive: interactive++; break;
          }
        }

        var tags = new StringBuilder();
        if (style > 0) tags.Append($" style={style}");
        if (link > 0) tags.Append($" link={link}");
        if (interactive > 0) tags.Append($" interactive={interactive}");
        string errStr = err > 0 ? "  ERR: " + string.Join(" || ", errs) : "";
        sb.AppendLine($"{name,-18} text={Quote(r.Text)}{tags}{errStr}");
      }

      string report = sb.ToString();
      _output.WriteLine(report);
      File.WriteAllText(
        Path.Combine(AppContext.BaseDirectory, $"feature-coverage-report-{profileName}.txt"), report);

      Assert.Equal(0, threw); // in-prose errors are fine; an exception is a crash-policy violation
    }

    private static string Quote(string s) => "\"" + (s ?? "").Replace("\r", "").Replace("\n", "\\n") + "\"";
  }
}
