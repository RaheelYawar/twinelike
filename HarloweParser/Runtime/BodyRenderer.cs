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
  /// <see cref="Changer.Apply"/>. A changer with no hook of its own goes
  /// through <see cref="FollowChanger"/>, which combines it across a <c>+</c>
  /// and attaches it to the next hook or command, or reports that it can't.
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
  /// unset temp variable in body position, or an unset story variable under
  /// Harlowe 4, emits an error and the surrounding passage continues to render.
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
      _evaluator = new ExpressionEvaluator(context.Store, context.EvaluationContext, registry, context.Rng, context.Profile);
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
      // A hook rendered after the passage marks where reference's first new
      // node ends: before a leading line break, which is a <br> there; before a
      // leading expression's attached hook, which is its own element there;
      // otherwise after the leading text run or construct.
      var unit = _sentinelUnit;
      _sentinelUnit = null;
      int lead = -1, leadEnd = -1;
      if (unit != null)
      {
        FindLead(children, out lead, out leadEnd);
        if (lead < 0) PlantEnd(unit);
      }

      // Only a render after the passage looks back at the levels it's inside.
      Level level = null;
      if (_context.CollapseEachRender)
      {
        if (_levels.Count == 0) _levelsRoot = (_output as Rendering.RenderTreeBuilder)?.CurrentContainer;
        level = new Level { Nodes = children, IsSpan = _nextLevelIsSpan };
        _levels.Add(level);
      }
      _nextLevelIsSpan = false;

      var priorSiblings = _siblings;
      int priorIndex = _siblingIndex;
      var priorPlanned = _planned;
      _siblings = children;
      _planned = null;
      for (int i = 0; i < children.Count; i++)
      {
        if (level != null) level.Index = i;
        if (unit != null && unit.End == null && leadEnd >= 0 && i > leadEnd) PlantEnd(unit);
        if (_context.RenderBlocked) break;
        _siblingIndex = i;
        if (unit != null && i == lead)
        {
          if (children[i] is NewlineNode) PlantEnd(unit);
          else if (children[i] is MacroNode || children[i] is VariableNode || children[i] is ChangerChainNode) _plantBeforeHook = unit;
        }
        if (_planned != null && _planned.TryGetValue(i, out var planned))
        {
          planned?.Invoke();
          continue;
        }
        if (_frameBlocked && RunsCode(children[i])) continue;
        children[i].Accept(this);
      }
      if (unit != null && unit.End == null) PlantEnd(unit);
      if (unit != null && ReferenceEquals(_plantBeforeHook, unit)) _plantBeforeHook = null;
      if (level != null) _levels.RemoveAt(_levels.Count - 1);
      _siblings = priorSiblings;
      _siblingIndex = priorIndex;
      _planned = priorPlanned;
    }

    // ----- Collapsing what renders after the passage -----

    /// <summary>A hook rendered after the passage, collapsed on its own once it finishes.</summary>
    private sealed class CollapseUnit
    {
      public Rendering.RenderHookNode Content;
      public Rendering.RenderSentinelNode End;
      /// <summary>Whether its content collapses, from where it was written, or null to decide by where it sits.</summary>
      public bool? Collapses;
    }

    /// <summary>One sibling list being rendered, and the position in it.</summary>
    private sealed class Level
    {
      public List<IBodyNode> Nodes;
      public int Index;
      public bool IsSpan;
    }

    // Whether the hook RenderHookInto is about to visit collapses, from where it
    // was written. Null for any other hook.
    private bool? _pendingCollapses;
    // Handed from Visit(HookNode) to the RenderChildren call it makes.
    private CollapseUnit _sentinelUnit;
    // A unit whose leading expression is running: its sentinel goes before that
    // expression's attached hook, if one renders in place.
    private CollapseUnit _plantBeforeHook;
    // Set by Visit(CollapsedNode) for the sibling list it renders next.
    private bool _nextLevelIsSpan;
    // The sibling lists this renderer is inside, outermost first, and the
    // container the outermost one writes into.
    private readonly List<Level> _levels = new List<Level>();
    private Rendering.IRenderContainer _levelsRoot;

    /// <summary>The first child that renders anything, and the last of the text run it begins, or both -1 for none.</summary>
    private static void FindLead(List<IBodyNode> children, out int lead, out int leadEnd)
    {
      lead = leadEnd = -1;
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is CommentNode) continue;
        lead = leadEnd = i;
        if (children[i] is TextNode)
          while (leadEnd + 1 < children.Count && children[leadEnd + 1] is TextNode) leadEnd++;
        return;
      }
    }

    private void PlantEnd(CollapseUnit unit)
    {
      if (unit.End == null) unit.End = (_output as Rendering.RenderTreeBuilder)?.PlantSentinel();
    }

    /// <summary>
    /// Collapses a hook that finished rendering after the passage, as reference's
    /// <c>Section.execute</c> collapses each render at its end, then removes its
    /// sentinel. A render outside the page, such as one in a custom macro's body,
    /// is left for the render that prints its output.
    /// </summary>
    private void FinishUnit(CollapseUnit unit, Rendering.RenderTreeBuilder builder)
    {
      if (_context.UsesCollapsedWhitespace && ReferenceEquals(builder.Root, _context.LiveRoot))
      {
        if (!Rendering.WhitespaceCollapser.CollapseRendered(builder.Root, unit.Content, unit.End, unit.Collapses,
            PendingTextAfter(), _levelsRoot, _context))
          _context.ShowTargetWalkRefusal(builder.Root);
      }
      if (unit.End != null && !Rendering.RenderNodes.RemoveDescendant(unit.Content, unit.End)
          && !RemoveFromDetached(unit.Content.Hidden?.Detached, unit.End))
        Rendering.RenderNodes.RemoveDescendant(builder.Root, unit.End);
    }

    /// <summary>Removes <paramref name="node"/> from content a <c>(hide:)</c> set aside, which <c>(show:)</c> would otherwise put back with it.</summary>
    private static bool RemoveFromDetached(List<Rendering.RenderNode> detached, Rendering.RenderNode node)
    {
      if (detached == null) return false;
      if (detached.Remove(node)) return true;
      for (int i = 0; i < detached.Count; i++)
        if (detached[i] is Rendering.IRenderContainer container && Rendering.RenderNodes.RemoveDescendant(container, node)) return true;
      return false;
    }

    /// <summary>
    /// Whether static text is still to come after the hook just rendered, in the
    /// sibling lists this renderer is inside. Reference has that text on the page
    /// already, while expressions and hooks still to run are empty elements, so
    /// only text outside them counts.
    /// </summary>
    private Rendering.WhitespaceCollapser.PendingText PendingTextAfter()
    {
      for (int k = _levels.Count - 1; k >= 0; k--)
      {
        var level = _levels[k];
        if (!HasStaticText(level.Nodes, level.Index + 1)) continue;
        for (int j = k; j >= 0; j--)
          if (_levels[j].IsSpan) return Rendering.WhitespaceCollapser.PendingText.InSpan;
        return Rendering.WhitespaceCollapser.PendingText.AtRenderRoot;
      }
      return Rendering.WhitespaceCollapser.PendingText.None;
    }

    private static bool HasStaticText(List<IBodyNode> nodes, int from)
    {
      if (nodes == null) return false;
      for (int i = from; i < nodes.Count; i++)
      {
        switch (nodes[i])
        {
          case TextNode text when !string.IsNullOrEmpty(text.Content):
          case VerbatimNode _:
          case ParseErrorNode _:
            return true;
          case FormatNode format when HasStaticText(format.Children, 0):
            return true;
          case CollapsedNode collapsed when HasStaticText(collapsed.Children, 0):
            return true;
          case ListNode list when list.Items != null:
            foreach (var item in list.Items)
              if (HasStaticText(item?.Children, 0)) return true;
            break;
        }
      }
      return false;
    }

    // Set by an (output:) outside any custom macro, for the rest of its hook or
    // passage. Reference blocks the frame's remaining code there, while prose
    // already in the page stays.
    private bool _frameBlocked;

    // The node list being rendered and the current position in it, so a changer
    // or boolean with no hook of its own can look at what follows it. _planned
    // holds what a later position does instead of rendering its node, and a
    // null action removes the node.
    private List<IBodyNode> _siblings;
    private int _siblingIndex;
    private Dictionary<int, System.Action> _planned;

    private static bool RunsCode(IBodyNode node)
      => node is MacroNode || node is ChangerChainNode || node is VariableNode || node is HookNode || node is LinkNode;

    private void Plan(int index, System.Action action)
    {
      if (_planned == null) _planned = new Dictionary<int, System.Action>();
      _planned[index] = action;
    }

    private void PlanRemoved(int from, int to)
    {
      for (int i = from; i <= to; i++) Plan(i, null);
    }

    /// <summary>An expression that's been evaluated and won't print, whose hook is still ordinary content.</summary>
    private void PlanSpent(int index)
    {
      var node = _siblings[index];
      Plan(index, () => RenderGapAndHook(GapOf(node), HookOf(node)));
    }

    private void RenderGapAndHook(string gap, IBodyNode hook)
    {
      if (hook == null) return;
      if (!string.IsNullOrEmpty(gap)) _output.Text(gap);
      hook.Accept(this);
    }

    private static HookNode HookOf(IBodyNode node)
      => node is MacroNode macro ? macro.AttachedHook : (node as ChangerChainNode)?.AttachedHook;

    private static string GapOf(IBodyNode node)
      => node is MacroNode macro ? macro.HookGap : (node as ChangerChainNode)?.HookGap;

    /// <summary>The next position after <paramref name="index"/> holding something other than reference's <c>nextNonWhitespace</c> skips.</summary>
    private int NextContent(int index)
    {
      int next = index + 1;
      while (next < _siblings.Count && RendersNoContent(_siblings[next])) next++;
      return next;
    }

    /// <summary>Whitespace, line breaks, and comments, which leave no element in reference's DOM.</summary>
    private static bool RendersNoContent(IBodyNode node)
      => node is NewlineNode || node is CommentNode
        || (node is TextNode text && string.IsNullOrWhiteSpace(text.Content))
        || (node is HtmlNode html && IsLineBreakTag(html.RawHtml));

    /// <summary>
    /// A <c>&lt;br&gt;</c> by tag name, whatever attributes it carries. The name
    /// ends only at whitespace, <c>/</c> or <c>&gt;</c>. A browser parses
    /// <c>&lt;/br&gt;</c> as a line break too, so reference's DOM holds one.
    /// </summary>
    private static bool IsLineBreakTag(string html)
    {
      if (html == null) return false;
      int name = html.StartsWith("</", System.StringComparison.Ordinal) ? 2 : 1;
      if (html.Length <= name + 2
          || string.Compare(html, name, "br", 0, 2, System.StringComparison.OrdinalIgnoreCase) != 0)
        return false;
      char next = html[name + 2];
      return next == '>' || next == '/' || char.IsWhiteSpace(next);
    }

    private static bool IsPlus(IBodyNode node) => node is TextNode text && text.Content.Trim() == "+";

    private static bool IsExpression(IBodyNode node)
      => node is MacroNode || node is VariableNode || node is ChangerChainNode || node is LinkNode;

    /// <summary>
    /// Finds what a changer with no hook of its own applies to, following the
    /// loop in reference's <c>runExpression</c>. A <c>+</c> and another
    /// expression combine with it, and a hook or command after that takes the
    /// result. Reference evaluates the next expression either way, so an
    /// expression that can't attach is spent: it reports in the changer's place
    /// and doesn't run again, while a hook written after it still renders.
    /// </summary>
    private void FollowChanger(Changer changer, string macroName)
    {
      // Errors name the changer as reference does, by the main name of the
      // macro that made it, so (either: (colour: red)) is a (text-colour:) one.
      string name = MacroNames.CanonicalChangerName(changer.FrontMacroName ?? (macroName != null ? _registry.RegisteredName(macroName) : null));
      if (_siblings == null)
      {
        _output.Error(UnattachedChangerError(name));
        return;
      }

      int at = _siblingIndex;
      while (true)
      {
        int nearest = NextContent(at);
        int candidate = nearest;
        if (nearest < _siblings.Count && IsPlus(_siblings[nearest]))
        {
          candidate = NextContent(nearest);
          if (candidate < _siblings.Count && IsExpression(_siblings[candidate]))
          {
            var right = EvaluateAdjacent(_siblings[candidate]);
            if (right.IsError)
            {
              // Reference leaves the + in the page when the right side errors.
              _output.Error(right.Value.ErrorMessage);
              PlanSpent(candidate);
              return;
            }
            PlanRemoved(at + 1, candidate - 1);
            if (!right.IsChanger)
            {
              _output.Error(CombineError(name, right));
              PlanSpent(candidate);
              return;
            }
            changer = ExpressionEvaluator.ComposeChangers(changer, right.Value.AsChanger);
            var ownHook = HookOf(_siblings[candidate]);
            if (ownHook != null)
            {
              var composed = changer;
              Plan(candidate, () => ApplyChangerHook(composed, ownHook, macroName));
              return;
            }
            Plan(candidate, null);
            at = candidate;
            continue;
          }
        }

        if (candidate < _siblings.Count && IsExpression(_siblings[candidate]))
        {
          AttachToExpression(changer, name, candidate);
          return;
        }
        if (candidate < _siblings.Count && _siblings[candidate] is HookNode following)
        {
          // Only the whitespace before a + goes, so `(a:) + [b]` keeps its + in the page.
          PlanRemoved(at + 1, nearest - 1);
          var attached = changer;
          Plan(candidate, () => ApplyChangerHook(attached, following, macroName));
          return;
        }
        _output.Error(UnattachedChangerError(name));
        return;
      }
    }

    /// <summary>
    /// A changer directly before an expression. A command takes it and runs at
    /// its own position, so whitespace between them stays. Anything else is an
    /// error in the changer's place.
    /// </summary>
    private void AttachToExpression(Changer changer, string name, int index)
    {
      var node = _siblings[index];
      var adjacent = EvaluateAdjacent(node);
      if (adjacent.IsError)
      {
        _output.Error(adjacent.Value.ErrorMessage);
        PlanSpent(index);
        return;
      }
      if (adjacent.Attach != null)
      {
        Plan(index, () =>
        {
          adjacent.Attach(changer);
          RenderGapAndHook(GapOf(node), HookOf(node));
        });
        return;
      }
      _output.Error(adjacent.IsChanger
        ? $"Changers like ({name}:) need to be combined using + between them."
        : $"{Capitalised(adjacent.ObjectName)} can't have changers like ({name}:) attached.");
      PlanSpent(index);
    }

    private static string CombineError(string name, AdjacentExpression right)
    {
      string left = name != null ? $"a ({name}:) changer" : "a changer";
      string hint = right.IsCommand
        ? $"If you want to attach this changer to {right.ObjectName}, remove the + between them."
        : "Changers can only be added to other changers.";
      return $"I can't combine {left} with {right.ObjectName}. {hint}";
    }

    private static string Capitalised(string text)
      => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    /// <summary>An expression next to a changer, evaluated but not yet run.</summary>
    private sealed class AdjacentExpression
    {
      /// <summary>What it evaluated to. Null for an assignment, which has no value.</summary>
      public HarloweValue Value;

      /// <summary>How errors name an assignment, which reference calls an operation.</summary>
      public string OperationName;

      /// <summary>Runs the command with a changer attached. Null when it can't take one.</summary>
      public System.Action<Changer> Attach;

      public bool IsError => Value != null && Value.IsError;
      public bool IsChanger => Value != null && Value.Kind == HarloweValueKind.Changer;
      public bool IsCommand => Value != null && Value.Kind == HarloweValueKind.Command;
      public string ObjectName => OperationName ?? Value.ObjectName;
    }

    private AdjacentExpression EvaluateAdjacent(IBodyNode node)
    {
      switch (node)
      {
        case LinkNode link:
          return FromValue(HarloweValue.OfCommand(CommandValue.ForLink(link.Text, link.Target)));
        case VariableNode variable:
          return FromValue(_evaluator.ReadVariable(variable.Name, variable.IsTemporary)
            ?? HarloweValue.OfError($"{(variable.IsTemporary ? "_" : "$")}{variable.Name} is not set"));
        case ChangerChainNode chain:
          return FromValue(_evaluator.Evaluate(chain.Expression));
        default:
          return EvaluateAdjacentMacro((MacroNode)node);
      }
    }

    private AdjacentExpression FromValue(HarloweValue value)
    {
      var adjacent = new AdjacentExpression { Value = value };
      if (value.Kind != HarloweValueKind.Command) return adjacent;
      var command = value.AsCommand;
      switch (command.Kind)
      {
        case CommandKind.Hook:
          adjacent.Attach = changer => RunCommand(command, changer);
          break;
        case CommandKind.Link:
          adjacent.Attach = changer =>
          {
            // Reference's (link-goto:) refuses a hook transition, which would
            // have no effect on a passage link, and shows only the error.
            if (changer.HasHookTransition)
              _output.Error("Please attach (transition-depart:) or (transition-arrive:) to a passage link, not (transition:).");
            else
              ApplyToCommand(changer, renderer => renderer.Visit(command.Link), aroundLink: true);
          };
          break;
        case CommandKind.Builtin:
          var role = BuiltinCommands.RoleOf(command.BuiltinName);
          if (role == CommandRole.Takes)
            adjacent.Attach = changer => ApplyToCommand(changer, renderer => renderer.RunBuiltin(command.BuiltinName, command.Arguments));
          else if (role == CommandRole.Reveals)
            adjacent.Attach = changer => RevealWith(changer, command.Arguments);
          else if (role == CommandRole.Ignores)
            adjacent.Attach = _ => RunBuiltin(command.BuiltinName, command.Arguments);
          break;
      }
      return adjacent;
    }

    private AdjacentExpression EvaluateAdjacentMacro(MacroNode macro)
    {
      var arguments = macro.Arguments ?? new List<Ast.Expression.IExpressionNode>();
      if (MacroNames.IsCustomCall(macro.Name))
        return FromValue(_evaluator.CallCustomMacro(macro.Name, arguments));

      // An assignment takes effect when it's evaluated, even though it then
      // can't attach: 3.3.9 assigns and reports the error.
      if (BuiltinCommands.RoleOf(macro.Name) == CommandRole.Operation)
      {
        var error = _evaluator.ValidateAssignmentOperators(macro.Name, arguments)
          ?? _evaluator.EvaluateMacroArguments(macro.Name, arguments, new List<HarloweValue>());
        if (error != null) return new AdjacentExpression { Value = error };
        return new AdjacentExpression { OperationName = $"a ({BuiltinCommands.CanonicalName(macro.Name)}:) operation" };
      }

      return FromValue(_evaluator.Evaluate(new Ast.Expression.MacroCallNode
      {
        Name = macro.Name,
        Arguments = arguments,
        Line = macro.Line,
        Column = macro.Column,
      }));
    }

    /// <summary>
    /// Applies a changer to a command's output. <c>(output:)</c> instead takes
    /// the command's place as the macro's result, with nothing to show, because
    /// a built-in command has no hook for it to capture.
    /// </summary>
    private void ApplyToCommand(Changer changer, System.Action<BodyRenderer> run, bool aroundLink = false)
    {
      // As in ApplyChangerHook: the command runs on its own.
      if (WritesIntoCutContent)
      {
        run(this);
        return;
      }

      if (changer.HasOutputPatch)
      {
        CaptureOutput(changer, new HookNode { Children = new List<IBodyNode>() });
        return;
      }
      if (changer.Live != null)
      {
        // Reference's own refusal, and the command doesn't run.
        _output.Error("I currently can't attach (live:) or (event:) macros to commands - only hooks.");
        return;
      }
      bool insideCollapse = _collapseDepth > 0;
      var command = new CommandRun(run);
      changer.Apply(_output, target => RenderHookInto(command, target, insideCollapse), _context, aroundLink);
    }

    /// <summary>
    /// <c>(show:)</c> with a changer written before it. The changer goes on the
    /// hooks it reveals rather than around the command. <c>(output:)</c> takes
    /// the command's place, as with any other built-in command.
    /// </summary>
    private void RevealWith(Changer changer, List<HarloweValue> args)
    {
      if (changer.HasOutputPatch)
      {
        CaptureOutput(changer, new HookNode { Children = new List<IBodyNode>() });
        return;
      }
      Macros.ShowMacro.Reveal(args, _context, _output, changer);
    }

    /// <summary>Lets a command go through the changer pipeline in the place of a hook.</summary>
    private sealed class CommandRun : IBodyNode
    {
      private readonly System.Action<BodyRenderer> _run;

      public CommandRun(System.Action<BodyRenderer> run) { _run = run; }

      public void Accept(IBodyVisitor visitor) => _run((BodyRenderer)visitor);
    }

    /// <summary>A built-in command, with its arguments already evaluated, printing what it returns.</summary>
    private void RunBuiltin(string name, List<HarloweValue> args)
    {
      var result = InvokeBuiltin(name, args);
      if (result == null) return;
      if (result.IsError)
      {
        _output.Error(result.ErrorMessage);
        return;
      }
      if (_context.RenderBlocked) return;
      var printed = BeginPrintedOutput();
      EmitMacroResult(result);
      printed?.EndWhitespace();
    }

    /// <summary>
    /// An attached <c>false</c> hides the next hook and <c>true</c> shows it,
    /// and neither removes the whitespace between. A boolean prints nothing
    /// itself, because reference's <c>runExpression</c> prints only strings,
    /// numbers, data structures and values with a print form.
    /// </summary>
    private void PlanBooleanHook(bool shown)
    {
      if (_siblings == null) return;
      int next = NextContent(_siblingIndex);
      if (next >= _siblings.Count || !(_siblings[next] is HookNode hook)) return;
      Plan(next, () => ApplyBoolean(shown, hook));
    }

    private void ApplyBoolean(bool shown, HookNode hook)
    {
      if (shown) hook.Accept(this);
      else PlantHiddenHook(hook);
      _context.LastConditional = shown;
    }

    public void Visit(TextNode node) => _output.Text(node.Content);

    public void Visit(NewlineNode node) => _output.Text("\n");

    public void Visit(VariableNode node)
    {
      var value = _evaluator.ReadVariable(node.Name, node.IsTemporary);
      if (value == null)
      {
        string sigil = node.IsTemporary ? "_" : "$";
        _output.Error($"{sigil}{node.Name} is not set");
        return;
      }
      if (value.IsError) { _output.Error(value.ErrorMessage); return; }
      // A bare changer can't be displayed: it attaches to what follows, or errors.
      if (value.Kind == HarloweValueKind.Changer)
      {
        FollowChanger(value.AsChanger, null);
        return;
      }
      if (value.Kind == HarloweValueKind.Bool)
      {
        PlanBooleanHook(value.AsBool);
        return;
      }
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
        // A target that can't be read is an error in place of the whole link,
        // label included, where a readable target naming no passage below is
        // a broken link that keeps its label.
        var value = _evaluator.ReadVariable(varName, isTemp);
        if (value == null)
        {
          _output.Error($"{(isTemp ? "_" : "$")}{varName} is not set");
          return;
        }
        if (value.IsError)
        {
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
      bool? collapses = _pendingCollapses;
      _pendingCollapses = null;
      if (node.IsHidden)
      {
        PlantHiddenHook(node);
        return;
      }

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
      // After the passage, every hook's render collapses on its own when it ends.
      CollapseUnit unit = null;
      if (builder != null && _context.CollapseEachRender)
        unit = new CollapseUnit { Content = (Rendering.RenderHookNode)builder.CurrentContainer, Collapses = collapses };
      // A hook is its own conditional scope. Reference Harlowe renders each
      // hook in a fresh stack frame whose lastHookShown starts unset, so a
      // conditional inside a hook can neither pair with an (else:) outside it
      // nor leak its show/hide decision back out. Start the hook body with a
      // cleared pairing and restore the outer value on exit.
      var priorConditional = _context.LastConditional;
      bool priorBlocked = _frameBlocked;
      _context.LastConditional = null;
      _frameBlocked = false;
      using (_context.Store.PushTempScope())
      {
        _sentinelUnit = unit;
        if (node.Children != null) RenderChildren(node.Children);
        _sentinelUnit = null;
      }
      _frameBlocked = priorBlocked;
      _context.LastConditional = priorConditional;
      builder?.EndHook();
      if (unit != null) FinishUnit(unit, builder);
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
      if (RendersNothing(node.Children)) return;
      _output.PushStyle(StyleFor(node.Format));
      RenderChildren(node.Children);
      _output.PopStyle();
    }

    /// <summary>
    /// True for a span with no content to render, like <c>****</c>. Reference's
    /// <c>renderTag</c> drops the element when its rendered HTML is empty, and
    /// only comments and other empty spans render to nothing at that stage.
    /// An unclosed <c>{=</c> doesn't go through <c>renderTag</c>, so it counts
    /// as content.
    /// </summary>
    private static bool RendersNothing(List<IBodyNode> children)
    {
      if (children == null) return true;
      for (int i = 0; i < children.Count; i++)
      {
        if (children[i] is CommentNode) continue;
        if (children[i] is FormatNode format && RendersNothing(format.Children)) continue;
        if (children[i] is CollapsedNode collapsed && collapsed.UnclosedMarker == null && RendersNothing(collapsed.Children)) continue;
        return false;
      }
      return true;
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
        builder.BeginSpan();
        _collapseDepth++;
        _nextLevelIsSpan = true;
        RenderChildren(node.Children);
        _collapseDepth--;
        builder.EndWhitespace();
      }
      else
      {
        var local = new Rendering.RenderTreeBuilder();
        local.BeginSpan();
        var inner = new BodyRenderer(local, _registry, _context) { _collapseDepth = 1 };
        inner.RenderChildren(node.Children);
        local.EndWhitespace();
        Rendering.WhitespaceCollapser.Apply(local.Root);
        Rendering.RenderTreeFlusher.Flush(local.Root, _output);
      }
      _context.RenderNesting--;
    }

    /// <summary>
    /// Prints a verbatim span as written. Inside <c>{…}</c> its whitespace is
    /// kept, as reference's <c>collapse()</c> treats <c>&lt;tw-verbatim&gt;</c>
    /// like printed output.
    /// </summary>
    public void Visit(VerbatimNode node)
    {
      var preserved = BeginPrintedOutput();
      _output.Text(node.Text);
      preserved?.EndWhitespace();
    }

    public void Visit(HorizontalRuleNode node) => _output.Html("<hr>");

    /// <summary>
    /// A list, as reference's renderer writes it: one <c>&lt;ul&gt;</c> or
    /// <c>&lt;ol&gt;</c>, more opened or closed as an item's depth changes, and
    /// each item in an <c>&lt;li&gt;</c>. Every tag is its own
    /// <see cref="IRenderOutput.Html"/> event and they always balance, where
    /// reference closes one level more than it opened. Items have no temp or
    /// conditional scope of their own, so an <c>(if:)</c> in one item pairs
    /// with an <c>(else:)</c> in the next.
    /// </summary>
    public void Visit(ListNode node)
    {
      if (node.Items == null) return;
      string open = node.Ordered ? "<ol>" : "<ul>";
      string close = node.Ordered ? "</ol>" : "</ul>";
      _output.Html(open);
      int depth = 1;
      foreach (var item in node.Items)
      {
        if (item == null) continue;
        int itemDepth = System.Math.Max(1, item.Depth);
        for (; depth < itemDepth; depth++) _output.Html(open);
        for (; depth > itemDepth; depth--) _output.Html(close);
        _output.Html("<li>");
        if (item.Children != null) RenderChildren(item.Children);
        _output.Html("</li>");
      }
      for (; depth > 0; depth--) _output.Html(close);
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
        // Reference documents these as "identical to italics and boldface by
        // default", and a host has no use for a separate semantic flag.
        case Ast.Body.InlineFormat.Emphasis: spec.Italic = true; break;
        case Ast.Body.InlineFormat.Strong: spec.Bold = true; break;
        default:
          throw new System.ArgumentOutOfRangeException(
            nameof(format), format, "no StyleSpec mapping for this InlineFormat");
      }
      return spec;
    }

    /// <summary>
    /// A variable with a hook after it. The pairing gate wants the syntactic
    /// front macro name, which is null for a stored changer, so its hides
    /// preserve the pairing (reference gates on the expression's name).
    /// </summary>
    public void Visit(ChangerChainNode node)
    {
      var result = _evaluator.Evaluate(node.Expression);
      if (result == null) return;
      FinishExpression(result, node.AttachedHook, node.HookGap, FrontMacroName(node.Expression));
    }

    public void Visit(MacroNode node)
    {
      if (MacroNames.IsCustomCall(node.Name))
      {
        var arguments = node.Arguments ?? new List<Ast.Expression.IExpressionNode>();
        FinishExpression(_evaluator.CallCustomMacro(node.Name, arguments), node.AttachedHook, node.HookGap, node.Name);
        return;
      }

      // Pre-check the macro name so an unknown call surfaces as a clean
      // in-prose error rather than the arity / type errors a no-op registry
      // dispatch would otherwise produce. `to`/`into` mutation leakage is
      // no longer a concern here: the parser only emits assignment binary
      // ops at the top of (set:)/(put:) argument positions, so arg evaluation
      // for any other macro can't trigger a hidden assignment.
      if (!_registry.Contains(node.Name))
      {
        _output.Error($"unknown macro '{node.Name}'");
        RenderGapAndHook(node.HookGap, node.AttachedHook);
        return;
      }

      var args = new List<HarloweValue>(node.Arguments != null ? node.Arguments.Count : 0);
      if (node.Arguments != null)
      {
        // Reference's typeChecker runs before any argument executes, so a
        // wrong assignment operator anywhere means nothing assigns.
        var argumentError = _evaluator.ValidateAssignmentOperators(node.Name, node.Arguments)
          ?? _evaluator.EvaluateMacroArguments(node.Name, node.Arguments, args);
        if (argumentError != null)
        {
          _output.Error(argumentError.ErrorMessage);
          RenderGapAndHook(node.HookGap, node.AttachedHook);
          return;
        }
      }

      var result = InvokeBuiltin(node.Name, args);
      // A custom command keeps its changer and saves through its source, and a
      // composed one needs both halves stamped. Other changers here are used
      // and dropped, so they skip the cost of building one.
      if (result != null && result.Kind == HarloweValueKind.Changer
          && (result.AsChanger.HasOutputPatch || PlusFollows()))
        ExpressionEvaluator.StampChangerSource(result, _registry.RegisteredName(node.Name), args);
      FinishExpression(result, node.AttachedHook, node.HookGap, node.Name);
    }

    private bool PlusFollows()
    {
      if (_siblings == null) return false;
      int next = NextContent(_siblingIndex);
      return next < _siblings.Count && IsPlus(_siblings[next]);
    }

    /// <summary>
    /// Runs a built-in macro. It sees this renderer's output, so a command like
    /// <c>(display:)</c> renders structured content straight into the page. The
    /// sink is cleared afterwards so argument evaluation, which has no renderer
    /// around it, takes the buffered path. Whatever a macro writes here keeps
    /// its whitespace inside <c>{…}</c>, like reference's
    /// <c>&lt;tw-expression&gt;</c> content.
    /// </summary>
    private HarloweValue InvokeBuiltin(string name, List<HarloweValue> args)
    {
      var priorOutput = _context.Output;
      _context.Output = _output;
      var preserved = BeginPrintedOutput();
      try { return _registry.Invoke(name, args, _context); }
      finally
      {
        preserved?.EndWhitespace();
        _context.Output = priorOutput;
      }
    }

    /// <summary>
    /// What an expression's result does in prose, following reference's
    /// <c>runExpression</c>. A changer takes the hook and removes the whitespace
    /// before it. An error replaces the expression and a boolean shows or hides
    /// the hook, and both leave that whitespace. Other values print in place,
    /// with the whitespace and hook after them as ordinary content.
    ///
    /// <para>The conditional pairing (<see cref="MacroContext.LastConditional"/>,
    /// reference's <c>lastHookShown</c>) changes only when an attached expression
    /// shows or hides a hook, so an intervening <c>(set:)</c> or prose leaves an
    /// <c>(if:)</c>…<c>(else:)</c> pair intact.</para>
    /// </summary>
    private void FinishExpression(HarloweValue result, HookNode hook, string gap, string macroName)
    {
      if (result != null && result.IsError)
      {
        _output.Error(result.ErrorMessage);
        RenderGapAndHook(gap, hook);
        return;
      }

      // (goto:) and any macro that triggered a navigation aborts now.
      if (_context.RenderBlocked) return;

      if (result != null && result.Kind == HarloweValueKind.Changer)
      {
        if (hook != null) ApplyChangerHook(result.AsChanger, hook, macroName);
        else FollowChanger(result.AsChanger, macroName);
        return;
      }

      if (result != null && result.Kind == HarloweValueKind.Bool)
      {
        if (hook == null)
        {
          PlanBooleanHook(result.AsBool);
          return;
        }
        if (!string.IsNullOrEmpty(gap)) _output.Text(gap);
        ApplyBoolean(result.AsBool, hook);
        return;
      }

      if (result != null)
      {
        var printed = BeginPrintedOutput();
        EmitMacroResult(result);
        printed?.EndWhitespace();
      }
      RenderGapAndHook(gap, hook);
    }

    /// <summary>
    /// Apply a changer to its hook and record the show/hide outcome.
    /// <paramref name="macroName"/> is the applying expression's front macro
    /// name, or null when it has none (a stored changer in a variable).
    /// </summary>
    private void ApplyChangerHook(Changer changer, IBodyNode hook, string macroName)
    {
      // In content a revision has emptied out of its target, reference's
      // changer can't find its hook any more, which still renders on its own.
      if (WritesIntoCutContent)
      {
        hook.Accept(this);
        return;
      }

      if (changer.HasOutputPatch)
      {
        CaptureOutput(changer, hook);
        return;
      }

      var live = changer.Live;
      if (live != null)
      {
        PlantLiveHook(changer, hook as HookNode, live);
        // Reference's live descriptor is disabled, which only a conditional's
        // own name lets reach the (else:) pairing.
        UpdateConditionalPairing(macroName, false);
        return;
      }

      if (hook is HookNode written && written.IsHidden)
      {
        ApplyToHiddenHook(changer, written, macroName);
        return;
      }

      // Captured now: a click's deferred render runs after this renderer has
      // left the {…} span the macro was written in.
      bool insideCollapse = _collapseDepth > 0;
      bool shown = changer.Apply(_output, target => RenderHookInto(hook, target, insideCollapse), _context);
      if (!shown) PlantHiddenHook(hook);
      UpdateConditionalPairing(macroName, shown);
    }

    /// <summary>
    /// True when this renderer writes directly into content that a revision
    /// has taken off its target, so a changer here has lost its hook. See
    /// <see cref="Rendering.RenderTreeBuilder.WritesIntoCutContent"/>.
    /// </summary>
    private bool WritesIntoCutContent => _output is Rendering.RenderTreeBuilder builder && builder.WritesIntoCutContent();

    /// <summary>
    /// A changer attached to a hook written hidden, measured on 3.3.9. The hook
    /// stays hidden, and an <c>(else:)</c> still pairs with the changer's own
    /// condition. Styles, revisions and <c>(for:)</c> don't apply when it's
    /// shown. A <c>(link:)</c> shows its label once the hook is revealed. A
    /// <c>(click:)</c> on another hook is armed now, and fills this one.
    /// </summary>
    private void ApplyToHiddenHook(Changer changer, HookNode hook, string macroName)
    {
      bool enabled = changer.IsEnabled;
      UpdateConditionalPairing(macroName, enabled);

      if (changer.HasLinkInteraction)
      {
        PlantHiddenHook(hook, changer);
        return;
      }
      if (!enabled || !changer.HasInteraction || !(_output is Rendering.RenderTreeBuilder builder))
      {
        PlantHiddenHook(hook);
        return;
      }

      bool insideCollapse = _collapseDepth > 0;
      var body = new HookNode { Children = hook.Children };
      builder.BeginHook(hook.Name, hook.Anchor, HiddenContentFor(hook, null));
      changer.Apply(_output, target => RenderHookInto(body, target, insideCollapse), _context);
      builder.EndHook();
    }

    /// <summary>
    /// A named hook that's hidden stays on the page, empty, so a later
    /// <c>(replace:)</c> can fill it and <c>(show:)</c> can reveal it, as
    /// reference keeps the hidden <c>&lt;tw-hook&gt;</c> in the DOM.
    /// <paramref name="shownThrough"/> is a changer to apply when it's revealed.
    /// </summary>
    private void PlantHiddenHook(IBodyNode hook, Changer shownThrough = null)
    {
      if (!(hook is HookNode named) || named.Name == null) return;
      if (!(_output is Rendering.RenderTreeBuilder builder)) return;
      builder.BeginHook(named.Name, named.Anchor, HiddenContentFor(named, shownThrough));
      builder.EndHook();
    }

    /// <summary>
    /// A hook with <c>(live:)</c> or <c>(after:)</c> attached. It's planted
    /// empty and hidden, so <c>(show:)</c> and revisions can still reach it,
    /// and registered for <see cref="StorySession.Tick"/> to render when its
    /// time comes, with the temp variables in view here and the rest of the
    /// changer. A live hook in a custom macro's body never renders, as
    /// reference's <c>inDOM</c> check stops one whose section isn't on the page.
    /// </summary>
    private void PlantLiveHook(Changer changer, HookNode hook, LiveSpec live)
    {
      if (hook == null || !(_output is Rendering.RenderTreeBuilder builder)) return;
      builder.BeginHook(hook.Name, hook.Anchor, HiddenContentFor(hook, null));
      var node = (Rendering.RenderHookNode)builder.CurrentContainer;
      builder.EndHook();
      if (_context.CurrentCustomMacro != null) return;
      if (_context.LiveHooks.Count >= MacroContext.MaxLiveHooks)
      {
        _output.Error($"There are too many (live:) and (after:) hooks running on this passage. Only {MacroContext.MaxLiveHooks} can run at once.");
        return;
      }

      var scopes = _context.Store.CaptureScopeChain();
      var body = new HookNode { Children = hook.Children };
      var registry = _registry;
      var context = _context;
      _context.LiveHooks.Add(new LiveHook
      {
        Node = node,
        Delay = live.Delay,
        Remaining = live.Delay,
        After = live.After,
        Skip = live.Skip,
        Render = (target, root) =>
        {
          var roots = new List<Rendering.RenderNode> { root };
          bool insideCollapse = Rendering.RenderNodes.IsInsideCollapse(roots, target, out var owner);
          var into = new Rendering.RenderTreeBuilder(target, owner ?? root);
          var renderer = new BodyRenderer(into, registry, context) { _collapseDepth = insideCollapse ? 1 : 0 };
          var priorOutput = context.Output;
          context.Output = into;
          try
          {
            using (context.Store.EnterScopeChain(scopes))
              changer.ApplyLive(into, output => renderer.RenderHookInto(body, output, insideCollapse), context);
          }
          finally
          {
            context.Output = priorOutput;
            into.FinishBase();
          }
        },
      });
    }

    /// <summary>
    /// What a hidden hook renders when it's shown: its contents, in the temp
    /// variables in view here, as reference stores <c>tempVariables</c> on the
    /// element. The contents go in an anonymous hook, so the hook's name isn't
    /// on the page twice.
    /// </summary>
    private Rendering.HiddenHookContent HiddenContentFor(HookNode hook, Changer shownThrough)
    {
      var scopes = _context.Store.CaptureScopeChain();
      var body = new HookNode { Children = hook.Children };
      var registry = _registry;
      var context = _context;
      return new Rendering.HiddenHookContent
      {
        Cost = ContentCost(hook.Children),
        Render = (target, insideCollapse) =>
        {
          var renderer = new BodyRenderer(target, registry, context) { _collapseDepth = insideCollapse ? 1 : 0 };
          using (context.Store.EnterScopeChain(scopes))
          {
            if (shownThrough != null)
              shownThrough.Apply(target, inner => renderer.RenderHookInto(body, inner, insideCollapse), context);
            else
              renderer.RenderHookInto(body, target, insideCollapse);
          }
        },
      };
    }

    /// <summary>A rough measure of a hook's size: its nodes and text. Bounded by the parser's nesting limit.</summary>
    private static int ContentCost(List<IBodyNode> nodes)
    {
      if (nodes == null) return 0;
      long cost = nodes.Count;
      for (int i = 0; i < nodes.Count && cost < int.MaxValue; i++)
      {
        switch (nodes[i])
        {
          case TextNode text: cost += text.Content?.Length ?? 0; break;
          case HookNode hook: cost += ContentCost(hook.Children); break;
          case FormatNode format: cost += ContentCost(format.Children); break;
          case CollapsedNode collapsed: cost += ContentCost(collapsed.Children); break;
          case ListNode list when list.Items != null:
            for (int k = 0; k < list.Items.Count && cost < int.MaxValue; k++)
              cost += 1 + ContentCost(list.Items[k]?.Children);
            break;
          case MacroNode macro when macro.AttachedHook != null: cost += ContentCost(macro.AttachedHook.Children); break;
          case ChangerChainNode chain when chain.AttachedHook != null: cost += ContentCost(chain.AttachedHook.Children); break;
        }
      }
      return (int)System.Math.Min(cost, int.MaxValue);
    }

    /// <summary>
    /// The hook attached to <c>(output:)</c>: it doesn't render, and becomes the
    /// running custom macro's result, a command holding the hook, its changer
    /// and the temp variables in view. With no macro running, reference
    /// suppresses the hook and blocks the rest of this frame's code, with no error.
    /// </summary>
    private void CaptureOutput(Changer changer, IBodyNode hook)
    {
      var frame = _context.CurrentCustomMacro;
      if (frame == null)
      {
        _frameBlocked = true;
        return;
      }
      var children = hook is HookNode hookNode ? hookNode.Children : new List<IBodyNode> { hook };
      frame.Output = HarloweValue.OfCommand(CommandValue.ForHook(
        changer, new CodeHookValue(null, children ?? new List<IBodyNode>()), _context.Store.CaptureTemps()));
      frame.Done = true;
    }

    /// <summary>
    /// Runs a command that has reached prose. <c>(output-data:)</c> and
    /// <c>(error:)</c> hand their value to the running custom macro, which stops
    /// its body. A custom command renders its hook through its changer, with the
    /// temp variables it captured added to the ones in view here.
    /// <paramref name="attached"/> is a changer written just before the command,
    /// which applies outside the command's own.
    /// </summary>
    private void RunCommand(CommandValue command, Changer attached = null)
    {
      if (command.Kind == CommandKind.Link)
      {
        Visit(command.Link);
        return;
      }
      if (command.Kind == CommandKind.Builtin)
      {
        RunBuiltin(command.BuiltinName, command.Arguments);
        return;
      }

      if (command.Kind != CommandKind.Hook)
      {
        var frame = _context.CurrentCustomMacro;
        if (frame == null)
        {
          string name = command.Kind == CommandKind.OutputData ? "output-data" : "error";
          _output.Error($"({name}:) should only be used inside a code hook passed to (macro:).");
          return;
        }
        frame.Output = command.Kind == CommandKind.OutputData
          ? command.Data
          : HarloweValue.OfError(command.Data.AsString);
        frame.Done = true;
        return;
      }

      // (output:) takes the command's hook as its own result. Reference's capture
      // reads only the hook, so the temps come from where (output:) is running.
      if (attached != null && attached.HasOutputPatch)
      {
        CaptureOutput(attached, new HookNode { Children = command.Hook.ChildrenFor(_context.Profile) });
        return;
      }

      // A command can print itself more than once, so without this charge its
      // renders would double at every level of nesting.
      var refusal = _context.EnterCustomMacro(command.Hook.Source.Length);
      if (refusal != NestedRenderRefusal.None)
      {
        _output.Error(refusal == NestedRenderRefusal.TooDeep
          ? "Printing this expression may have trapped me in an infinite loop."
          : "Printing stopped: " + MacroContext.OverBudgetMessage);
        return;
      }
      try
      {
        var hook = new HookNode { Children = command.Hook.ChildrenFor(_context.Profile) };
        bool insideCollapse = _collapseDepth > 0;
        var changer = attached != null ? attached.Compose(command.Changer) : command.Changer;
        using (_context.Store.PushCapturedTemps(command.Temps))
          changer.Apply(_output, target => RenderHookInto(hook, target, insideCollapse), _context);
      }
      finally
      {
        _context.ExitCustomMacro();
      }
    }

    /// <summary>Renders a printed code hook's contents, in its own scope, under the limits on nested renders.</summary>
    private void RenderCodeHook(CodeHookValue hook)
    {
      var refusal = _context.EnterNestedRender(hook.Source.Length);
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
        using (_context.Store.PushTempScope())
          new BodyRenderer(_output, _registry, _context).Render(new PassageBody { Children = hook.ChildrenFor(_context.Profile) });
      }
      finally
      {
        _context.LastConditional = priorConditional;
        _context.ExitNestedRender();
      }
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

    /// <summary>The reference error for a changer used bare in prose (<c>section.ts</c>: "The (X:) changer should be stored in a variable or attached to a hook."). A stored changer with no creation source gets the generic form.</summary>
    private static string UnattachedChangerError(string name)
    {
      return name != null
        ? $"The ({name}:) changer should be stored in a variable or attached to a hook."
        : "This changer should be stored in a variable or attached to a hook.";
    }

    /// <summary>
    /// Render <paramref name="hook"/> into <paramref name="target"/>. When the
    /// target is this renderer's own output, reuse this renderer; otherwise
    /// spin up a sub-renderer sharing the registry and context. The latter is
    /// how a revision changer renders its hook into each target's placeholder,
    /// and a click its deferred hook. The whitespace layer and the hook's own
    /// node it opens there count as that builder's layers.
    ///
    /// <para>Whitespace follows where the hook was <em>written</em>, wherever
    /// it lands (reference's "lexical rather than dynamic position"). A hook
    /// written inside <c>{…}</c> collapses even when rendered outside it, and
    /// one written outside keeps its whitespace when rendered into a span.</para>
    /// </summary>
    private void RenderHookInto(IBodyNode hook, IRenderOutput target, bool insideCollapse)
    {
      if (hook == null) return;
      if (ReferenceEquals(target, _output))
      {
        // An attached hook renders in place, as reference's own element after
        // its expression's, so a leading expression's first node ends here.
        if (_plantBeforeHook != null) PlantEnd(_plantBeforeHook);
        _plantBeforeHook = null;
        _pendingCollapses = insideCollapse;
        hook.Accept(this);
        _pendingCollapses = null;
        return;
      }

      var renderer = new BodyRenderer(target, _registry, _context) { _collapseDepth = insideCollapse ? 1 : 0, _pendingCollapses = insideCollapse };
      var builder = target as Rendering.RenderTreeBuilder;
      if (builder == null)
      {
        hook.Accept(renderer);
        return;
      }
      // The whitespace layer and the hook's own node are layers of whatever
      // this renders into, not content: reference has no element for either.
      bool marked = !insideCollapse && !_context.UsesCollapsedWhitespace;
      if (!marked)
      {
        builder.BeginWhitespace(insideCollapse ? Rendering.WhitespaceMode.Collapse : Rendering.WhitespaceMode.Preserve);
        builder.ExtendLayers();
      }
      builder.NextHookIsLayer = hook is HookNode;
      hook.Accept(renderer);
      builder.NextHookIsLayer = false;
      if (!marked) builder.EndWhitespace();
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
        case HarloweValueKind.Array:
        case HarloweValueKind.Datamap:
        case HarloweValueKind.Dataset:
        case HarloweValueKind.Colour:
        case HarloweValueKind.Datatype:
        case HarloweValueKind.CodeHook:
        case HarloweValueKind.CustomMacro:
        case HarloweValueKind.Command:
          EmitPrinted(result);
          return;
      }
    }

    /// <summary>
    /// Prints a value into prose. Strings render as markup, as in reference's
    /// <c>#runExpression</c>, which hands the printed text to <c>renderInto</c>.
    /// Arrays and datasets print their items that way, comma-separated, a
    /// dataset in the order its values were added. Every other value's text has
    /// no markup in it and goes out as it stands. A value can be nested far
    /// deeper than the call stack allows, so past
    /// <see cref="HarloweValue.MaxPrintDepth"/> levels this reports an error,
    /// as reference does when its own printing runs out of stack.
    /// </summary>
    private void EmitPrinted(HarloweValue value, int depth = 0)
    {
      switch (value.Kind)
      {
        case HarloweValueKind.String:
          if (value.AsString.Length == 0) return;
          if (value.IsLiteralText) _output.Text(value.AsString);
          else RenderPrintedMarkup(value.AsString);
          return;
        case HarloweValueKind.Array:
        case HarloweValueKind.Dataset:
          if (depth >= HarloweValue.MaxPrintDepth)
          {
            _output.Error("This value is nested too deeply to print.");
            return;
          }
          var items = (List<HarloweValue>)value.Raw;
          for (int i = 0; i < items.Count; i++)
          {
            if (i > 0) _output.Text(",");
            // A command in an array prints as a description instead of running.
            if (items[i].Kind == HarloweValueKind.Command) _output.Text(items[i].ToHarloweString());
            else EmitPrinted(items[i], depth + 1);
          }
          return;
        case HarloweValueKind.CodeHook:
          RenderCodeHook(value.AsCodeHook);
          return;
        case HarloweValueKind.Command:
          RunCommand(value.AsCommand);
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
    /// <c>--</c>, the last of which also covers a <c>---</c> rule.
    /// </summary>
    private static bool MayContainMarkup(string text)
    {
      for (int i = 0; i < text.Length; i++)
      {
        char c = text[i];
        switch (c)
        {
          case '(': case '[': case ']': case '<': case '|': case '$': case '_': case '{': case '}':
          case '*': case '`':
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
