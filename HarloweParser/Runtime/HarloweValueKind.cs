namespace Harlowe.Runtime
{
  /// <summary>
  /// Tag for the runtime variant carried by a <see cref="HarloweValue"/>. Mirrors
  /// <see cref="Ast.Expression.LiteralKind"/> for the three primitive variants so
  /// the evaluator can copy <c>Kind</c>/<c>Value</c> straight from a
  /// <see cref="Ast.Expression.LiteralNode"/>; the collection variants and
  /// <see cref="Error"/> only exist at runtime.
  /// </summary>
  public enum HarloweValueKind
  {
    /// <summary>A boxed <see cref="double"/>. Harlowe has a single numeric type.</summary>
    Number,

    /// <summary>A CLR <see cref="string"/>.</summary>
    String,

    /// <summary>A boxed <see cref="bool"/>.</summary>
    Bool,

    /// <summary>A <see cref="System.Collections.Generic.List{T}"/> of <see cref="HarloweValue"/>s, in order.</summary>
    Array,

    /// <summary>A <see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> from <see cref="string"/> name to <see cref="HarloweValue"/>.</summary>
    Datamap,

    /// <summary>
    /// An evaluation error carrying a human-readable message. Propagates through
    /// every operator and macro until it reaches the renderer, where it is
    /// emitted via <see cref="IRenderOutput.Error"/> rather than thrown. This is
    /// how Harlowe surfaces in-prose errors to the author.
    /// </summary>
    Error
  }
}
