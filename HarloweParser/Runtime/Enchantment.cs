using System.Collections.Generic;
using Harlowe.Runtime.Rendering;

namespace Harlowe.Runtime
{
  /// <summary>
  /// A registered, persistent restyling: a <c>(enchant:)</c> macro's target
  /// query plus the changer to apply to every match. Unlike <c>(change:)</c>
  /// — which applies once, at the point it runs — an enchantment is held on
  /// <see cref="MacroContext.Enchantments"/> and re-applied by
  /// <see cref="EnchantmentPass"/> after the passage finishes rendering, so it
  /// catches hooks declared after the macro and content rewritten by revision
  /// macros. The analogue of the reference Harlowe runtime's
  /// <c>section.enchantments</c> list + <c>updateEnchantments()</c>.
  ///
  /// <para>
  /// Exactly one of <see cref="Target"/> / <see cref="StringTarget"/> is set,
  /// and exactly one of <see cref="Changer"/> / <see cref="Lambda"/> —
  /// mirroring reference's <c>Enchantment</c> descriptor, which carries either
  /// a <c>changer</c> or a <c>lambda</c> property over a scope built from
  /// either a HookSet or a string (<c>HookSet.from(Scope)</c> in
  /// <c>ts/macrolib/enchantments.ts</c>).
  /// </para>
  /// </summary>
  public class Enchantment
  {
    /// <summary>The hook-name query re-resolved against the live render tree on each pass. Null when <see cref="StringTarget"/> is set.</summary>
    public HookNameValue Target;

    /// <summary>Literal prose to match (<c>(enchant: "gold", …)</c>). Each occurrence is wrapped as an addressable hook per pass. Null when <see cref="Target"/> is set.</summary>
    public string StringTarget;

    /// <summary>The changer applied to every node the target resolves to. Null when <see cref="Lambda"/> is set.</summary>
    public Changer Changer;

    /// <summary>
    /// A <c>via</c> lambda evaluated per match to produce that match's changer
    /// (<c>(enchant: ?a, via (opacity: pos * 0.25))</c>), with <c>pos</c> bound
    /// to the 1-based match position. Null when <see cref="Changer"/> is set.
    /// </summary>
    public LambdaValue Lambda;
  }

  /// <summary>
  /// Runs the registered enchantments over a finished render tree — the
  /// analogue of Harlowe's <c>updateEnchantments()</c>. Invoked after a
  /// passage's main render and again after every dispatch re-render, so an
  /// enchantment catches hooks declared after the macro, revision-rewritten
  /// content, and content spliced in by a click event.
  ///
  /// <para>
  /// Idempotent by construction: <see cref="Update"/> first <em>disenchants</em>
  /// — unwraps every <see cref="RenderStyleNode"/> and string-occurrence
  /// <see cref="RenderHookNode"/> tagged with a non-null
  /// <c>SourceEnchantment</c> — and then re-applies each enchantment fresh. So
  /// running the pass N times on the same tree gives the same result as running
  /// it once, no matter how the tree mutated between passes. <c>(change:)</c>'s
  /// wraps carry no source tag and are left intact (one-shot semantics —
  /// survive across passes).
  /// </para>
  /// </summary>
  public static class EnchantmentPass
  {
    /// <summary>
    /// Apply every enchantment in <paramref name="enchantments"/> to
    /// <paramref name="root"/>. Each enchantment's target is resolved fresh —
    /// it is a query, not a cached node list — and its changer wraps every
    /// matching container's content. A disenchant sweep runs first so prior
    /// applications don't double up. <paramref name="ctx"/> supplies the store
    /// and invoker for <c>via</c>-lambda evaluation; when null, lambda
    /// enchantments are skipped (changer enchantments still apply). Null-safe;
    /// tolerates malformed entries.
    /// </summary>
    public static void Update(RenderRoot root, IReadOnlyList<Enchantment> enchantments, MacroContext ctx = null)
    {
      if (root == null || enchantments == null) return;

      Disenchant(root);

      for (int i = 0; i < enchantments.Count; i++)
      {
        var enchantment = enchantments[i];
        if (enchantment == null) continue;
        Apply(root, enchantment, ctx, enchantment);
      }
    }

    /// <summary>
    /// Apply one enchantment to <paramref name="root"/> — the shared core of
    /// the persistent pass and one-shot <c>(change:)</c> (which passes a null
    /// <paramref name="source"/> so its wraps carry no disenchant tag and
    /// survive later passes). Resolves the target (hook query, or string
    /// occurrences wrapped fresh), skips completely empty hooks (reference's
    /// <c>:empty</c> check — they're invisible and don't count toward
    /// <c>pos</c>), and applies either the fixed changer or the per-match
    /// changer the <c>via</c> lambda produces.
    /// </summary>
    public static void Apply(RenderRoot root, Enchantment enchantment, MacroContext ctx, Enchantment source)
    {
      if (root == null || enchantment == null) return;
      if (enchantment.Changer == null && enchantment.Lambda == null) return;

      IReadOnlyList<RenderNode> targets;
      if (enchantment.Target != null)
      {
        targets = HookResolver.Resolve(root, enchantment.Target);
      }
      else if (enchantment.StringTarget != null)
      {
        var wraps = TextOccurrenceFinder.FindAndWrap(root, enchantment.StringTarget);
        var list = new List<RenderNode>(wraps.Count);
        for (int i = 0; i < wraps.Count; i++)
        {
          // Tag persistent wraps so the next pass's disenchant unwinds them
          // back to plain prose before re-matching; (change:)'s (source null)
          // stay, exactly like its style layers.
          wraps[i].SourceEnchantment = source;
          list.Add(wraps[i]);
        }
        targets = list;
      }
      else
      {
        return;
      }

      int pos = 0;
      bool lambdaFailed = false;
      for (int i = 0; i < targets.Count; i++)
      {
        var target = targets[i];
        // Reference skips completely empty hooks — `|A>[]` is hidden by CSS
        // there and must not be enchanted nor advance pos. Leaves (a ?link
        // match) are never "empty".
        if (target is IRenderContainer c && c.Children.Count == 0) continue;
        pos++;

        Changer changer;
        if (enchantment.Lambda != null)
        {
          // First lambda failure replaced its match with the error and killed
          // the lambda for the remaining matches (reference nulls it out).
          if (lambdaFailed || ctx == null) continue;
          changer = EvaluateLambda(root, enchantment, target, pos, ctx, out lambdaFailed);
          if (changer == null) continue;
        }
        else
        {
          changer = enchantment.Changer;
        }

        changer.ApplyToTarget(root, target, source);
      }
    }

    /// <summary>
    /// Evaluate the enchantment's <c>via</c> lambda for one match: binds the
    /// target value to <c>it</c> and <paramref name="pos"/> to <c>pos</c> and
    /// expects a changer back. A non-changer result, an error, or a changer
    /// that can't enchant replaces the match with an in-prose error and sets
    /// <paramref name="failed"/> (reference replaces the element and ignores
    /// the rest of the scope). The whole evaluation is sandboxed by
    /// <see cref="MacroContext.PushSideEffectGuard"/>, so any session side
    /// effect a clause macro stages — a <c>(goto:)</c>/<c>(load-game:)</c>
    /// navigation, an <c>(enchant:)</c> registration, an RNG draw — is rolled
    /// back: a lambda's job is to produce a changer, and pass-time evaluation
    /// must never clobber a navigation queued by the render or dispatch that
    /// triggered the pass, grow the list this pass is iterating, or desync the
    /// reproducible RNG (see the ordering invariant in
    /// <see cref="StorySession.DispatchEvent"/>).
    /// </summary>
    private static Changer EvaluateLambda(RenderRoot root, Enchantment enchantment, RenderNode target,
                                          int pos, MacroContext ctx, out bool failed)
    {
      // Reference binds the lambda's `it` to the i-th match of the scope
      // (`scope.getProperty(i)`, a narrowed HookSet). No shipped macro that may
      // appear in an enchant lambda consumes a hook name, so the un-narrowed
      // query (or the matched string) is indistinguishable in practice.
      var item = enchantment.Target != null
        ? HarloweValue.OfHookName(enchantment.Target)
        : HarloweValue.OfString(enchantment.StringTarget);

      return EvaluateViaLambda(root, enchantment.Lambda, item, pos, ctx, target, out failed);
    }

    /// <summary>
    /// Evaluate a per-match <c>via</c> lambda that must produce an enchantable
    /// changer — the shared core of the enchantment pass's lambda form and the
    /// interaction macros' second-argument lambda (reference runs both through
    /// the same <c>enchantScope</c> loop). Binds <paramref name="item"/> to
    /// <c>it</c> and <paramref name="pos"/> to <c>pos</c>; sandboxed by
    /// <see cref="MacroContext.PushSideEffectGuard"/> so pass-time evaluation
    /// can't clobber queued navigation, the registration lists, or the RNG. A
    /// non-changer result, an error, or a changer that can't enchant replaces
    /// <paramref name="target"/> with an in-prose error and sets
    /// <paramref name="failed"/> (reference replaces the element and ignores
    /// the rest of the scope).
    /// </summary>
    internal static Changer EvaluateViaLambda(RenderRoot root, LambdaValue lambda, HarloweValue item,
                                              int pos, MacroContext ctx, RenderNode target, out bool failed)
    {
      failed = false;

      HarloweValue result;
      using (ctx.PushSideEffectGuard())
        result = LambdaInvoker.EvalTransform(lambda, item, pos, ctx);

      string message;
      if (result.IsError)
        message = result.ErrorMessage;
      else if (result.Kind != HarloweValueKind.Changer)
        message = $"The 'via' lambda must return a changer, not {result.Kind}";
      else if (!result.AsChanger.CanEnchant)
        message = "The changer produced by the 'via' lambda can't include a revision, enchantment, or interaction changer like (replace:), (click:), or (link:)";
      else
        return result.AsChanger;

      failed = true;
      ReplaceWithError(root, target, message);
      return null;
    }

    /// <summary>
    /// Swap a failed lambda's match for an in-prose error — reference's
    /// <c>e.replaceWith(error.render())</c>. When the match is the root itself
    /// (<c>?page</c>), it can't be replaced in a parent, so its content becomes
    /// the error instead.
    /// </summary>
    private static void ReplaceWithError(RenderRoot root, RenderNode target, string message)
    {
      var error = new RenderErrorNode { Message = message };
      if (RenderNodes.ReplaceChild(root, target, error)) return;
      if (target is IRenderContainer container)
      {
        container.Children.Clear();
        container.Children.Add(error);
      }
    }

    /// <summary>
    /// Walk <paramref name="container"/> and unwrap every
    /// <see cref="RenderStyleNode"/> or <see cref="RenderHookNode"/> whose
    /// <c>SourceEnchantment</c> is non-null — i.e. every style layer and
    /// string-occurrence wrap produced by a previous <see cref="Update"/>.
    /// Layers from <c>(text-style:)</c> / <c>(change:)</c> have a null source
    /// tag and stay intact.
    /// </summary>
    public static void Disenchant(IRenderContainer container)
      => RenderNodes.UnwrapWhere(container, n =>
           (n is RenderStyleNode style && style.SourceEnchantment != null)
        || (n is RenderHookNode hook && hook.SourceEnchantment != null));
  }
}
