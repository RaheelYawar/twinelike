using System;
using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Rendering;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime
{
  /// <summary>
  /// The frozen core output contract and its optional features: a host only
  /// receives event kinds it opted into, and one that didn't still gets a
  /// working link.
  /// </summary>
  public class OutputFeatureTests
  {
    // A label a flat string can't express: bold content spliced into a link.
    private const string StyledLabel = "[[Go->P2]](replace: ?link)[''R'']";

    // A label holding an error: the unknown macro renders in-prose inside it.
    private const string ErrorLabel = "[[Go->P2]](append: ?link)[(nosuchmacro:)]";

    /// <summary>Records calls through only the eight core channels.</summary>
    private class CoreOutput : IRenderOutput
    {
      public readonly List<string> Calls = new List<string>();
      public void Text(string content) => Calls.Add("T:" + content);
      public void Html(string rawHtml) => Calls.Add("H:" + rawHtml);
      public void Link(string text, string target) => Calls.Add("L:" + text + ">" + target);
      public void Error(string message) => Calls.Add("E:" + message);
      public void PushStyle(StyleSpec style) => Calls.Add("P");
      public void PopStyle() => Calls.Add("/P");
      public void BeginInteractive(InteractiveRegion region) => Calls.Add("I:" + region?.Id);
      public void EndInteractive() => Calls.Add("/I");
    }

    private class LabelOutput : CoreOutput, ILinkLabelOutput
    {
      public void BeginLink(string target) => Calls.Add("BL:" + target);
      public void EndLink() => Calls.Add("/BL");
    }

    // A copy of the documented minimal adapter. If IRenderOutput gains a member
    // this stops compiling, which is the break every host would see.
    private class ConsoleOutput : IRenderOutput
    {
      public void Text(string content)             => Console.Write(content);
      public void Html(string rawHtml)             => Console.Write(rawHtml);
      public void Link(string text, string target) => Console.Write($"[{text} -> {target}]");
      public void Error(string message)            => Console.Write($"<<error: {message}>>");

      public void PushStyle(StyleSpec style) {
        if (style.Bold)   Console.Write("\x1b[1m");
        if (style.Italic) Console.Write("\x1b[3m");
      }
      public void PopStyle() => Console.Write("\x1b[0m");

      public void BeginInteractive(InteractiveRegion region) => Console.Write("[");
      public void EndInteractive()                           => Console.Write("]");
    }

    private static StorySession NewSession(string firstPassage, bool optIn = false)
    {
      var html = "<tw-storydata name=\"T\" startnode=\"1\">"
        + "<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">" + firstPassage + "</tw-passagedata>"
        + "<tw-passagedata pid=\"2\" name=\"P2\" tags=\"\">second</tw-passagedata>"
        + "</tw-storydata>";
      var session = new StorySession(new HarloweStory(html));
      if (optIn) session.OutputFeatures = OutputFeatures.LinkLabels;
      return session;
    }

    /// <summary>Replays hand-emitted events into a core-only output through the whole-list path.</summary>
    private static List<string> Lowered(Action<ILinkLabelOutput> emit)
    {
      var buf = new BufferedRenderOutput();
      emit(buf);
      var core = new CoreOutput();
      new RenderResult { Entries = buf.Entries }.ReplayTo(core);
      return core.Calls;
    }

    private static int Count(RenderResult r, BufferedRenderOutput.Kind kind)
    {
      int n = 0;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == kind) n++;
      return n;
    }

    private static List<string> Signatures(Type type)
    {
      var signatures = new List<string>();
      foreach (var method in type.GetMethods()) signatures.Add(method.ToString());
      signatures.Sort(StringComparer.Ordinal);
      return signatures;
    }

    // --- The frozen contracts: a changed signature breaks hosts as surely as
    // an added member. ---

    [Fact]
    public void IRenderOutput_IsFrozenAtItsEightChannels()
    {
      Assert.Equal(
        new[]
        {
          "Void BeginInteractive(Harlowe.Runtime.InteractiveRegion)",
          "Void EndInteractive()",
          "Void Error(System.String)",
          "Void Html(System.String)",
          "Void Link(System.String, System.String)",
          "Void PopStyle()",
          "Void PushStyle(Harlowe.Runtime.StyleSpec)",
          "Void Text(System.String)"
        },
        Signatures(typeof(IRenderOutput)));
    }

    [Fact]
    public void ILinkLabelOutput_AddsOnlyTheBracketPair()
    {
      Assert.Equal(
        new[] { "Void BeginLink(System.String)", "Void EndLink()" },
        Signatures(typeof(ILinkLabelOutput)));
      Assert.True(typeof(IRenderOutput).IsAssignableFrom(typeof(ILinkLabelOutput)));
    }

    [Fact]
    public void ISaveStorage_IsFrozenAtItsFourMembers()
    {
      Assert.Equal(
        new[]
        {
          "Boolean TryDelete(System.String)",
          "Boolean TryRead(System.String, System.String ByRef)",
          "Boolean TryWrite(System.String, System.String, System.String)",
          "System.Collections.Generic.IEnumerable`1[Harlowe.Runtime.Saving.SavedGameInfo] Enumerate()"
        },
        Signatures(typeof(ISaveStorage)));
    }

    [Fact]
    public void DocumentedMinimalAdapter_IsACompleteOutput()
    {
      Assert.IsAssignableFrom<IRenderOutput>(new ConsoleOutput());
      Assert.False(typeof(ILinkLabelOutput).IsAssignableFrom(typeof(ConsoleOutput)));
    }

    // --- Lowering, per kind of label content ---

    [Fact]
    public void Lowering_TextBecomesTheLabel()
    {
      var calls = Lowered(o => { o.BeginLink("P"); o.Text("a"); o.Text("b"); o.EndLink(); });
      Assert.Equal(new[] { "L:ab>P" }, calls);
    }

    [Fact]
    public void Lowering_DropsStyles()
    {
      var calls = Lowered(o =>
      {
        o.BeginLink("P"); o.Text("a");
        o.PushStyle(new StyleSpec { Bold = true }); o.Text("b"); o.PopStyle();
        o.EndLink();
      });
      Assert.Equal(new[] { "L:ab>P" }, calls);
    }

    [Fact]
    public void Lowering_DropsInteractiveRegions()
    {
      var calls = Lowered(o =>
      {
        o.BeginLink("P");
        o.BeginInteractive(new InteractiveRegion { Id = "r" }); o.Text("a"); o.EndInteractive();
        o.EndLink();
      });
      Assert.Equal(new[] { "L:a>P" }, calls);
    }

    [Fact]
    public void Lowering_DropsHtml()
    {
      var calls = Lowered(o => { o.BeginLink("P"); o.Html("<i>"); o.Text("a"); o.Html("</i>"); o.EndLink(); });
      Assert.Equal(new[] { "L:a>P" }, calls);
    }

    [Fact]
    public void Lowering_MovesErrorsAfterTheLink_InOrder()
    {
      var calls = Lowered(o =>
      {
        o.BeginLink("P"); o.Text("a"); o.Error("e1"); o.Text("b"); o.Error("e2"); o.EndLink();
        o.Text("after");
      });
      Assert.Equal(new[] { "L:ab>P", "E:e1", "E:e2", "T:after" }, calls);
    }

    [Fact]
    public void Lowering_NestedBracket_JoinsTheLabelAndDropsItsTarget()
    {
      var calls = Lowered(o =>
      {
        o.BeginLink("Outer"); o.Text("a");
        o.BeginLink("Inner"); o.Text("b"); o.EndLink();
        o.Text("c"); o.EndLink();
        o.Text("after");
      });
      Assert.Equal(new[] { "L:abc>Outer", "T:after" }, calls);
    }

    [Fact]
    public void Lowering_InnerFlatLink_JoinsTheLabelAndDropsItsTarget()
    {
      var calls = Lowered(o => { o.BeginLink("Outer"); o.Text("a"); o.Link("b", "Inner"); o.EndLink(); });
      Assert.Equal(new[] { "L:ab>Outer" }, calls);
    }

    [Fact]
    public void Lowering_EmptyLabel_IsAnEmptyLink()
    {
      var calls = Lowered(o => { o.BeginLink("P"); o.EndLink(); });
      Assert.Equal(new[] { "L:>P" }, calls);
    }

    [Fact]
    public void Lowering_UnmatchedEndLink_IsIgnored()
    {
      var calls = Lowered(o => { o.Text("a"); o.EndLink(); o.Text("b"); });
      Assert.Equal(new[] { "T:a", "T:b" }, calls);
    }

    [Fact]
    public void Lowering_UnclosedBeginLink_StillDeliversTheLink()
    {
      var calls = Lowered(o => { o.BeginLink("P"); o.Text("a"); o.Error("e"); });
      Assert.Equal(new[] { "L:a>P", "E:e" }, calls);
    }

    [Fact]
    public void Lowering_ForwardsEverythingOutsideALabel()
    {
      var calls = Lowered(o =>
      {
        o.PushStyle(new StyleSpec { Bold = true });
        o.BeginInteractive(new InteractiveRegion { Id = "r" });
        o.Text("t"); o.Html("<br>"); o.Link("L", "T"); o.Error("e");
        o.BeginLink("P"); o.Text("a"); o.EndLink();
        o.EndInteractive();
        o.PopStyle();
      });
      Assert.Equal(new[] { "P", "I:r", "T:t", "H:<br>", "L:L>T", "E:e", "L:a>P", "/I", "/P" }, calls);
    }

    [Fact]
    public void WholeListReplay_RepairsBracketsForACapableOutput()
    {
      // A capable output's EndLink is contractually paired, so a hand-built
      // list's stray close is dropped and a missing one supplied.
      var buf = new BufferedRenderOutput();
      ILinkLabelOutput sink = buf;
      sink.EndLink();
      sink.BeginLink("P");
      sink.Text("a");

      var output = new LabelOutput();
      new RenderResult { Entries = buf.Entries }.ReplayTo(output);
      Assert.Equal(new[] { "BL:P", "T:a", "/BL" }, output.Calls);
    }

    [Fact]
    public void WholeListReplay_IsNullSafe()
    {
      new RenderResult { Entries = new List<BufferedRenderOutput.Entry>() }.ReplayTo(null);
      var core = new CoreOutput();
      new RenderResult().ReplayTo(core);
      Assert.Empty(core.Calls);
    }

    // --- Every flag x adapter combination ---

    [Fact]
    public void DefaultEntries_HoldOnlyCoreChannels()
    {
      var r = NewSession(StyledLabel).Render();
      Assert.Equal(0, Count(r, BufferedRenderOutput.Kind.BeginLink));
      Assert.Equal(0, Count(r, BufferedRenderOutput.Kind.EndLink));
      Assert.Equal(0, Count(r, BufferedRenderOutput.Kind.PushStyle));
      var link = Assert.Single(r.Entries);
      Assert.Equal(BufferedRenderOutput.Kind.Link, link.Kind);
      Assert.Equal("R", link.Content);
      Assert.Equal("P2", link.Target);
    }

    [Fact]
    public void OptedInEntries_CarryTheBracket()
    {
      var r = NewSession(StyledLabel, optIn: true).Render();
      var kinds = new List<BufferedRenderOutput.Kind>();
      for (int i = 0; i < r.Entries.Count; i++) kinds.Add(r.Entries[i].Kind);
      Assert.Equal(
        new[]
        {
          BufferedRenderOutput.Kind.BeginLink,
          BufferedRenderOutput.Kind.PushStyle,
          BufferedRenderOutput.Kind.Text,
          BufferedRenderOutput.Kind.PopStyle,
          BufferedRenderOutput.Kind.EndLink
        },
        kinds);
    }

    // One real story per kind of label content, through the session's own
    // lowering. Expected streams are '|'-separated.
    [Theory]
    [InlineData("[[Go->P2]](append: ?link)[&lt;i&gt;x&lt;/i&gt;]", "L:Gox>P2", "BL:P2|T:Go|H:<i>|T:x|H:</i>|/BL")]
    [InlineData("[[Go->P2]](click: \"Go\")[hi]", "L:Go>P2", "BL:P2|I:r-0|T:Go|/I|/BL")]
    [InlineData("[[A->P2]](append: ?link)[ [[B->P1]]]", "L:A B>P2", "BL:P2|T:A|T: |L:B>P1|/BL")]
    [InlineData("[[A->P2]](append: ?link)[ [[B->P1]]](enchant: \"B\", (text-style: \"bold\"))", "L:A B>P2", "BL:P2|T:A|T: |BL:P1|P|T:B|/P|/BL|/BL")]
    [InlineData("[[Go->P2]](replace: ?link)[]", "L:>P2", "L:>P2")]
    public void SessionLowering_PerLabelContent(string source, string core, string bracketed)
    {
      var coreOutput = new CoreOutput();
      NewSession(source).Render().ReplayTo(coreOutput);
      Assert.Equal(core.Split('|'), coreOutput.Calls);

      var labelOutput = new LabelOutput();
      NewSession(source).Render().ReplayTo(labelOutput);
      Assert.Equal(bracketed.Split('|'), labelOutput.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayIntoCoreOutput_IsAFlatLink_WhateverTheFlag(bool optIn)
    {
      var core = new CoreOutput();
      NewSession(StyledLabel, optIn).Render().ReplayTo(core);
      Assert.Equal(new[] { "L:R>P2" }, core.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplayIntoLabelOutput_IsABracket_WhateverTheFlag(bool optIn)
    {
      var output = new LabelOutput();
      NewSession(StyledLabel, optIn).Render().ReplayTo(output);
      Assert.Equal(new[] { "BL:P2", "P", "T:R", "/P", "/BL" }, output.Calls);
    }

    [Fact]
    public void LabelErrors_FollowTheLink_InDefaultEntries()
    {
      var output = new LabelOutput();
      var r = NewSession(ErrorLabel).Render();
      Assert.Equal(1, Count(r, BufferedRenderOutput.Kind.Error));
      r.ReplayTo(output);
      Assert.Equal("BL:P2", output.Calls[0]);
      Assert.Equal("T:Go", output.Calls[1]);
      Assert.Equal("/BL", output.Calls[2]);
      Assert.StartsWith("E:", output.Calls[3]);
      Assert.Equal(4, output.Calls.Count);
    }

    [Fact]
    public void LabelErrors_StayInPlace_WhenOptedIn()
    {
      var output = new LabelOutput();
      NewSession(ErrorLabel, optIn: true).Render().ReplayTo(output);
      Assert.Equal("BL:P2", output.Calls[0]);
      Assert.Equal("T:Go", output.Calls[1]);
      Assert.StartsWith("E:", output.Calls[2]);
      Assert.Equal("/BL", output.Calls[3]);
      Assert.Equal(4, output.Calls.Count);
    }

    [Fact]
    public void Flush_IntoCoreOutput_LowersTheLabel()
    {
      var root = StructuredTree();
      var core = new CoreOutput();
      RenderTreeFlusher.Flush(root, core);
      Assert.Equal(new[] { "L:abcd>P", "E:oops" }, core.Calls);
      Assert.Equal("abcd", ((RenderLinkNode)root.Children[0]).Text);
    }

    [Fact]
    public void Flush_IntoLabelOutput_KeepsTheBracket()
    {
      var output = new LabelOutput();
      RenderTreeFlusher.Flush(StructuredTree(), output);
      Assert.Equal(
        new[] { "BL:P", "T:a", "P", "T:b", "/P", "H:<br>", "I:r", "T:c", "/I", "BL:Inner", "H:<i>", "T:d", "/BL", "E:oops", "/BL" },
        output.Calls);
    }

    private static RenderRoot StructuredTree()
    {
      var builder = new RenderTreeBuilder();
      builder.BeginLink("P");
      builder.Text("a");
      builder.PushStyle(new StyleSpec { Bold = true });
      builder.Text("b");
      builder.PopStyle();
      builder.Html("<br>");
      builder.BeginInteractive(new InteractiveRegion { Id = "r" });
      builder.Text("c");
      builder.EndInteractive();
      builder.BeginLink("Inner");
      builder.Html("<i>");
      builder.Text("d");
      builder.EndLink();
      builder.Error("oops");
      builder.EndLink();
      return builder.Root;
    }

    // --- End to end ---

    [Fact]
    public void ZeroTwoHost_StructuredLabelStaysNavigable()
    {
      // A 0.2.0 host walks Entries itself, knowing only that release's kinds,
      // and navigates to the target of the link the player picks.
      var session = NewSession(StyledLabel);
      string target = null;
      var entries = session.Render().Entries;
      for (int i = 0; i < entries.Count; i++)
      {
        switch (entries[i].Kind)
        {
          case BufferedRenderOutput.Kind.Link:
            target = entries[i].Target;
            break;
          case BufferedRenderOutput.Kind.Text:
          case BufferedRenderOutput.Kind.Html:
          case BufferedRenderOutput.Kind.Error:
          case BufferedRenderOutput.Kind.PushStyle:
          case BufferedRenderOutput.Kind.PopStyle:
          case BufferedRenderOutput.Kind.BeginInteractive:
          case BufferedRenderOutput.Kind.EndInteractive:
            break;
          default:
            throw new InvalidOperationException("a 0.2.0 host doesn't know " + entries[i].Kind);
        }
      }

      Assert.Equal("P2", target);
      Assert.Equal("second", session.Goto(target).Text);
    }

    [Fact]
    public void ResultText_DoesNotDependOnTheFlag()
    {
      const string source = "before " + StyledLabel + " after";
      Assert.Equal(NewSession(source).Render().Text, NewSession(source, optIn: true).Render().Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispatch_FollowsTheSetting(bool optIn)
    {
      // The click splices styled content into the link's label.
      var session = NewSession("[[Go->P2]](click-replace: ?link)[''X'']", optIn);
      var first = session.Render();
      string region = null;
      for (int i = 0; i < first.Entries.Count; i++)
        if (first.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          region = first.Entries[i].Region.Id;

      var after = session.DispatchEvent(region);
      Assert.Equal(optIn ? 1 : 0, Count(after, BufferedRenderOutput.Kind.BeginLink));
      Assert.Equal(optIn ? 0 : 1, Count(after, BufferedRenderOutput.Kind.Link));

      var core = new CoreOutput();
      after.ReplayTo(core);
      Assert.Equal(new[] { "L:X>P2" }, core.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedGoto_FollowsTheSetting_AndAppendsItsError(bool optIn)
    {
      var session = NewSession(StyledLabel, optIn);
      session.Render();
      var r = session.Goto("Nowhere");
      Assert.Equal(optIn ? 1 : 0, Count(r, BufferedRenderOutput.Kind.BeginLink));
      Assert.Equal(optIn ? 0 : 1, Count(r, BufferedRenderOutput.Kind.Link));
      Assert.Equal(BufferedRenderOutput.Kind.Error, r.Entries[r.Entries.Count - 1].Kind);

      var core = new CoreOutput();
      r.ReplayTo(core);
      Assert.Equal(2, core.Calls.Count);
      Assert.Equal("L:R>P2", core.Calls[0]);
      Assert.StartsWith("E:", core.Calls[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownRegionDispatch_FollowsTheSetting(bool optIn)
    {
      var session = NewSession(StyledLabel, optIn);
      session.Render();
      var r = session.DispatchEvent("no-such-region");
      Assert.Equal(optIn ? 1 : 0, Count(r, BufferedRenderOutput.Kind.BeginLink));
      Assert.Equal(optIn ? 0 : 1, Count(r, BufferedRenderOutput.Kind.Link));
    }

    [Fact]
    public void Replay_HonoursHostEditsToEntries()
    {
      // A release build filtering errors out must not see them come back
      // through a bracketed replay.
      var r = NewSession(ErrorLabel).Render();
      r.Entries.RemoveAll(e => e.Kind == BufferedRenderOutput.Kind.Error);

      var output = new LabelOutput();
      r.ReplayTo(output);
      Assert.Equal(new[] { "BL:P2", "T:Go", "/BL" }, output.Calls);
    }

    [Fact]
    public void Replay_HonoursHostRewriteOfLinkText()
    {
      var r = NewSession(StyledLabel).Render();
      r.Entries[0].Content = "Localised";

      var output = new LabelOutput();
      r.ReplayTo(output);
      Assert.Equal(new[] { "L:Localised>P2" }, output.Calls);
    }

    // --- Per-entry replay ---

    [Theory]
    [InlineData(BufferedRenderOutput.Kind.BeginLink)]
    [InlineData(BufferedRenderOutput.Kind.EndLink)]
    public void EntryReplay_BracketIntoCoreOutput_ThrowsNamingBothFixes(BufferedRenderOutput.Kind kind)
    {
      var entry = new BufferedRenderOutput.Entry { Kind = kind, Target = "P" };
      var ex = Assert.Throws<NotSupportedException>(() => entry.ReplayTo(new CoreOutput()));
      Assert.Contains("ILinkLabelOutput", ex.Message);
      Assert.Contains("RenderResult.ReplayTo", ex.Message);
    }

    [Fact]
    public void EntryReplay_DefaultEntriesIntoCoreOutput_IsAFlatLink()
    {
      var entries = NewSession(StyledLabel).Render().Entries;
      var core = new CoreOutput();
      for (int i = 0; i < entries.Count; i++) entries[i].ReplayTo(core);
      Assert.Equal(new[] { "L:R>P2" }, core.Calls);
    }

    [Fact]
    public void EntryReplay_FlattenedLabelIntoLabelOutput_EmitsTheBracket()
    {
      var entries = NewSession(StyledLabel).Render().Entries;
      var output = new LabelOutput();
      for (int i = 0; i < entries.Count; i++) entries[i].ReplayTo(output);
      Assert.Equal(new[] { "BL:P2", "P", "T:R", "/P", "/BL" }, output.Calls);
    }

    [Fact]
    public void EntryReplay_OptedInBracketIntoLabelOutput_Forwards()
    {
      var entries = NewSession(StyledLabel, optIn: true).Render().Entries;
      var output = new LabelOutput();
      for (int i = 0; i < entries.Count; i++) entries[i].ReplayTo(output);
      Assert.Equal(new[] { "BL:P2", "P", "T:R", "/P", "/BL" }, output.Calls);
    }

    // --- HtmlRenderOutput ---

    [Fact]
    public void Html_OverCapableInner_ForwardsTheBracket()
    {
      var inner = new LabelOutput();
      var html = new HtmlRenderOutput(inner);
      html.BeginLink("P");
      html.Text("a<b");
      html.EndLink();
      Assert.Equal(new[] { "BL:P", "T:a&lt;b", "/BL" }, inner.Calls);
    }

    [Fact]
    public void Html_OverCoreInner_LowersEscapedOnce_TagStackBalanced()
    {
      var core = new CoreOutput();
      var html = new HtmlRenderOutput(core);
      html.BeginLink("P");
      html.Text("a<b");
      html.PushStyle(new StyleSpec { Bold = true });
      html.Text("c");
      html.PopStyle();
      html.Error("x<y");
      html.EndLink();
      // A style after the label still opens and closes its own tag.
      html.PushStyle(new StyleSpec { Italic = true });
      html.Text("d");
      html.PopStyle();

      Assert.Equal(new[] { "L:a&lt;bc>P", "E:x&lt;y", "H:<i>", "T:d", "H:</i>" }, core.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Html_ReplayOverCoreInner_IsAFlatLink_WhateverTheFlag(bool optIn)
    {
      var core = new CoreOutput();
      NewSession(StyledLabel, optIn).Render().ReplayTo(new HtmlRenderOutput(core));
      Assert.Equal(new[] { "L:R>P2" }, core.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Html_ReplayOverCapableInner_IsABracket_WhateverTheFlag(bool optIn)
    {
      var inner = new LabelOutput();
      NewSession(StyledLabel, optIn).Render().ReplayTo(new HtmlRenderOutput(inner));
      Assert.Equal(new[] { "BL:P2", "H:<b>", "T:R", "H:</b>", "/BL" }, inner.Calls);
    }

    [Fact]
    public void Html_NullInner_Throws()
    {
      Assert.Throws<ArgumentNullException>(() => new HtmlRenderOutput(null));
    }
  }
}
