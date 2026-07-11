using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Macros;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Tests for the <c>(link:)</c> family — the changers (<c>(link:)</c>/
  /// <c>(link-replace:)</c>, <c>(link-reveal:)</c>/<c>(link-append:)</c>,
  /// <c>(link-repeat:)</c>, <c>(link-rerun:)</c>) that create their own armed
  /// label in place of the attached hook, and the <c>(link-goto:)</c>/
  /// <c>(link-undo:)</c> commands. Uses a real <see cref="StorySession"/>
  /// because arming and dispatch live on the session.
  /// </summary>
  public class LinkMacroTests
  {
    private static Harlowe OnePassage(string body)
    {
      var sb = new System.Text.StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" creator=\"\" creator-version=\"\">");
      sb.Append("<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">");
      sb.Append(body);
      sb.Append("</tw-passagedata></tw-storydata></body></html>");
      return new Harlowe(sb.ToString());
    }

    private static StorySession Session(string body) => new StorySession(OnePassage(body));

    private static StorySession TwoPassageSession(string p1, string p2)
    {
      var sb = new System.Text.StringBuilder();
      sb.Append("<html><body><tw-storydata name=\"T\" startnode=\"1\" creator=\"\" creator-version=\"\">");
      sb.Append("<tw-passagedata pid=\"1\" name=\"P1\" tags=\"\">").Append(p1).Append("</tw-passagedata>");
      sb.Append("<tw-passagedata pid=\"2\" name=\"P2\" tags=\"\">").Append(p2).Append("</tw-passagedata>");
      sb.Append("</tw-storydata></body></html>");
      return new StorySession(new Harlowe(sb.ToString()));
    }

    private static string FirstRegionId(RenderResult result)
    {
      for (int i = 0; i < result.Entries.Count; i++)
        if (result.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          return result.Entries[i].Region?.Id;
      return null;
    }

    private static List<InteractiveRegion> Regions(RenderResult r)
    {
      var list = new List<InteractiveRegion>();
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.BeginInteractive)
          list.Add(r.Entries[i].Region);
      return list;
    }

    private static int CountKind(RenderResult r, BufferedRenderOutput.Kind k)
    {
      int n = 0;
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == k) n++;
      return n;
    }

    private static bool HasError(RenderResult r, string substring)
    {
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.Error
            && r.Entries[i].Content.Contains(substring))
          return true;
      return false;
    }

    private static BufferedRenderOutput.Entry FirstLink(RenderResult r)
    {
      for (int i = 0; i < r.Entries.Count; i++)
        if (r.Entries[i].Kind == BufferedRenderOutput.Kind.Link) return r.Entries[i];
      return null;
    }

    // --- (link:) / (link-replace:) ---

    [Fact]
    public void Link_ShowsLabelNotHook_ArmsOneClickRegion()
    {
      var session = Session("(link: \"Stake\")[The dracula crumbles to dust.]");
      var result = session.Render();
      Assert.Equal("Stake", result.Text);
      var region = Assert.Single(Regions(result));
      Assert.Equal(InteractionKind.Click, region.Kind);
    }

    [Fact]
    public void Dispatch_Link_ContentReplacesLabel()
    {
      var session = Session("(link: \"Stake\")[The dracula crumbles to dust.]");
      var initial = session.Render();
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("The dracula crumbles to dust.", after.Text);
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_LinkReplace_Alias_SameBehavior()
    {
      var session = Session("(link-replace: \"x\")[y]");
      var after = session.DispatchEvent(FirstRegionId(session.Render()));
      Assert.Equal("y", after.Text);
    }

    // --- (link-reveal:) / (link-append:) ---

    [Fact]
    public void Dispatch_LinkReveal_LabelStaysContentAppends()
    {
      var session = Session("(link-reveal: \"Heart\")[break]");
      var initial = session.Render();
      Assert.Equal("Heart", initial.Text);
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal("Heartbreak", after.Text);
      // Consumed: the label is plain text now, nothing armed.
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_LinkAppend_Alias_SameBehavior()
    {
      var session = Session("(link-append: \"Heart\")[break]");
      var after = session.DispatchEvent(FirstRegionId(session.Render()));
      Assert.Equal("Heartbreak", after.Text);
    }

    // --- (link-repeat:) / (link-rerun:) ---

    [Fact]
    public void Dispatch_LinkRepeat_AccumulatesPerClick_StaysArmed()
    {
      var session = Session("(link-repeat: \"S\")[A]");
      var initial = session.Render();
      var id = FirstRegionId(initial);
      Assert.Equal("SA", session.DispatchEvent(id).Text);
      var second = session.DispatchEvent(id);
      Assert.Equal("SAA", second.Text);
      Assert.Equal(1, CountKind(second, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_LinkRerun_ReplacesPerClick_StaysArmed()
    {
      var session = Session("(set: $n to 0)(link-rerun: \"R\")[(set: $n to it + 1)$n]");
      var initial = session.Render();
      Assert.Equal("R", initial.Text);
      var id = FirstRegionId(initial);
      Assert.Equal("R1", session.DispatchEvent(id).Text);
      var second = session.DispatchEvent(id);
      Assert.Equal("R2", second.Text);
      Assert.Equal(1, CountKind(second, BufferedRenderOutput.Kind.BeginInteractive));
    }

    // --- Validation and changer semantics ---

    [Fact]
    public void Link_EmptyText_Errors()
    {
      var session = Session("(link: \"\")[x]");
      Assert.True(HasError(session.Render(), "Links can't have empty strings"));
    }

    [Fact]
    public void Link_Unattached_IsInProseError()
    {
      var session = Session("(link: \"x\")");
      var result = session.Render();
      Assert.Equal(1, CountKind(result, BufferedRenderOutput.Kind.Error));
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Link_SecondArgChanger_StylesTheLabel()
    {
      var session = Session("(link: \"L\", (text-style: \"bold\"))[content]");
      var initial = session.Render();
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.PushStyle));
      // The arm style belongs to the link; the revealed content is unstyled.
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
    }

    [Fact]
    public void Link_ComposedChanger_StylesContentNotLabel()
    {
      // Reference: composed changers are "exempt" from the link and "ONLY
      // apply to the hook once it's revealed".
      var session = Session("(text-style: \"bold\")+(link: \"L\")[content]");
      var initial = session.Render();
      Assert.Equal(0, CountKind(initial, BufferedRenderOutput.Kind.PushStyle));
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal(1, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
      Assert.Equal("content", after.Text);
    }

    [Fact]
    public void Link_RevisionSecondArg_Errors()
    {
      var session = Session("(link: \"x\", (replace: ?z))[y]");
      Assert.True(HasError(session.Render(), "can't be (or include)"));
    }

    [Fact]
    public void Link_InsideFalseConditional_NothingArmedOrPlanted()
    {
      var session = Session("(if: false)+(link: \"L\")[y]");
      var result = session.Render();
      Assert.Equal(string.Empty, result.Text);
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void LinkChanger_StoredInVariable_Applies()
    {
      var session = Session("(set: $l to (link: \"GO\"))$l[content]");
      var result = session.Render();
      Assert.Equal("GO", result.Text);
      Assert.Single(Regions(result));
    }

    [Fact]
    public void Enchant_QuestionLink_MatchesCreatedLabel_UntilConsumed()
    {
      // Reference's ?Link matches created <tw-link>s; a consumed
      // (link-reveal:) label reverts to plain prose and stops matching.
      var session = Session("(enchant: ?link, (text-style: \"bold\"))(link-reveal: \"L\")[y]");
      var initial = session.Render();
      Assert.Equal(1, CountKind(initial, BufferedRenderOutput.Kind.PushStyle));
      var after = session.DispatchEvent(FirstRegionId(initial));
      Assert.Equal(0, CountKind(after, BufferedRenderOutput.Kind.PushStyle));
      Assert.Equal("Ly", after.Text);
    }

    // --- (link-goto:) ---

    [Fact]
    public void LinkGoto_EmitsFlatLinkEvent()
    {
      var session = TwoPassageSession("(link-goto: \"Enter\", \"P2\")", "dest");
      var link = FirstLink(session.Render());
      Assert.NotNull(link);
      Assert.Equal("Enter", link.Content);
      Assert.Equal("P2", link.Target);
    }

    [Fact]
    public void LinkGoto_OneArg_PassageDefaultsToText()
    {
      var session = TwoPassageSession("(link-goto: \"P2\")", "dest");
      var link = FirstLink(session.Render());
      Assert.NotNull(link);
      Assert.Equal("P2", link.Content);
      Assert.Equal("P2", link.Target);
    }

    [Fact]
    public void LinkGoto_MissingPassage_StillEmitsLink()
    {
      // Deliberate engine behaviour: same host deferral as a dangling
      // [[link]] (reference renders a broken-link element instead).
      var session = Session("(link-goto: \"Enter\", \"Nope\")");
      var result = session.Render();
      var link = FirstLink(result);
      Assert.NotNull(link);
      Assert.Equal("Nope", link.Target);
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void LinkGoto_EmptyText_Errors()
    {
      var session = Session("(link-goto: \"\", \"P2\")");
      Assert.True(HasError(session.Render(), "Links can't have empty strings"));
    }

    // --- (link-undo:) ---

    [Fact]
    public void LinkUndo_FirstTurn_ShowsAltInsteadOfLink()
    {
      var session = Session("(link-undo: \"Back\", \"cannot\")");
      var result = session.Render();
      Assert.Equal("cannot", result.Text);
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.Error));
    }

    [Fact]
    public void LinkUndo_FirstTurn_NoAlt_ShowsNothing()
    {
      var session = Session("(link-undo: \"Back\")");
      var result = session.Render();
      Assert.Equal(string.Empty, result.Text);
      Assert.Equal(0, CountKind(result, BufferedRenderOutput.Kind.BeginInteractive));
    }

    [Fact]
    public void Dispatch_LinkUndo_ReturnsToPreviousTurn()
    {
      var session = TwoPassageSession("origin", "(link-undo: \"Back\")");
      session.Render();
      var atP2 = session.Goto("P2");
      Assert.Equal("Back", atP2.Text);
      var region = Assert.Single(Regions(atP2));

      var after = session.DispatchEvent(region.Id);
      Assert.Equal("P1", after.PassageName);
      Assert.Equal("origin", after.Text);
    }

    // --- Argument validation and standalone (no session/tree) degradation ---

    [Fact]
    public void LinkGoto_NonStringText_RendersError()
    {
      Assert.True(HasError(Session("(link-goto: 5)").Render(), "(link-goto:)"));
    }

    [Fact]
    public void LinkGoto_NonStringPassage_RendersError()
    {
      Assert.True(HasError(Session("(link-goto: \"t\", 5)").Render(), "second argument"));
    }

    [Fact]
    public void LinkUndo_NonStringAlt_RendersError()
    {
      Assert.True(HasError(Session("(link-undo: \"t\", 5)").Render(), "second argument"));
    }

    private static (MacroRegistry reg, MacroContext ctx) Standalone()
    {
      var reg = new MacroRegistry();
      StandardMacros.RegisterAll(reg);
      var ctx = new MacroContext { Store = new HarloweVariableStore() };
      reg.Context = ctx;
      return (reg, ctx);
    }

    [Fact]
    public void LinkGoto_NoOutput_ReturnsProseOnlyError()
    {
      var (reg, ctx) = Standalone();
      var v = reg.Invoke("link-goto", new List<HarloweValue> { HarloweValue.OfString("t") }, ctx);
      Assert.True(v.IsError);
      Assert.Contains("passage prose", v.ErrorMessage);
    }

    [Fact]
    public void LinkUndo_NoOutput_ReturnsProseOnlyError()
    {
      var (reg, ctx) = Standalone();
      var v = reg.Invoke("link-undo", new List<HarloweValue> { HarloweValue.OfString("t") }, ctx);
      Assert.True(v.IsError);
      Assert.Contains("passage prose", v.ErrorMessage);
    }

    [Fact]
    public void LinkUndo_PlainBufferOutput_DegradesToVisibleLabel()
    {
      // Outside a tree-building render there is nothing to arm — the label
      // must still show rather than vanish or error.
      var (reg, ctx) = Standalone();
      var buf = new BufferedRenderOutput();
      ctx.Output = buf;
      var v = reg.Invoke("link-undo", new List<HarloweValue> { HarloweValue.OfString("Back") }, ctx);
      Assert.True(v == null || !v.IsError);
      Assert.Equal("Back", buf.Text);
    }
  }
}
