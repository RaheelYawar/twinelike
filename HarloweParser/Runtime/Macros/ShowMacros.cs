using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// <c>(hidden:)</c>. A changer that hides the hook it's attached to, like
  /// <c>(if: false)</c>. Reference's entry in <c>ts/macrolib/stylechangers.ts</c>.
  /// </summary>
  public class HiddenMacro : IMacro
  {
    public string Name => "hidden";
    public int MinArgs => 0;
    public int MaxArgs => 0;

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
      => HarloweValue.OfChanger(Changer.FromPatches(new HiddenPatch()));
  }

  /// <summary>
  /// <c>(show: ?name, …)</c>. Shows every named hook that's hidden, by
  /// <c>|name)[…]</c>, <c>(hidden:)</c>, a false condition or <c>(hide:)</c>,
  /// and leaves other hooks alone. Reference's entry in <c>ts/macrolib/commands.ts</c>.
  ///
  /// <para>A hook hidden before it rendered runs when it's shown, with the temp
  /// variables it could see where it was written. One <c>(hide:)</c> hid gets
  /// its content back as it was, without running again. Either way, anything
  /// revision macros put there while it was hidden goes. It can only find hooks
  /// already on the page. A changer attached to the command styles what it
  /// renders, and a false condition stops it, so the hook stays hidden. Neither
  /// touches content coming back from <c>(hide:)</c>, as in reference.
  /// Reference also lets a revision, <c>(for:)</c> or link changer steer the
  /// reveal, which isn't implemented: they have no effect here.</para>
  /// </summary>
  public class ShowMacro : IMacro, ICommandCheck, ITakesHookNames
  {
    public string Name => "show";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    HarloweValue ICommandCheck.Check(List<HarloweValue> args, MacroContext context)
      => HookArgs.Check("show", args);

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = ((ICommandCheck)this).Check(args, context);
      if (error != null) return error;
      Reveal(args, context, context.Output, null);
      return null;
    }

    /// <summary>
    /// Reveals the hidden hooks that <paramref name="args"/> name, in the live
    /// page and in whatever <paramref name="output"/> is still building, which
    /// is where a hook written in the same click's content is.
    /// </summary>
    internal static void Reveal(List<HarloweValue> args, MacroContext context, IRenderOutput output, Changer attached)
    {
      // Reference applies the changer only where it renders, so a false
      // condition stops a hook rendering but not (hide:)'s content coming back.
      bool enabled = attached == null || attached.IsEnabled;
      var styles = attached?.GetStyleLayers();
      var roots = HookArgs.Roots(context, output);
      if (roots.Count == 0) return;

      for (int a = 0; a < args.Count; a++)
      {
        if (!HookArgs.SpendOnWalk(roots, context, output)) return;
        var targets = HookResolver.Resolve(roots, args[a].AsHookName);
        for (int t = 0; t < targets.Count; t++)
        {
          if (!(targets[t] is IHideable node)) continue;
          if (node is RenderRoot && !ReferenceEquals(node, context.LiveRoot)) continue;
          // A (hide:) of a node still being built hasn't happened yet. Reference
          // would have hidden it and this would put it back, so neither happens.
          if (node.PendingHide) { node.PendingHide = false; continue; }
          var content = node.HiddenContent;
          if (content == null) continue;
          if (content.Detached != null)
          {
            // Reference's elem.empty().append(data).
            node.HiddenContent = null;
            node.Children.Clear();
            node.Children.AddRange(content.Detached);
            continue;
          }
          if (enabled && node is RenderHookNode hook && !RevealHook(hook, roots, styles, context, output)) return;
        }
      }
    }

    /// <summary>Renders one hidden hook into its node. False when the nested-render limits refused it.</summary>
    private static bool RevealHook(RenderHookNode hook, List<RenderNode> roots, List<StyleSpec> styles, MacroContext context, IRenderOutput output)
    {
      var content = hook.Hidden;
      var refusal = context.EnterCustomMacro(content.Cost);
      if (refusal != NestedRenderRefusal.None)
      {
        output?.Error(refusal == NestedRenderRefusal.TooDeep
          ? "Printing this expression may have trapped me in an infinite loop."
          : "Printing stopped: " + MacroContext.OverBudgetMessage);
        return false;
      }
      try
      {
        // Cleared first, so a (show:) of this hook inside its own content does nothing.
        hook.Hidden = null;
        bool insideCollapse = RenderNodes.IsInsideCollapse(roots, hook, out var owner);
        // What a revision put here goes. A (click:) anchor stays armed.
        hook.Children.RemoveAll(node => !(node is RenderHookNode inner && inner.RevealRegionId != null));

        var builder = new RenderTreeBuilder(hook, owner);
        // The hook is this builder's base, which it never opens or closes.
        RenderNodes.StartBuilding(hook);
        int layers = styles?.Count ?? 0;
        for (int i = 0; i < layers; i++) builder.PushStyle(styles[i]);
        content.Render(builder, insideCollapse);
        for (int i = 0; i < layers; i++) builder.PopStyle();
        RenderNodes.FinishBuilding(hook);
      }
      finally
      {
        context.ExitCustomMacro();
      }
      return true;
    }
  }

  /// <summary>
  /// <c>(hide: ?name, …)</c>. Hides every named hook that's showing, keeping its
  /// content as it is for <c>(show:)</c> to put back. Reference's entry in
  /// <c>ts/macrolib/commands.ts</c>: a command that can't have changers
  /// attached, refuses <c>?page</c>, and leaves a hook that's already hidden
  /// alone. While hidden, the hook's content is out of the page, so nothing
  /// can find, change or click it, but revision macros can still fill the
  /// empty hook, and <c>(show:)</c> throws that away.
  ///
  /// <para>A hook that's hidden while it's still rendering, from inside it or
  /// from a revision, custom macro or reveal running meanwhile, hides all of
  /// its content when it's finished, because reference renders the content
  /// before running its macros. <c>?passage</c> hides the whole passage the
  /// same way, and <c>?link</c> leaves each link empty.</para>
  /// </summary>
  public class HideMacro : IMacro, ICommandCheck, ITakesHookNames
  {
    public string Name => "hide";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    HarloweValue ICommandCheck.Check(List<HarloweValue> args, MacroContext context)
      => HookArgs.Check("hide", args);

    public HarloweValue Invoke(List<HarloweValue> args, MacroContext context)
    {
      var error = ((ICommandCheck)this).Check(args, context);
      if (error != null) return error;

      var output = context.Output;
      var roots = HookArgs.Roots(context, output);
      for (int a = 0; a < args.Count && roots.Count > 0; a++)
      {
        if (!HookArgs.SpendOnWalk(roots, context, output)) return null;
        var targets = HookResolver.Resolve(roots, args[a].AsHookName);
        for (int t = 0; t < targets.Count; t++)
        {
          if (!(targets[t] is IHideable node)) continue;
          // ?passage is the page, not the content a click is still building.
          if (node is RenderRoot && !ReferenceEquals(node, context.LiveRoot)) continue;
          if (node.Building) node.PendingHide = node.HiddenContent == null;
          else RenderNodes.Hide(node);
        }
      }
      return null;
    }
  }

  /// <summary>What <c>(show:)</c> and <c>(hide:)</c> share.</summary>
  internal static class HookArgs
  {
    /// <summary>Reference's signature check, <c>[rest(HookSet)]</c>, and its shared refusal of <c>?page</c>.</summary>
    public static HarloweValue Check(string macro, List<HarloweValue> args)
    {
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].Kind != HarloweValueKind.HookName)
          return HarloweValue.OfError($"The ({macro}:) macro's {i + 1}{MacroRegistry.OrdinalSuffix(i + 1)} value is {args[i].ObjectName}, but should be a hook name (like ?this).");
        if (MacroNames.SameName(args[i].AsHookName.Name, "page"))
          return HarloweValue.OfError("You can't (hide:) the ?page. Sorry.");
      }
      return null;
    }

    /// <summary>The live page, then whatever <paramref name="output"/> is still building.</summary>
    public static List<RenderNode> Roots(MacroContext context, IRenderOutput output)
    {
      var roots = new List<RenderNode>(2);
      if (context.LiveRoot != null) roots.Add(context.LiveRoot);
      var own = (output as RenderTreeBuilder)?.Root;
      if (own != null && !ReferenceEquals(own, context.LiveRoot)) roots.Add(own);
      return roots;
    }

    /// <summary>Charges one search of every root. False, with the error shown, when the budget refuses it.</summary>
    public static bool SpendOnWalk(List<RenderNode> roots, MacroContext context, IRenderOutput output)
    {
      for (int r = 0; r < roots.Count; r++)
      {
        if (context.TrySpendOnTargetWalk(roots[r])) continue;
        output?.Error(MacroContext.OverBudgetMessage);
        return false;
      }
      return true;
    }
  }
}
