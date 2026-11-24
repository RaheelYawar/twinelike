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
  /// <c>(show: ?name, …)</c>. Renders every named hook that's hidden, by
  /// <c>|name)[…]</c>, <c>(hidden:)</c>, or a false condition, and leaves other
  /// hooks alone. Reference's entry in <c>ts/macrolib/commands.ts</c>.
  ///
  /// <para>A hook runs when it's shown, with the temp variables it could see
  /// where it was written, and replaces anything revision macros put there. It
  /// can only find hooks already on the page. A changer attached to the command
  /// styles what it reveals, and a false condition stops it, so the hook stays
  /// hidden. Reference also lets a revision, <c>(for:)</c> or link changer steer
  /// the reveal, which isn't implemented: they have no effect here.</para>
  /// </summary>
  public class ShowMacro : IMacro, ICommandCheck, ITakesHookNames
  {
    public string Name => "show";
    public int MinArgs => 1;
    public int MaxArgs => -1;

    HarloweValue ICommandCheck.Check(List<HarloweValue> args, MacroContext context)
    {
      for (int i = 0; i < args.Count; i++)
      {
        if (args[i].Kind != HarloweValueKind.HookName)
          return HarloweValue.OfError($"The (show:) macro should be given hook names, not {args[i].ObjectName}.");
        // Reference's own wording, shared with (hide:).
        if (MacroNames.SameName(args[i].AsHookName.Name, "page"))
          return HarloweValue.OfError("You can't (hide:) the ?page. Sorry.");
      }
      return null;
    }

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
      if (attached != null && !attached.IsEnabled) return;
      var styles = attached?.GetStyleLayers();

      var roots = new List<RenderNode>(2);
      if (context.LiveRoot != null) roots.Add(context.LiveRoot);
      var own = (output as RenderTreeBuilder)?.Root;
      if (own != null && !ReferenceEquals(own, context.LiveRoot)) roots.Add(own);
      if (roots.Count == 0) return;

      for (int a = 0; a < args.Count; a++)
      {
        for (int r = 0; r < roots.Count; r++)
        {
          if (context.TrySpendOnTargetWalk(roots[r])) continue;
          output?.Error(MacroContext.OverBudgetMessage);
          return;
        }
        var targets = HookResolver.Resolve(roots, args[a].AsHookName);
        for (int t = 0; t < targets.Count; t++)
        {
          if (!(targets[t] is RenderHookNode hook) || hook.Hidden == null) continue;
          if (!RevealHook(hook, roots, styles, context, output)) return;
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
        int layers = styles?.Count ?? 0;
        for (int i = 0; i < layers; i++) builder.PushStyle(styles[i]);
        content.Render(builder, insideCollapse);
        for (int i = 0; i < layers; i++) builder.PopStyle();
      }
      finally
      {
        context.ExitCustomMacro();
      }
      return true;
    }
  }
}
