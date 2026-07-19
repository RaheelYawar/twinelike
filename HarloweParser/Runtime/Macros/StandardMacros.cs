namespace Harlowe.Runtime.Macros
{
  /// <summary>
  /// Convenience entry point for wiring the standard macro set onto a fresh
  /// <see cref="MacroRegistry"/>. The session calls <see cref="RegisterAll"/>
  /// once at construction; tests that want the full set use the same call.
  /// Tests that want only a subset register macros directly.
  /// </summary>
  public static class StandardMacros
  {
    /// <summary>
    /// Register every standard macro on <paramref name="registry"/>. Order
    /// does not matter; the registry is a flat name lookup. Re-registering
    /// replaces the prior implementation, so this is idempotent.
    /// </summary>
    public static void RegisterAll(MacroRegistry registry)
    {
      registry.Register(new SetMacro());
      registry.Register(new PutMacro());
      registry.Register(new MoveMacro());
      registry.Register(new UnpackMacro());
      registry.Register(new PrintMacro());
      registry.Register(new IfMacro());
      registry.Register(new ElseMacro());
      // One registration covers both spellings: "else-if" and "elseif"
      // normalize to the same key (the dash is stripped).
      registry.Register(new ElseIfMacro("else-if"));
      registry.Register(new UnlessMacro());
      registry.Register(new GotoMacro());
      registry.Register(new SaveGameMacro());
      registry.Register(new LoadGameMacro());
      registry.Register(new SavedGamesMacro());
      registry.Register(new DisplayMacro());
      registry.Register(new RandomMacro());
      registry.Register(new EitherMacro());
      registry.Register(new AMacro());
      registry.Register(new DmMacro());
      registry.Register(new ModuloMacro());
      registry.Register(new TextMacro("text"));
      registry.Register(new TextMacro("str"));
      registry.Register(new TextMacro("string"));
      registry.Register(new NumMacro("num"));
      registry.Register(new NumMacro("number"));
      registry.Register(new RoundMacro());
      registry.Register(new MinMaxMacro("min", max: false));
      registry.Register(new MinMaxMacro("max", max: true));
      registry.Register(new MathMacro("floor", System.Math.Floor));
      registry.Register(new MathMacro("ceil", System.Math.Ceiling));
      registry.Register(new MathMacro("trunc", System.Math.Truncate));
      registry.Register(new MathMacro("abs", System.Math.Abs));
      registry.Register(new MathMacro("sign", MathMacro.Sign));
      registry.Register(new StringFnMacro("uppercase", s => s.ToUpperInvariant()));
      registry.Register(new StringFnMacro("lowercase", s => s.ToLowerInvariant()));
      registry.Register(new StringFnMacro("str-reversed", StringFnMacro.Reverse));
      registry.Register(new StringFnMacro("string-reversed", StringFnMacro.Reverse));
      registry.Register(new StringFnMacro("upperfirst", s => StringFnMacro.CaseFirst(s, toUpper: true)));
      registry.Register(new StringFnMacro("lowerfirst", s => StringFnMacro.CaseFirst(s, toUpper: false)));
      registry.Register(new SubstringMacro());
      registry.Register(new WordsMacro());
      registry.Register(new StrRepeatedMacro("str-repeated"));
      registry.Register(new StrRepeatedMacro("string-repeated"));
      registry.Register(new StrNthMacro("str-nth"));
      registry.Register(new StrNthMacro("string-nth"));
      registry.Register(new HistoryMacro());
      registry.Register(new TextStyleMacro());
      registry.Register(new TextColorMacro("text-color"));
      registry.Register(new TextColorMacro("text-colour"));
      registry.Register(new TextColorMacro("color"));
      registry.Register(new TextColorMacro("colour"));
      registry.Register(new BackgroundMacro("background"));
      registry.Register(new BackgroundMacro("bg"));
      registry.Register(new RgbMacro("rgb"));
      registry.Register(new RgbMacro("rgba"));
      registry.Register(new HslMacro("hsl"));
      registry.Register(new HslMacro("hsla"));
      registry.Register(new FontMacro());
      registry.Register(new TextSizeMacro("text-size"));
      registry.Register(new TextSizeMacro("size"));
      registry.Register(new OpacityMacro());
      registry.Register(new AlignMacro());
      registry.Register(new FindMacro());
      registry.Register(new AllPassMacro());
      registry.Register(new AlteredMacro());
      registry.Register(new SomePassMacro());
      registry.Register(new NonePassMacro());
      registry.Register(new ForMacro("for"));
      registry.Register(new ForMacro("loop"));
      registry.Register(new FoldedMacro());
      registry.Register(new RotatedToMacro());
      registry.Register(new SortedMacro());
      registry.Register(new ReplaceMacro());
      registry.Register(new AppendMacro());
      registry.Register(new PrependMacro());
      registry.Register(new ChangeMacro());
      registry.Register(new EnchantMacro());
      // Plain interaction macros (combo mode null) reveal their attached hook
      // at the macro's own position; (click-rerun:) — click only, as in
      // reference — survives dispatch and re-renders per activation; the
      // -replace/-append/-prepend combos splice into the target instead.
      registry.Register(new InteractionMacro("click", InteractionKind.Click));
      registry.Register(new InteractionMacro("click-rerun", InteractionKind.Click, null, once: false));
      registry.Register(new InteractionMacro("click-replace", InteractionKind.Click, RevisionMode.Replace));
      registry.Register(new InteractionMacro("click-append", InteractionKind.Click, RevisionMode.Append));
      registry.Register(new InteractionMacro("click-prepend", InteractionKind.Click, RevisionMode.Prepend));
      registry.Register(new InteractionMacro("mouseover", InteractionKind.MouseOver));
      registry.Register(new InteractionMacro("mouseover-replace", InteractionKind.MouseOver, RevisionMode.Replace));
      registry.Register(new InteractionMacro("mouseover-append", InteractionKind.MouseOver, RevisionMode.Append));
      registry.Register(new InteractionMacro("mouseover-prepend", InteractionKind.MouseOver, RevisionMode.Prepend));
      registry.Register(new InteractionMacro("mouseout", InteractionKind.MouseOut));
      registry.Register(new InteractionMacro("mouseout-replace", InteractionKind.MouseOut, RevisionMode.Replace));
      registry.Register(new InteractionMacro("mouseout-append", InteractionKind.MouseOut, RevisionMode.Append));
      registry.Register(new InteractionMacro("mouseout-prepend", InteractionKind.MouseOut, RevisionMode.Prepend));
      // The -goto/-undo command variants (no attached hook): firing navigates
      // to the passage / undoes the turn instead of revealing content.
      registry.Register(new InteractionCommandMacro("click-goto", InteractionKind.Click, undo: false));
      registry.Register(new InteractionCommandMacro("click-undo", InteractionKind.Click, undo: true));
      registry.Register(new InteractionCommandMacro("mouseover-goto", InteractionKind.MouseOver, undo: false));
      registry.Register(new InteractionCommandMacro("mouseover-undo", InteractionKind.MouseOver, undo: true));
      registry.Register(new InteractionCommandMacro("mouseout-goto", InteractionKind.MouseOut, undo: false));
      registry.Register(new InteractionCommandMacro("mouseout-undo", InteractionKind.MouseOut, undo: true));
      // The (link:) family creates its own armed label in place of the hook;
      // the variants differ in what survives the click (see LinkMacro).
      registry.Register(new LinkMacro("link", RevisionMode.Replace, once: true, replacesLabel: true));
      registry.Register(new LinkMacro("link-replace", RevisionMode.Replace, once: true, replacesLabel: true));
      registry.Register(new LinkMacro("link-reveal", RevisionMode.Append, once: true, replacesLabel: false));
      registry.Register(new LinkMacro("link-append", RevisionMode.Append, once: true, replacesLabel: false));
      registry.Register(new LinkMacro("link-repeat", RevisionMode.Append, once: false, replacesLabel: false));
      registry.Register(new LinkMacro("link-rerun", RevisionMode.Replace, once: false, replacesLabel: false));
      registry.Register(new LinkGotoMacro());
      registry.Register(new LinkUndoMacro());
    }
  }
}
