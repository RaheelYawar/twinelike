using System;

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
    /// <see cref="Restore"/> to rewind. Used by the navigation layer to
    /// implement undo (one snapshot per <c>Goto</c>, popped by <c>Undo</c>).
    /// </summary>
    object Snapshot();

    /// <summary>
    /// Restore the store to the state captured by an earlier
    /// <see cref="Snapshot"/>. Replaces both namespaces and the <c>it</c>
    /// slot wholesale. Implementations are expected to deep-copy collections
    /// at snapshot time so a restored value is not shared with a mutated one.
    /// </summary>
    void Restore(object snapshot);

    /// <summary>
    /// Bind <paramref name="name"/> to <paramref name="value"/> in the
    /// namespace selected by <paramref name="isTemporary"/>, returning a token
    /// that restores the prior state on dispose. Used by
    /// <see cref="LambdaInvoker"/> to bind a lambda's parameter for the
    /// duration of a single clause evaluation. Lambdas commonly use temporary
    /// parameters (<c>_x where ...</c>); the story-sigil form (<c>$x where ...</c>)
    /// binds the story namespace and is restored on the way out, so the inner
    /// clause sees the local value without permanently mutating story state.
    ///
    /// <para>
    /// Unlike <see cref="Set"/>, this does <em>not</em> rebind the
    /// <c>it</c> slot — Harlowe lambda parameters are local; the caller's
    /// <c>it</c> survives the inner clause.
    /// </para>
    /// </summary>
    IDisposable PushBinding(string name, bool isTemporary, HarloweValue value);

    /// <summary>
    /// Bind the implicit <c>it</c> slot to <paramref name="value"/> and
    /// return a token that restores the prior <c>it</c> on dispose. Used by
    /// <see cref="LambdaInvoker"/> for the implicit-parameter lambda form
    /// (<c>where it &gt; 5</c>), where the clause body references <c>it</c>
    /// rather than a named parameter.
    /// </summary>
    IDisposable PushItBinding(HarloweValue value);

    /// <summary>
    /// The current 1-indexed lambda iteration position, or <c>null</c> when
    /// no lambda is in flight. Matches reference Harlowe's <c>pos</c>
    /// identifier — see the <c>pos</c> binding in <c>ts/datatypes/lambda.ts</c>'s
    /// <c>apply()</c> method. Documented examples:
    /// <c>(altered: via it + (str: pos), "A","B","C")</c> →
    /// <c>(a:"A1","B2","C3")</c>.
    /// </summary>
    HarloweValue Pos { get; }

    /// <summary>
    /// Bind <see cref="Pos"/> to <paramref name="oneIndexedPos"/> for the
    /// duration of a lambda iteration; the disposable restores the prior
    /// value on disposal. Callers pass the 1-indexed iteration counter (so
    /// the first iteration sets <c>pos = 1</c>).
    /// </summary>
    IDisposable PushPosBinding(int oneIndexedPos);

    /// <summary>
    /// Push a fresh empty temporary-variable scope and return a token that
    /// pops it on dispose. While the inner scope is active, lookups walk
    /// outward (inner-to-outer) until a name is found, and a
    /// <see cref="Set"/> of a previously-undefined temp variable writes to
    /// the innermost scope (does not leak out on pop). <see cref="Set"/> of
    /// a name that already exists in an outer scope writes to the outermost
    /// declaration, matching reference Harlowe's parent-walking set() loop in
    /// ts/internaltypes/varref.ts ("inner hooks can modify outer hooks'
    /// values").
    ///
    /// <para>Called by the renderer at every hook boundary so authors who
    /// write <c>(set: _x to ...)</c> inside a hook see the documented
    /// hook-scoped temp variable semantics. Story-scoped variables
    /// (<c>$foo</c>) are unaffected by this stack — they always live in a
    /// single global namespace.</para>
    /// </summary>
    IDisposable PushTempScope();
  }
}
