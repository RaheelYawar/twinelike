using System.Collections.Generic;
using Harlowe.Ast.Body;

namespace Harlowe.Runtime
{
  /// <summary>
  /// Walks a <see cref="PassageBody"/>, executes command macros, and pushes
  /// the visible output through an <see cref="IRenderOutput"/>. Implements
  /// <see cref="IBodyVisitor"/>; the public entry point is <see cref="Render"/>.
  ///
  /// <para>
  /// <b>Conditional rendering.</b> <c>(if:)</c>, <c>(unless:)</c>,
  /// <c>(else-if:)</c>, and <c>(else:)</c> return conditional <em>Changers</em>
  /// (reference Harlowe's model), so they flow through the same attached-hook
  /// path as styling changers and compose via <c>+</c>. Whether the hook
  /// rendered feeds <see cref="MacroContext.LastConditional"/> through
  /// <see cref="UpdateConditionalPairing"/>, which implements reference
  /// <c>section.ts</c>'s <c>lastHookShown</c> rules. Attached booleans hide or
  /// show the hook and feed the same pairing.
  /// </para>
  ///
  /// <para>
  /// <b>Changer rendering.</b> When an expression yields a
  /// <see cref="HarloweValueKind.Changer"/> and has an
  /// <see cref="MacroNode.AttachedHook"/>, the changer's open HTML is emitted,
  /// the hook contents are rendered, and the close HTML follows — see
  /// <see cref="Changer.Apply"/>. Without an attached hook a changer in prose
  /// is an in-prose error, matching reference (storing one in a variable
  /// happens during expression evaluation and never reaches this renderer).
  /// </para>
  ///
  /// <para>
  /// <b>Goto handling.</b> A successful <c>(goto:)</c> sets
  /// <see cref="MacroContext.PendingGoto"/>; the renderer checks that flag
  /// before each sibling node and inside hook recursion, aborting render the
  /// moment it appears. The session reads the flag after render to navigate.
  /// </para>
  ///
  /// <para>
  /// <b>Errors.</b> Argument-evaluation errors and macro-returned errors are
  /// pushed through <see cref="IRenderOutput.Error"/>; they never throw. An
  /// unset variable in body position emits an error and the surrounding
  /// passage continues to render.
  /// </para>
  /// </summary>
  public class BodyRenderer : IBodyVisitor
  {
    private readonly IRenderOutput _output;
    private readonly MacroRegistry _registry;
    private readonly MacroContext _context;
    private readonly ExpressionEvaluator _evaluator;

    // How many {…} spans lexically enclose the node being rendered. Printed
    // output inside one is marked to keep its whitespace, and a hook rendered
    // elsewhere (a revision or click) remembers whether its source was inside.
    private int _collapseDepth;

    public BodyRenderer(IRenderOutput output, MacroRegistry registry, MacroContext context)
    {
      _output = output;
      _registry = registry;
      _context = context;
      _evaluator = new ExpressionEvaluator(context.Store, context.EvaluationContext, registry, context.Rng);
    }

    /// <summary>
    /// Render <paramref name="body"/> into the output sink. Aborts early on
    /// <see cref="MacroContext.PendingGoto"/>. Safe with a null body or null
    /// children list.
    /// </summary>
    public void Render(PassageBody body)
    {
      if (body == null || body.Children == null) return;
      RenderChildren(body.Children);
    }

    private void RenderChildren(List<IBodyNode> children)
    {
      for (int i = 0; i < children.Count; i++)
      {
        if (_context.NavigationHalt) return;
        children[i].Accept(this);
      }
    }

    public void Visit(TextNode node) => _output.Text(node.Content);

    public void Visit(NewlineNode node) => _output.Text("\n");

    public void Visit(VariableNode node)
    {
      var value = _context.Store.Get(node.Name, node.IsTemporary);
      if (value == null)
      {
        string sigil = node.IsTemporary ? "_" : "$";
        _output.Error($"{sigil}{node.Name} is not set");
        return;
      }
      if (value.IsError) { _output.Error(value.ErrorMessage); return; }
      // A bare changer variable can't be displayed — reference errors here too.
      if (value.Kind == HarloweValueKind.Changer) { _output.Error(UnattachedChangerError(value.AsChanger)); return; }
      var preserved = BeginPrintedOutput();
      EmitPrinted(value);
      preserved?.EndWhitespace();
    }

    public void Visit(HtmlNode node) => _output.Html(node.RawHtml);

    /// <summary>
    /// A <c>[[…]]</c> link. A pure variable target (<c>[[Go-&gt;$next]]</c>) is
    /// resolved through the store first — reference evaluates markup in the
    /// passage-name position — then the target is existence-checked
    /// (<see cref="MacroContext.PassageExists"/>, the same gate <c>(goto:)</c>
    /// uses — null when no story is wired, as in standalone renderer tests, and
    /// then skipped): a link to a passage that isn't there emits its label as
    /// plain prose plus an in-prose error, and <em>no</em>
    /// <see cref="IRenderOutput.Link"/> event at all, so nothing downstream can
    /// make it clickable. That matters beyond cosmetics — a followed dead link
    /// used to strand the session on a passage that doesn't exist and write that
    /// name into the save, after which the blob could never be loaded again.
    ///
    /// <para>Reference instead renders an unclickable red
    /// <c>&lt;tw-broken-link&gt;</c> and no error (<c>ts/macrolib/links.ts</c>,
    /// gated on <c>Passages.hasValid</c>). We have no HTML to give the host, and
    /// modelling "broken link" as its own render channel isn't worth the surface
    /// — so the un-navigable half is reproduced structurally and the visible half
    /// through the error channel, which hosts already silence in production
    /// builds if they want reference's quieter behaviour.</para>
    /// </summary>
    public void Visit(LinkNode node)
    {
      string target = node.Target;
      string text = node.Text;

      // Reference evaluates markup in the passage-name position before its
      // validity gate ([[Go->$next]] — ts/macrolib/links.ts renders the name
      // and takes its text), so a pure variable target resolves through the
      // store here; the existence check below then sees the real passage name.
      if (LinkNode.TryParseVariableTarget(target, out bool isTemp, out string varName))
      {
        var value = _context.Store.Get(varName, isTemp);
        if (value == null)
        {
          if (!string.IsNullOrEmpty(text)) _output.Text(text);
          _output.Error($"{(isTemp ? "_" : "$")}{varName} is not set");
          return;
        }
        if (value.IsError)
        {
          if (!string.IsNullOrEmpty(text)) _output.Text(text);
          _output.Error(value.ErrorMessage);
          return;
        }
        // A bare [[$next]] displays the resolved name too, as in reference
        // (link text is rendered markup as well).
        if (text == target) text = value.ToHarloweString();
        target = value.ToHarloweString();
      }

      var missing = _context.MissingPassageError("link to", target);
      if (missing != null)
      {
        if (!string.IsNullOrEmpty(text)) _output.Text(text);
        _output.Error(missing.ErrorMessage);
        return;
      }
      _output.Link(text, target);
    }

    public void Visit(ParseErrorNode node) => _output.Error(node.Message);

    // A comment renders as nothing — reference's renderer eliminates the
    // commented-out token and emits nothing for htmlComment.
    public void Visit(CommentNode node) { }

    /// <summary>
    /// Render a hook's contents. When the output is a
    /// <see cref="Rendering.RenderTreeBuilder"/>, the contents are bracketed as
    /// a <see cref="Rendering.RenderHookNode"/> — the addressable unit revision
    /// and enchantment macros target. Anonymous hooks still produce a node
    /// (with a null name) so position/string targeting can find them. When the
    /// output is a plain sink (a unit-test buffer, an expression-position
    /// <c>(display:)</c> capture) there is no tree to build into, so the hook
    /// renders flat — exactly the pre-render-tree behaviour.
    ///
    /// <para>Pushes a fresh temp-variable scope for the hook body so authors
    /// who write <c>(set: _x to ...)</c> inside a hook see Harlowe's
    /// documented hook-scoped semantics: a freshly-declared temp variable
    /// dies on hook exit, but <c>(set:)</c> of an outer-scoped temp variable
    /// still updates the outer binding. Matches reference Harlowe
    /// (ts/internaltypes/varscope.ts).</para>
    /// </summary>
    public void Visit(HookNode node)
    {
      // The parser limits nesting within one source, but a recursive display
      // or print multiplies it, so the running total is checked here. Reference
      // has the same guard in renderInto, with this wording.
      if (_context.RenderNesting >= MacroContext.MaxRenderNesting)
      {
        _output.Error("Printing this expression may have trapped me in an infinite loop.");
        return;
      }
      _context.RenderNesting++;

      var builder = _output as Rendering.RenderTreeBuilder;
      builder?.BeginHook(node.Name, node.Anchor);
      // A hook is its own conditional scope. Reference Harlowe renders each
      // hook in a fresh stack frame whose lastHookShown starts unset, so a
      // conditional inside a hook can neither pair with an (else:) outside it
      // nor leak its show/hide decision back out. Start the hook body with a
      // cleared pairing and restore the outer value on exit.
      var priorConditional = _context.LastConditional;
      _context.LastConditional = null;
      using (_context.Store.PushTempScope())
      {
        if (node.Children != null) RenderChildren(node.Children);
      }
      _context.LastConditional = priorConditional;
      builder?.EndHook();
      _context.RenderNesting--;
    }

    /// <summary>
    /// Render an inline-formatted span (<c>''bold''</c> / <c>//italic//</c>) by
    /// bracketing its content in the matching <see cref="StyleSpec"/> flag —
    /// the same style channel <c>(text-style: "bold"/"italic")</c> uses, so the
    /// render tree wraps it in a <see cref="Rendering.RenderStyleNode"/> that
    /// enchant/revision macros can target. Unlike a hook, an inline format span
    /// is not its own temp-variable or conditional scope and never touches
    /// <see cref="MacroContext.LastConditional"/>, matching reference Harlowe
    /// (style markup does not affect <c>lastHookShown</c>).
    /// </summary>
    public void Visit(FormatNode node)
    {
      _output.PushStyle(StyleFor(node.Format));
      if (node.Children != null) RenderChildren(node.Children);
      _output.PopStyle();
    }

    /// <summary>
    /// Render a collapsing-whitespace span. On a render tree the span is a
    /// marked hook that <see cref="Rendering.WhitespaceCollapser"/> processes
    /// once the whole passage has run, because a later macro can still target
    /// text inside it. A plain sink has no tree to mark, so the span renders
    /// into a local one, collapses, and replays.
    /// </summary>
    public void Visit(CollapsedNode node)
    {
      _context.UsesCollapsedWhitespace = true;
      if (node.Children == null) return;

      // A span nests like a hook, so it shares the hook nesting limit.
      if (_context.RenderNesting >= MacroContext.MaxRenderNesting)
      {
        _output.Error("Printing this expression may have trapped me in an infinite loop.");
        return;
      }
      _context.RenderNesting++;

      if (_output is Rendering.RenderTreeBuilder builder)
      {
        builder.BeginWhitespace(Rendering.WhitespaceMode.Collapse);
        _collapseDepth++;
        RenderChildren(node.Children);
        _collapseDepth--;
        builder.EndWhitespace();
      }
      else
      {
        var local = new Rendering.RenderTreeBuilder();
        local.BeginWhitespace(Rendering.WhitespaceMode.Collapse);
        var inner = new BodyRenderer(local, _registry, _context) { _collapseDepth = 1 };
        inner.RenderChildren(node.Children);
        local.EndWhitespace();
        Rendering.WhitespaceCollapser.Apply(local.Root);
        Rendering.RenderTreeFlusher.Flush(local.Root, _output);
      }
      _context.RenderNesting--;
    }

    /// <summary>
    /// The <see cref="Ast.Body.InlineFormat"/> → <see cref="StyleSpec"/> mapping
    /// (the runtime half of the centralized format mapping; the delimiter half
    /// lives in <see cref="Ast.Body.InlineFormats"/>). The throwing default is a
    /// developer invariant guard — a <c>FormatNode</c> only ever carries a value
    /// the tokenizer + parser produced, so an unmapped format means a new markup
    /// type was wired up without a style here, caught loudly by tests rather than
    /// rendering silently unstyled. Not reachable from author input, so it does
    /// not breach the in-prose-error policy.
    /// </summary>
    private static StyleSpec StyleFor(Ast.Body.InlineFormat format)
    {
      var spec = new StyleSpec();
      switch (format)
      {
        case Ast.Body.InlineFormat.Bold: spec.Bold = true; break;
        case Ast.Body.InlineFormat.Italic: spec.Italic = true; break;
        case Ast.Body.InlineFormat.Strike: spec.Strikethrough = true; break;
        // Superscript markup is a semantic primitive (renders <sup>), kept
        // distinct from the (text-style:"superscript") macro's CSS effect —
        // matching reference, which renders ^^ as <sup> but the macro as a span.
        case Ast.Body.InlineFormat.Superscript: spec.Superscript = true; break;
        default:
          throw new System.ArgumentOutOfRangeException(
            nameof(format), format, "no StyleSpec mapping for this InlineFormat");
      }
      return spec;
    }

    /// <summary>
    /// Render a changer-chain node: evaluate the expression, apply if it
    /// resolves to a <see cref="HarloweValueKind.Changer"/>, and fall back to
    /// "emit value text + render hook" for any other value so non-Changer
    /// variables still render predictably (the body parser builds these for
    /// any <c>$var[hook]</c> shape regardless of what the runtime value
    /// turns out to be).
    /// </summary>
    public void Visit(ChangerChainNode node)
    {
      var result = _evaluator.Evaluate(node.Expression);
      if (result == null) return;
      if (result.IsError) { _output.Error(result.ErrorMessage); return; }

      if (result.Kind == HarloweValueKind.Changer)
      {
        // The pairing gate wants the syntactic front macro name — null for a
        // stored changer in a variable, whose hides therefore preserve the
        // pairing (reference gates on the expression's name, never a variable's).
        if (node.AttachedHook != null)
          ApplyChangerHook(result.AsChanger, node.AttachedHook, FrontMacroName(node.Expression));
        else
          _output.Error(UnattachedChangerError(result.AsChanger));
        return;
      }

      // Attached booleans hide or show the hook and feed the pairing —
      // reference section.ts: "Attached false values hide hooks as well. This
      // is special: as it prevents hooks from being run, an (else:) that
      // follows this will pass."
      if (result.Kind == HarloweValueKind.Bool && node.AttachedHook != null)
      {
        if (result.AsBool) node.AttachedHook.Accept(this);
        _context.LastConditional = result.AsBool;
        return;
      }

      // Other non-Changer values: emit the value's text form (matching the
      // existing VariableNode behaviour), then render the hook contents —
      // reference likewise declines to attach plain data and prints it in place.
      var preserved = BeginPrintedOutput();
      EmitMacroResult(result);
      preserved?.EndWhitespace();
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    public void Visit(MacroNode node)
    {
      // Pre-check the macro name so an unknown call surfaces as a clean
      // in-prose error rather than the arity / type errors a no-op registry
      // dispatch would otherwise produce. `to`/`into` mutation leakage is
      // no longer a concern here: the parser only emits assignment binary
      // ops at the top of (set:)/(put:) argument positions, so arg evaluation
      // for any other macro can't trigger a hidden assignment.
      if (!_registry.Contains(node.Name))
      {
        _output.Error($"unknown macro '{node.Name}'");
        return;
      }

      var args = new List<HarloweValue>(node.Arguments != null ? node.Arguments.Count : 0);
      if (node.Arguments != null)
      {
        // Reference's typeChecker runs before any argument executes, so a
        // wrong assignment operator anywhere means nothing assigns.
        var operatorError = _evaluator.ValidateAssignmentOperators(node.Name, node.Arguments);
        if (operatorError != null) { _output.Error(operatorError.ErrorMessage); return; }
        var argumentError = _evaluator.EvaluateMacroArguments(node.Name, node.Arguments, args);
        if (argumentError != null) { _output.Error(argumentError.ErrorMessage); return; }
      }

      // Expose the active sink for command macros (e.g. (display:)) that want
      // to render structured output directly into the parent output rather
      // than capture it as a string. Cleared on the way out so a subsequent
      // arg-eval pass (which goes through ExpressionEvaluator with no
      // surrounding BodyRenderer) sees a null sink and routes through the
      // buffered-snapshot path instead. Whatever a macro writes here keeps its
      // whitespace inside {…}, like reference's <tw-expression> content.
      var priorOutput = _context.Output;
      _context.Output = _output;
      var preserved = BeginPrintedOutput();
      HarloweValue result;
      try { result = _registry.Invoke(node.Name, args, _context); }
      finally
      {
        preserved?.EndWhitespace();
        _context.Output = priorOutput;
      }

      // The conditional pairing (LastConditional, reference's lastHookShown)
      // is only touched when a hook is shown or hidden by an attached
      // expression — never reset by a plain command macro or by intervening
      // prose. So an intervening (set:)/(print:) leaves the pairing intact and
      // a following (else:) still pairs with the original (if:), matching
      // reference Harlowe. The changer branch below records the outcome via
      // UpdateConditionalPairing after the hook renders.
      if (result != null && result.IsError) { _output.Error(result.ErrorMessage); return; }

      // (goto:) and any macro that triggered a navigation aborts now.
      if (_context.NavigationHalt) return;

      // Changer + attached hook: open / render contents / close. Conditionals
      // ((if:)/(unless:)/(else-if:)/(else:)) are changers too, so show/hide is
      // just the descriptor's Enabled bit. Unattached, a changer in prose is an
      // authoring mistake — reference errors rather than silently dropping it
      // (storing it in a variable happens during evaluation, never here).
      if (result != null && result.Kind == HarloweValueKind.Changer)
      {
        if (node.AttachedHook != null)
          ApplyChangerHook(result.AsChanger, node.AttachedHook, node.Name);
        else
          _output.Error($"The ({node.Name}:) changer should be stored in a variable or attached to a hook.");
        return;
      }

      // Attached booleans hide or show the hook and feed the pairing —
      // reference section.ts hides the hook for an attached false and lets a
      // following (else:) pass.
      if (result != null && result.Kind == HarloweValueKind.Bool && node.AttachedHook != null)
      {
        if (result.AsBool) node.AttachedHook.Accept(this);
        _context.LastConditional = result.AsBool;
        return;
      }

      // Other values: emit any visible text, then render the hook (if any)
      // unconditionally.
      if (result != null && !result.IsError)
      {
        var printed = BeginPrintedOutput();
        EmitMacroResult(result);
        printed?.EndWhitespace();
      }
      if (node.AttachedHook != null) node.AttachedHook.Accept(this);
    }

    /// <summary>
    /// Apply a changer to its attached hook and record the show/hide outcome —
    /// the shared tail of the <see cref="MacroNode"/> and
    /// <see cref="ChangerChainNode"/> changer branches.
    /// <paramref name="macroName"/> is the applying expression's front macro
    /// name, or null when it has none (a stored changer in a variable).
    /// </summary>
    private void ApplyChangerHook(Changer changer, IBodyNode hook, string macroName)
    {
      // Captured now: a click's deferred render runs after this renderer has
      // left the {…} span the macro was written in.
      bool insideCollapse = _collapseDepth > 0;
      bool shown = changer.Apply(_output, target => RenderHookInto(hook, target, insideCollapse), _context);
      UpdateConditionalPairing(macroName, shown);
    }

    /// <summary>
    /// Record a changer-attached hook's show/hide outcome on
    /// <see cref="MacroContext.LastConditional"/> — reference <c>section.ts</c>'s
    /// <c>lastHookShown</c> rules: a <em>shown</em> hook sets it true (any
    /// attached expression, not just conditionals); a <em>hidden</em> hook sets
    /// it false only when the applying expression's front macro name is
    /// <c>if</c>/<c>unless</c>/<c>else</c>. Reference gates the false-write on
    /// its name whitelist with <c>elseif</c> excluded ("(else-if:) must be
    /// special-cased, so that it doesn't affect lastHookShown, instead
    /// preserving the value of the original (if:)"), so hides from an
    /// <c>(else-if:)</c>, from a stored changer in a variable, or from a
    /// composition fronted by a non-conditional all preserve the prior value.
    /// </summary>
    private void UpdateConditionalPairing(string macroName, bool shown)
    {
      if (shown)
      {
        _context.LastConditional = true;
        return;
      }
      if (macroName == null) return;
      string name = MacroNames.Normalize(macroName);
      if (name == "if" || name == "unless" || name == "else")
        _context.LastConditional = false;
    }

    /// <summary>The front macro name of a changer-chain expression — the leftmost <see cref="Ast.Expression.MacroCallNode"/> of a <c>+</c> chain; null for a variable reference, matching reference's expression-name gate.</summary>
    private static string FrontMacroName(Ast.Expression.IExpressionNode expr)
    {
      while (expr is Ast.Expression.BinaryOpNode b) expr = b.Left;
      return (expr as Ast.Expression.MacroCallNode)?.Name;
    }

    /// <summary>The reference error for a changer used bare in prose (<c>section.ts</c>: "The (X:) changer should be stored in a variable or attached to a hook."). The name comes from the changer's creation source; unstamped changers get the generic form.</summary>
    private static string UnattachedChangerError(Changer changer)
    {
      string name = changer.FrontMacroName;
      return name != null
        ? $"The ({name}:) changer should be stored in a variable or attached to a hook."
        : "This changer should be stored in a variable or attached to a hook.";
    }

    /// <summary>
    /// Render <paramref name="hook"/> into <paramref name="target"/>. When the
    /// target is this renderer's own output, reuse this renderer; otherwise
    /// spin up a sub-renderer sharing the registry and context. The latter is
    /// how a revision changer renders its hook into a detached tree — see
    /// <see cref="Changer.Apply"/> — without touching the live output.
    ///
    /// <para>Whitespace follows where the hook was <em>written</em>, wherever
    /// it lands (reference's "lexical rather than dynamic position"). A hook
    /// written inside <c>{…}</c> collapses even when spliced outside it, and
    /// one written outside keeps its whitespace when spliced into a span.</para>
    /// </summary>
    private void RenderHookInto(IBodyNode hook, IRenderOutput target, bool insideCollapse)
    {
      if (hook == null) return;
      if (ReferenceEquals(target, _output)) { hook.Accept(this); return; }

      var renderer = new BodyRenderer(target, _registry, _context) { _collapseDepth = insideCollapse ? 1 : 0 };
      var builder = target as Rendering.RenderTreeBuilder;
      if (builder == null || (!insideCollapse && !_context.UsesCollapsedWhitespace))
      {
        hook.Accept(renderer);
        return;
      }
      builder.BeginWhitespace(insideCollapse ? Rendering.WhitespaceMode.Collapse : Rendering.WhitespaceMode.Preserve);
      hook.Accept(renderer);
      builder.EndWhitespace();
    }

    /// <summary>
    /// Inside a <c>{…}</c> span, opens a hook that keeps printed content's
    /// whitespace and returns the builder to close it on. Null, with nothing
    /// opened, everywhere else.
    /// </summary>
    private Rendering.RenderTreeBuilder BeginPrintedOutput()
    {
      if (_collapseDepth == 0) return null;
      var builder = _output as Rendering.RenderTreeBuilder;
      builder?.BeginWhitespace(Rendering.WhitespaceMode.Preserve);
      return builder;
    }

    private void EmitMacroResult(HarloweValue result)
    {
      switch (result.Kind)
      {
        case HarloweValueKind.String:
        case HarloweValueKind.Number:
        case HarloweValueKind.Bool:
        case HarloweValueKind.Array:
        case HarloweValueKind.Datamap:
        case HarloweValueKind.Colour:
        case HarloweValueKind.Datatype:
          EmitPrinted(result);
          return;
      }
    }

    /// <summary>
    /// Prints a value into prose. Strings render as markup, as in reference's
    /// <c>#runExpression</c>, which hands the printed text to <c>renderInto</c>.
    /// Arrays print their items that way, comma-separated. Every other value's
    /// text has no markup in it and goes out as it stands.
    /// </summary>
    private void EmitPrinted(HarloweValue value)
    {
      switch (value.Kind)
      {
        case HarloweValueKind.String:
          if (value.AsString.Length == 0) return;
          if (value.IsLiteralText) _output.Text(value.AsString);
          else RenderPrintedMarkup(value.AsString);
          return;
        case HarloweValueKind.Array:
          var items = value.AsArray;
          for (int i = 0; i < items.Count; i++)
          {
            if (i > 0) _output.Text(",");
            EmitPrinted(items[i]);
          }
          return;
        default:
          _output.Text(value.ToHarloweString());
          return;
      }
    }

    /// <summary>
    /// Parses a printed string and renders it in place, in its own temp-variable
    /// and conditional scope (reference's <c>renderInto</c> pushes a new stack
    /// frame). A variable can print itself, so this shares the nested-render
    /// limits with <c>(display:)</c>; the depth error is reference's wording for
    /// the same situation.
    /// </summary>
    private void RenderPrintedMarkup(string source)
    {
      if (!MayContainMarkup(source)) { _output.Text(source); return; }

      var refusal = _context.EnterNestedRender(source.Length);
      if (refusal != NestedRenderRefusal.None)
      {
        _output.Error(refusal == NestedRenderRefusal.TooDeep
          ? "Printing this expression may have trapped me in an infinite loop."
          : "Printing stopped: " + MacroContext.OverBudgetMessage);
        return;
      }

      var priorConditional = _context.LastConditional;
      _context.LastConditional = null;
      try
      {
        var tokens = new Tokens.HarloweTokenizer(_context.Profile).Tokenize(source);
        var body = new Parsing.HarloweBodyParser().Parse(tokens, source);
        using (_context.Store.PushTempScope())
          new BodyRenderer(_output, _registry, _context).Render(body);
      }
      catch (HarloweParseException ex)
      {
        _output.Error(ex.Message);
      }
      finally
      {
        _context.LastConditional = priorConditional;
        _context.ExitNestedRender();
      }
    }

    /// <summary>
    /// False when <paramref name="text"/> can't contain markup, so it can skip
    /// parsing and the nested-render budget. Tests for the characters that start
    /// body markup, the same set as the tokenizer's <c>IsBodySpecial</c>, plus
    /// the doubled delimiters <c>''</c>, <c>//</c>, <c>~~</c>, <c>^^</c> and
    /// <c>--</c>.
    /// </summary>
    private static bool MayContainMarkup(string text)
    {
      for (int i = 0; i < text.Length; i++)
      {
        char c = text[i];
        switch (c)
        {
          case '(': case '[': case ']': case '<': case '|': case '$': case '_': case '{': case '}':
            return true;
          case '\'': case '/': case '~': case '^': case '-':
            if (i + 1 < text.Length && text[i + 1] == c) return true;
            break;
        }
      }
      return false;
    }
  }
}
