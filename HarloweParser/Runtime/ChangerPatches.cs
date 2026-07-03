using System.Collections.Generic;

namespace Harlowe.Runtime
{
  /// <summary>
  /// One mutation step a <see cref="Changer"/> applies to a
  /// <see cref="HookDescriptor"/> during composition. Concrete patch types
  /// must implement structural <see cref="object.Equals(object)"/> so two
  /// changers composed from equivalent patches compare equal.
  /// </summary>
  public interface IChangerPatch
  {
    void Apply(HookDescriptor descriptor);
  }

  /// <summary>
  /// Patch produced by style changers like <c>(text-style:)</c>. Appends one
  /// <see cref="StyleSpec"/> layer to the descriptor's style list.
  /// </summary>
  public class StylePatch : IChangerPatch
  {
    public StyleSpec Style;

    public void Apply(HookDescriptor descriptor) => descriptor.Styles.Add(Style ?? new StyleSpec());

    public override bool Equals(object obj)
      => obj is StylePatch other && object.Equals(Style, other.Style);

    public override int GetHashCode() => Style?.GetHashCode() ?? 0;
  }

  /// <summary>
  /// Patch produced by <c>(text-style: "none")</c>: clears any style layers
  /// accumulated by patches that ran earlier in the same composition. Matches
  /// the reference Harlowe semantics where the <c>none</c> sentinel wipes prior
  /// text-style layers from the descriptor. Patches that follow this one still
  /// add their styles normally — composing <c>(text-style: "bold") +
  /// (text-style: "none", "italic")</c> yields italic only.
  /// </summary>
  public class ClearStylesPatch : IChangerPatch
  {
    public void Apply(HookDescriptor descriptor) => descriptor.Styles.Clear();

    public override bool Equals(object obj) => obj is ClearStylesPatch;

    public override int GetHashCode() => -1;
  }

  /// <summary>Which conditional macro produced a <see cref="ConditionalPatch"/>. Drives the show/hide pairing rules in <see cref="BodyRenderer"/> (an <c>(else-if:)</c> hide preserves the prior pairing; the others record it).</summary>
  public enum ConditionalKind
  {
    If,
    Unless,
    ElseIf,
    Else
  }

  /// <summary>
  /// Patch produced by the conditional changers <c>(if:)</c> / <c>(unless:)</c> /
  /// <c>(else-if:)</c> / <c>(else:)</c>. ANDs its decision into
  /// <see cref="HookDescriptor.Enabled"/>, mirroring reference Harlowe's
  /// changer-apply functions in <c>ts/macrolib/stylechangers.ts</c> —
  /// <c>(d, expr) =&gt; {d.enabled &amp;&amp;= expr}</c> for <c>if</c>/
  /// <c>elseif</c>/<c>else</c> and <c>&amp;&amp;= !expr</c> for <c>unless</c> —
  /// which is what lets <c>(if: $cond) + (text-style: "bold")</c> compose.
  /// <see cref="Value"/> is the macro's evaluated boolean argument (for
  /// <c>(else-if:)</c>/<c>(else:)</c>, the decision baked at call time from
  /// the pairing state).
  /// </summary>
  public class ConditionalPatch : IChangerPatch
  {
    public ConditionalKind Kind;
    public bool Value;

    public void Apply(HookDescriptor descriptor)
      => descriptor.Enabled = descriptor.Enabled && (Kind == ConditionalKind.Unless ? !Value : Value);

    public override bool Equals(object obj)
      => obj is ConditionalPatch other && Kind == other.Kind && Value == other.Value;

    public override int GetHashCode() => ((int)Kind * 397) ^ (Value ? 1 : 0);
  }

  /// <summary>
  /// Patch produced by <c>(for:)</c>. Sets the descriptor's iteration spec.
  /// If a prior iteration patch already wrote one, the later one wins — a
  /// chained <c>(for:) + (for:)</c> composition is rejected at apply time by
  /// the renderer rather than here.
  /// </summary>
  public class IterationPatch : IChangerPatch
  {
    public IterationSpec Iteration;

    public void Apply(HookDescriptor descriptor) => descriptor.Iteration = Iteration;

    public override bool Equals(object obj)
    {
      if (!(obj is IterationPatch other)) return false;
      if (Iteration == null || other.Iteration == null) return Iteration == other.Iteration;
      // Lambda identity (reference) + items list equality — matches
      // LambdaValue's equality discipline.
      if (!ReferenceEquals(Iteration.Lambda, other.Iteration.Lambda)) return false;
      if (Iteration.ParamName != other.Iteration.ParamName) return false;
      if (Iteration.ParamIsTemporary != other.Iteration.ParamIsTemporary) return false;
      var a = Iteration.Items;
      var b = other.Iteration.Items;
      if (a == null || b == null) return a == b;
      if (a.Count != b.Count) return false;
      for (int i = 0; i < a.Count; i++) if (!a[i].Equals(b[i])) return false;
      return true;
    }

    public override int GetHashCode()
    {
      int h = 17;
      if (Iteration?.Lambda != null) h = (h * 397) ^ Iteration.Lambda.GetHashCode();
      if (Iteration?.ParamName != null) h = (h * 397) ^ Iteration.ParamName.GetHashCode();
      h = (h * 397) ^ (Iteration?.Items?.Count ?? 0);
      return h;
    }
  }

  /// <summary>
  /// Patch produced by the revision macros <c>(replace:)</c> / <c>(append:)</c>
  /// / <c>(prepend:)</c>. Sets the descriptor's revision spec — the renderer
  /// then splices the changer's rendered hook into the targeted tree nodes
  /// instead of rendering it inline.
  /// </summary>
  public class RevisionPatch : IChangerPatch
  {
    public RevisionSpec Revision;

    public void Apply(HookDescriptor descriptor) => descriptor.Revision = Revision;

    public override bool Equals(object obj)
    {
      if (!(obj is RevisionPatch other)) return false;
      if (Revision == null || other.Revision == null) return Revision == other.Revision;
      if (Revision.Mode != other.Revision.Mode) return false;
      if (Revision.StringTarget != other.Revision.StringTarget) return false;
      if (Revision.HookTarget == null || other.Revision.HookTarget == null)
        return Revision.HookTarget == other.Revision.HookTarget;
      return Revision.HookTarget.Equals(other.Revision.HookTarget);
    }

    public override int GetHashCode()
    {
      int h = 31;
      if (Revision != null)
      {
        h = (h * 397) ^ (int)Revision.Mode;
        if (Revision.StringTarget != null) h = (h * 397) ^ Revision.StringTarget.GetHashCode();
        if (Revision.HookTarget != null) h = (h * 397) ^ Revision.HookTarget.GetHashCode();
      }
      return h;
    }
  }

  /// <summary>
  /// Patch produced by the click/hover macros (<c>(click:)</c>,
  /// <c>(mouseover-append:)</c>, …). Sets the descriptor's interaction spec —
  /// the changer then wraps the targeted nodes in
  /// <see cref="RenderInteractiveNode"/>s and registers a deferred handler
  /// the session fires on <see cref="StorySession.DispatchEvent"/>.
  /// </summary>
  public class InteractionPatch : IChangerPatch
  {
    public InteractionSpec Interaction;

    public void Apply(HookDescriptor descriptor) => descriptor.Interaction = Interaction;

    public override bool Equals(object obj)
    {
      if (!(obj is InteractionPatch other)) return false;
      if (Interaction == null || other.Interaction == null) return Interaction == other.Interaction;
      if (Interaction.Kind != other.Interaction.Kind) return false;
      if (Interaction.Mode != other.Interaction.Mode) return false;
      if (Interaction.HookTarget == null || other.Interaction.HookTarget == null)
        return Interaction.HookTarget == other.Interaction.HookTarget;
      return Interaction.HookTarget.Equals(other.Interaction.HookTarget);
    }

    public override int GetHashCode()
    {
      int h = 41;
      if (Interaction != null)
      {
        h = (h * 397) ^ (int)Interaction.Kind;
        h = (h * 397) ^ (int)Interaction.Mode;
        if (Interaction.HookTarget != null) h = (h * 397) ^ Interaction.HookTarget.GetHashCode();
      }
      return h;
    }
  }
}
