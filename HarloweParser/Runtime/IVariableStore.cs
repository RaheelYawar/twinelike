namespace Harlowe.Runtime
{
  /// <summary>
  /// Storage for the two namespaces a Harlowe story manipulates: story-scoped
  /// variables (<c>$foo</c>, persistent across passages) and passage-scoped
  /// "temporary" variables (<c>_foo</c>, cleared on every navigation). Also
  /// owns the implicit <c>it</c> slot — the most recently set value, used by
  /// shorthand expressions like <c>(set: $hp to it + 1)</c>.
  ///
  /// <para>
  /// The interface is the contract <see cref="ExpressionEvaluator"/> and the
  /// command-macro handlers are coded against; <see cref="HarloweVariableStore"/>
  /// is the production implementation. Tests can substitute their own.
  /// </para>
  /// </summary>
  public interface IVariableStore
  {
    /// <summary>
    /// Look up <paramref name="name"/> (without sigil). When
    /// <paramref name="isTemporary"/> is true, reads from the passage-scoped
    /// namespace; otherwise from the story-scoped namespace. Returns null for
    /// an unset name — callers decide whether that should become an error
    /// value or a default. Authors of macro handlers should generally treat
    /// "unset" as an error.
    /// </summary>
    HarloweValue Get(string name, bool isTemporary);

    /// <summary>
    /// Store <paramref name="value"/> under <paramref name="name"/> in the
    /// story-scoped or temporary namespace, and update the implicit <c>it</c>
    /// slot to the same value. Macros like <c>(set:)</c>, <c>(put:)</c>, and
    /// <c>(move:)</c> route through here.
    /// </summary>
    void Set(string name, bool isTemporary, HarloweValue value);

    /// <summary>
    /// Called by the navigation layer at the start of every passage render.
    /// Clears the temporary namespace; story-scoped variables and the
    /// <c>it</c> slot are untouched.
    /// </summary>
    void BeginPassage();

    /// <summary>
    /// The implicit <c>it</c> value, refreshed on every <see cref="Set"/>.
    /// Returns null before any set has occurred. Read directly by
    /// <see cref="ExpressionEvaluator"/> when it sees an
    /// <see cref="Ast.Expression.IdentifierNode"/> with name <c>it</c>.
    /// </summary>
    HarloweValue It { get; }

    /// <summary>
    /// Captures a snapshot of every namespace plus the <c>it</c> slot. The
    /// returned token is opaque to the caller; pass it back to
    /// <see cref="Restore"/> to rewind. Used by undo and by the navigation
    /// layer to support single-step rewind in v1.
    /// </summary>
    object Snapshot();

    /// <summary>
    /// Restore the store to the state captured by an earlier
    /// <see cref="Snapshot"/>. Replaces both namespaces and the <c>it</c>
    /// slot wholesale. Implementations are expected to deep-copy collections
    /// at snapshot time so a restored value is not shared with a mutated one.
    /// </summary>
    void Restore(object snapshot);
  }
}
