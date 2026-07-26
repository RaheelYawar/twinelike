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
    Error,

    /// <summary>
    /// A Harlowe "changer" — a value that, when attached to a hook, modifies
    /// how the hook is rendered (e.g. <c>(text-style: "bold")</c> wraps the
    /// hook's content in <c>&lt;b&gt;</c>...<c>&lt;/b&gt;</c>). Changers compose
    /// with the <c>+</c> operator. Stored as a <see cref="Changer"/> object on
    /// <see cref="HarloweValue.Raw"/>.
    /// </summary>
    Changer,

    /// <summary>
    /// A Harlowe lambda — a parameter + clause expression carried as a value
    /// until a consuming macro (e.g. <c>(find:)</c>, <c>(altered:)</c>) fires
    /// it once per item. Lambdas don't close over scope; they re-evaluate
    /// against the caller's variable store when invoked. Stored as a
    /// <see cref="LambdaValue"/> on <see cref="HarloweValue.Raw"/>.
    /// </summary>
    Lambda,

    /// <summary>
    /// A Harlowe hook name — the <c>?name</c> reference value that targets a
    /// named hook (or a built-in like <c>?passage</c>/<c>?link</c>). It is a
    /// lazily-resolved query, not a captured node: revision and enchantment
    /// macros re-resolve it against the live render tree each time. Stored as a
    /// <see cref="HookNameValue"/> on <see cref="HarloweValue.Raw"/>.
    /// </summary>
    HookName,

    /// <summary>
    /// A Harlowe colour — built-in named values (<c>red</c>, <c>navy</c>),
    /// hex literals (<c>#a4e</c>), or the products of <c>(rgb:)</c>/<c>(hsl:)</c>.
    /// Stored as a <see cref="ColourValue"/> on <see cref="HarloweValue.Raw"/>.
    /// </summary>
    Colour,

    /// <summary>
    /// A Harlowe datatype — the keyword value (<c>num</c>, <c>str</c>,
    /// <c>even</c>) that <c>is a</c> and <c>matches</c> compare data against,
    /// and that appears inside array/datamap literals to make a pattern. Stored
    /// as a <see cref="DatatypeValue"/> on <see cref="HarloweValue.Raw"/>.
    /// </summary>
    Datatype
  }
}
