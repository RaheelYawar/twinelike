using System.Collections.Generic;
using Harlowe.Runtime;
using Harlowe.Runtime.Saving;
using Xunit;

namespace Harlowe.Tests.Runtime.Macros
{
  /// <summary>
  /// Custom macros as real Harlowe runs them: <c>(macro:)</c>, typed parameters,
  /// <c>(output:)</c>, <c>(output-data:)</c>, <c>(error:)</c>, <c>(partial:)</c>
  /// and the <c>($name:)</c> call. Every expected text here was rendered by the
  /// real 3.3.9 build, except where a comment says otherwise.
  /// </summary>
  public class CustomMacroTests
  {
    private static HarloweStory Story(params (string name, string body)[] passages)
    {
      var story = new HarloweStory { FormatVersion = "3.3.9" };
      foreach (var (name, body) in passages)
        story.AddPassage(new HarlowePassage { Name = name, RawBody = body });
      story.StartNode = story.GetPassage(passages[0].name).Pid;
      return story;
    }

    private static RenderResult Render(string body)
      => new StorySession(Story(("Start", body), ("b", "B"))).Render();

    private static List<string> Errors(RenderResult result)
    {
      var errors = new List<string>();
      foreach (var entry in result.Entries)
        if (entry.Kind == BufferedRenderOutput.Kind.Error) errors.Add(entry.Content);
      return errors;
    }

    private static void AssertRenders(string source, string expected)
    {
      var result = Render(source);
      Assert.Empty(Errors(result));
      Assert.Equal(expected, result.Text);
    }

    private static void AssertError(string source, string expectedText, string messagePart)
    {
      var result = Render(source);
      var errors = Errors(result);
      Assert.Single(errors);
      Assert.Contains(messagePart, errors[0]);
      Assert.Equal(expectedText, result.Text);
    }

    [Theory]
    [InlineData("(set: $m to (macro: [(output-data: 5)]))($m:)", "5")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data: _a * 3)]))($m: 5)", "15")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data: _a + 10)]))(print: ($m: 5) + 5)", "20")]
    [InlineData("(set: _m to (macro: [(output-data: 'x')]))(_m:)", "x")]
    [InlineData("(set: $m to (macro: [hello (output-data: 1) world]))[($m:)]", "1")]
    [InlineData("(set: $e to (macro:[(if:true)[(output-data:'bar')]]))($e:)", "bar")]
    [InlineData("(set: $n to 0)(set: $e to (macro:[(output-data:1)(set: $n to 9)]))($e:) $n", "1 0")]
    [InlineData("(set: $n to 'Fox')(set:_m to (macro:[(output-data:$n)]))(_m:)", "Fox")]
    [InlineData("(set: $m to (macro: [(for: each _i, 1, 2, 3)[(if: _i is 2)[(output-data: _i)]]]))($m:)", "2")]
    [InlineData("(set: $m to (macro: [(output-data: \"''b''\")]))($m:)", "b")]
    [InlineData("(set: $m to (macro: [(output-data: ']')]))($m:)", "]")]
    [InlineData("(set: $m to (macro: [(output-data: (a:1,2))]))(print: ($m:)'s length)", "2")]
    [InlineData("(set: $m to (macro: [(output-data: 'val')]))($m:)[hook]", "valhook")]
    [InlineData("(set: $m to (macro: [(output-data: false)]))($m:)[x]", "")]
    [InlineData("(set: $m to (macro: [(output-data: true)]))($m:)[x]", "x")]
    [InlineData("(set: $m to (macro:,[(output-data: 1)]))($m:)", "1")]
    [InlineData("(set: $m to (macro: dm-type _d, [(output-data: _d's x)]))($m: (dm: 'x', 5))", "5")]
    [InlineData("(set: $m to (macro: (a: num, str)-type _p, [(output-data: _p's 2nd)]))($m: (a: 1, 'b'))", "b")]
    [InlineData("(set: $m to (macro: any-type _a, [(output-data: _a)]))($m: 'x') ($m: 3)", "x 3")]
    [InlineData("(set: $m to (macro: macro-type _f, [(output-data: (_f:))]))(set: $g to (macro: [(output-data: 3)]))($m: $g)", "3")]
    [InlineData("(set: $m to (macro: codehook-type _h, [(output-data: _h)]))($m: [z])", "z")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data: _a)]))($m: ...(a: 4))", "4")]
    public void Outputs(string source, string expected) => AssertRenders(source, expected);

    [Theory]
    [InlineData("(set: $m to (macro: ...num-type _n, [(output-data: _n)]))(print: ($m: 1, 2, 3))", "1,2,3")]
    [InlineData("(set: $m to (macro: ...num-type _n, [(output-data: _n's length)]))($m:)", "0")]
    [InlineData("(set: $m to (macro: str-type _s, ...num-type _n, [(output-data: _s + (str: _n))]))($m: 'a', 1, 2)", "a1,2")]
    [InlineData("(set: $m to (macro: str-type _s, ...num-type _n, [(output-data: _s + (str: _n))]))($m: 'a')", "a")]
    [InlineData("(set: $m to (macro: ...num-type _a, [(output-data: _a's length)]))($m: ...(a: 4, 5))", "2")]
    public void SpreadParameters(string source, string expected) => AssertRenders(source, expected);

    [Theory]
    [InlineData("($nope:)", "", "isn't a custom macro")]
    [InlineData("(_nope:)", "", "There isn't a temp variable")]
    [InlineData("(set: $x to 5)($x:)", "", "I can't call the number 5")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data: _a)]))($m: 's')", "", "$m's 1st value is the string \"s\", but should be")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data: _a)]))($m:)", "", "was given nothing, but needs 1 more value")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data: _a)]))($m: 1, 2)", "", "1 too many values")]
    [InlineData("(set: $m to (macro: ...num-type _n, [(output-data: 1)]))($m: 1, 's')", "", "2nd value")]
    [InlineData("(set: $m to (macro: [(set: _a to 1)]))($m:)", "", "didn't output any data or hooks")]
    [InlineData("(set: $m to (macro: [(print: 1 + 'a')(output-data: 1)]))($m:)", "", "1 error occurred when running a custom macro (with no params)")]
    [InlineData("(set: $m to (macro: [(print: 1 + 'a')(print: 2 + 'b')(output-data: 1)]))($m:)", "", "2 errors occurred")]
    [InlineData("(set: $m to (macro: [(error: 'boom')(output-data: 1)]))($m:)", "", "boom")]
    [InlineData("(set: $m to (macro: [(error: '')(output-data: 1)]))($m:)", "", "1 error occurred")]
    [InlineData("(output-data: 1)", "", "(output-data:) should only be used inside a code hook")]
    [InlineData("(error: 'x')", "", "(error:) should only be used inside a code hook")]
    [InlineData("(error: '')", "", "given an empty string")]
    [InlineData("(set: _n to 'Fox')(set:_m to (macro:[(output-data:_n)]))(_m:)", "", "1 error occurred")]
    [InlineData("(set: $m to (macro: num-type _a, [(set: _a to 't')(output-data: _a)]))($m: 1)", "", "1 error occurred")]
    [InlineData("(set: $m to (macro: [(output:)]))($m:)", "", "1 error occurred")]
    [InlineData("(set: $m to (macro: [(output-data: (output-data: 'baz'))]))($m:)", "", "(output-data:) should only be used")]
    public void CallErrors(string source, string text, string message) => AssertError(source, text, message);

    [Theory]
    [InlineData("(set: $m to (macro: ...num-type _n, str-type _s, [(output-data: 1)]))", "one spread variable")]
    [InlineData("(set: $m to (macro: num-type $a, [(output-data: 1)]))", "must be temp variables")]
    [InlineData("(set: $m to (macro: num-type _a, str-type _a, [(output-data: 1)]))", "can't both be named 'a'")]
    [InlineData("(set: $m to (macro: num-type _a))", "last value given to (macro:) should be a code hook")]
    [InlineData("(set: $m to (macro:))", "requires at least 1")]
    [InlineData("(print: (macro: 'a', [(output-data: 1)]))", "1st value is the string \"a\"")]
    [InlineData("(print: (macro: [(output-data: 1)], [(output-data: 1)]))", "3rd-last value given to (macro:) should be a datatyped variable")]
    public void DefinitionErrors(string source, string message) => AssertError(source, "", message);

    [Theory]
    [InlineData("(set: $m to (macro: [(output:)[hook]]))($m:)", "hook")]
    [InlineData("(set: $m to (macro: [(set: _z to 5)(output:)[_z]]))($m:)", "5")]
    [InlineData("(set: _y to 7)(set: $m to (macro: [(output:)[_y]]))($m:)", "7")]
    [InlineData("(set: $m to (macro: [(output:)[q]]))(set: $c to ($m:))$c $c", "q q")]
    [InlineData("(set: $m to (macro: [(if: false)+(output:)[x]]))($m:)", "")]
    [InlineData("(set: $m to (macro: [(output:)+(if: false)[x]]))($m:)", "")]
    [InlineData("(set: $m to (macro: str-type _s, [(output:)[(print: _s)]]))($m: 'a')($m: 'b')", "ab")]
    [InlineData("(set: $m to (macro: [(if: true)[(output:)[in]]]))($m:)", "in")]
    [InlineData("(set: $m to (macro: [(set: $h to 1)(output:)[(set: $h to 2)x]]))(set: $c to ($m:))$h $c $h", "1 x 2")]
    [InlineData("(set: $m to (macro: [(output:)[q]]))(a: ($m:))", "[a custom command]")]
    [InlineData("(set: $m to (macro: [(output:)[q]]))(print: ($m:))", "[a custom command]")]
    [InlineData("(set: $m to (macro: [(output:)[q]]))(print: ($m:) is a command)", "true")]
    [InlineData("(set: $m to (macro: [(output:)[q]]))(print: ($m:) is ($m:))", "false")]
    [InlineData("(set: $m to (macro: [(output:)[q]]))(set: $c to ($m:))(print: $c is $c)", "true")]
    // Real 3.3.9 throws an internal error here. Harlowe's own spec expects this.
    [InlineData("(set: $m to (macro: [(for: each _a, 1,2,3)+(output:)[_a]]))($m:)", "123")]
    public void OutputHooks(string source, string expected) => AssertRenders(source, expected);

    [Fact]
    public void AChangerBeforeACustomCommand_StylesIt()
    {
      var result = Render("(set: $m to (macro: [(output:)[q]]))(text-style:'bold')($m:)");
      Assert.Empty(Errors(result));
      Assert.Equal("q", result.Text);
      Assert.Contains(result.Entries, e => e.Kind == BufferedRenderOutput.Kind.PushStyle && e.Style.Bold);
    }

    [Fact]
    public void AChangerBeforeARecursiveCommand_RendersEachLevelOnce()
    {
      var result = Render("(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](output:)[(text-style: 'bold')($r: _n - 1)]]))($r: 3)");
      Assert.Empty(Errors(result));
      Assert.Equal("x", result.Text);
    }

    [Theory]
    [InlineData("(output:)[x]after", "after")]
    [InlineData("before(output:)[x]after [more](print: 2)", "beforeafter ")]
    [InlineData("[(output:)[x]inner]outer", "innerouter")]
    public void OutputOutsideAMacro_HidesItsHookAndBlocksLaterCode(string source, string expected)
      => AssertRenders(source, expected);

    [Theory]
    [InlineData("(print: [a''b''c])", "abc")]
    [InlineData("(set: $h to [x (print: 1+1)])$h", "x 2")]
    [InlineData("(print: [a] is [a])", "true")]
    [InlineData("(print:[a] is [b])", "false")]
    [InlineData("(set: $a to [it's \"quoted\"])$a", "it's \"quoted\"")]
    [InlineData("(set: $h to [a[b]c])$h", "abc")]
    [InlineData("(print: [ ] is a codehook)", "true")]
    [InlineData("(print: (datatype: [a]))", "[the codehook datatype]")]
    [InlineData("(print: (str: [x]))", "x")]
    [InlineData("(set: $h to [(set: $q to 1)])$h $q", " 1")]
    [InlineData("(print: [x]<c|)", "x")]
    [InlineData("(print: [`]`])", "]")]
    public void CodeHooks(string source, string expected) => AssertRenders(source, expected);

    [Theory]
    [InlineData("(set: $m to (macro: [(output-data: 1)]))$m", "[a custom macro (with no params)]")]
    [InlineData("(set: $m to (macro: num-type _a, str-type _b, [(output-data: 1)]))(print: $m)", "[a custom macro (with num-type _a and str-type _b)]")]
    [InlineData("(macro: [(output-data: 1)])", "[a custom macro (with no params)]")]
    [InlineData("(print: (macro: ...num-type _a, [(output-data: 1)]))", "[a custom macro (with ...num-type _a)]")]
    [InlineData("(print: (macro: (a: num, str)-type _p, [(output-data: 1)]))", "[a custom macro (with (a:num,str)-type _p)]")]
    [InlineData("(set: $m to (macro: [(output-data: 1)]))(print: $m is a macro)", "true")]
    [InlineData("(set: $m to (macro: num-type _a, [(output-data:1)]))(print: $m's params's 1st's name) (print: $m's params's 1st's datatype)", "a [the num datatype]")]
    [InlineData("(print: (num-type _a)'s name)", "a")]
    [InlineData("(print: (...num-type _a)'s datatype)", "[the ...num datatype]")]
    [InlineData("(set: $a to (macro:[(output-data:1)]))(set:$b to $a)(print: $a is $b)", "false")]
    [InlineData("(set: $a to (macro:[(output-data:1)]))(print: $a is $a)", "true")]
    [InlineData("(print: (a: 3) matches (a: num-type _x))", "true")]
    public void MacroValues(string source, string expected) => AssertRenders(source, expected);

    [Theory]
    [InlineData("(print: (num-type _a))", "is not valid data for this macro")]
    [InlineData("(set: $t to (num-type _a))", "can't be stored")]
    public void TypedVarsCantBeUsedAsData(string source, string message) => AssertError(source, "", message);

    [Theory]
    [InlineData("(set:_a to (partial:'max',2,5,7))(_a:3)(_a:9)(_a:6)(_a:8)", "7978")]
    [InlineData("(set:_m to (macro: num-type _a, num-type _b, [(output-data: _a - _b)]))(set:_p to (partial: _m, 10))(_p: 3)", "7")]
    [InlineData("(set:_p to (partial:'max',2))(print: _p)", "[a (partial:) custom macro of (max:2)]")]
    [InlineData("(set: $p to (partial: 'print'))($p: 'hey')", "hey")]
    [InlineData("(set: $p to (partial: 'if', true))($p:)[yes]", "yes")]
    [InlineData("(set: $p to (partial: 'display', 'b'))($p:)", "B")]
    [InlineData("(set: $m to (macro: [(output-data: 1)]))(set: $p to (partial: $m))($p:)", "1")]
    [InlineData("(set: $p to (partial: (macro: num-type _a, [(output-data: _a)]), 4))(print: $p)($p:)", "[a (partial:) custom macro of another unnamed custom macro]4")]
    [InlineData("(set: $p to (partial: 'max', 2))(set: $q to (partial: $p, 3))($q: 9)(print: $q)", "9[a (partial:) custom macro of ($p:3)]")]
    public void Partials(string source, string expected) => AssertRenders(source, expected);

    [Theory]
    [InlineData("(partial:'nope')", "isn't the name of a built-in macro")]
    [InlineData("(set: $p to (partial: 3))", "should be a string or a custom macro")]
    [InlineData("(set:_m to (macro: num-type _a, [(output-data: _a)]))(set:_p to (partial: _m, 'x'))(_p:)", "An error occurred while running the (partial:)-created macro")]
    [InlineData("(set: $p to (partial: 'set'))($p: 1)", "(set:)")]
    public void PartialErrors(string source, string message) => AssertError(source, "", message);

    [Fact]
    public void TheEngineOnlyCommandMacro_DoesntExistForStories()
    {
      AssertError("(custom-command: (output:), [x], (dm:))", "", "doesn't exist");
      AssertError("(partial: 'custom-command')", "", "isn't the name of a built-in macro");
    }

    [Theory]
    [InlineData("(set: num-type $a to 5)$a", "5")]
    [InlineData("(set: num-type $a to 5)(set: num-type $a to 6)$a", "6")]
    [InlineData("(set: const-type $a to 5)(set: const-type $a to 6)$a", "6")]
    [InlineData("(set: any-type $a to 5)(set: $a to 'x')$a", "x")]
    [InlineData("(set: $a to 5)(set: str-type $a to 'x')$a", "x")]
    [InlineData("(set: _a to 1)[(set: str-type _a to 'x')_a]_a", "xx")]
    [InlineData("(set: const-type _a to 1)[(set: const-type _a to 2)_a]_a", "21")]
    [InlineData("[(set: const-type _a to 1)](set: _a to 2)_a", "2")]
    [InlineData("(set: (num-type _a) to 3)_a", "3")]
    [InlineData("(unpack: (a: 1, 'x') into (a: num-type $a, str-type $b))$a $b", "1 x")]
    [InlineData("(unpack: (a: 1, 2, 3) into (a: num-type _a, ...num-type _b))_a (print: _b)", "1 2,3")]
    [InlineData("(set: array-type $a to (a: 1))(set: $a's 1st to 'x')(print: $a)", "x")]
    [InlineData("(set: str-type $a to 'abc')(set: $a's 1st to 'z')$a", "zbc")]
    [InlineData("(set: (a: 1, num)-type $a to (a: 1, 2))(print: $a)", "1,2")]
    public void TypedAssignments(string source, string expected) => AssertRenders(source, expected);

    [Theory]
    [InlineData("(set: num-type $a to 'x')$a", "0", "doesn't match $a's datatype")]
    [InlineData("(set: num-type $a to 5)(set: $a to 'x')$a", "5", "restricted to num-type data")]
    [InlineData("(set: num-type $a to 5)(set: str-type $a to 'x')$a", "5", "I can't redefine the type")]
    [InlineData("(set: const-type $a to 5)(set: $a to 6)$a", "5", "restricted to a constant value")]
    [InlineData("(set: (a: num)-type $a to (a: 1))(set: $a to 'x')(print: $a)", "1", "restricted to (a:num)-type data")]
    [InlineData("(set: num-type _a to 5)[(set: _a to 'x')]_a", "5", "restricted to num-type data")]
    [InlineData("(put: 5 into num-type $a)(set: $a to 'x')$a", "5", "restricted to num-type data")]
    [InlineData("(set: $b to 5)(move: $b into num-type $a)(set: $a to 'x')$a", "5", "restricted to num-type data")]
    [InlineData("(unpack: (a: 'q') into (a: num-type $a))$a", "0", "doesn't match")]
    [InlineData("(set: const-type $a to (a: 1))(set: $a's 1st to 2)(print: $a)", "1", "restricted to a constant value")]
    [InlineData("(set: num-type _a to 1)[(set: str-type _a to 'x')]_a", "1", "I can't redefine the type")]
    public void TypeRestrictionErrors(string source, string text, string message) => AssertError(source, text, message);

    // 3.3.9's delete runs its type check with no value, which any restriction refuses.
    [Theory]
    [InlineData("(set: num-type $a to 5)(move: $a into $b)$a $b", "5 5")]
    [InlineData("(set: str-type $a to 'abc')(move: $a's 1st into $b)$a $b", "abc a")]
    [InlineData("(set: array-type _a to (a: 1, 2))(move: _a's 1st into $b)(print: _a) $b", "1,2 1")]
    [InlineData("(set: const-type $a to 5)(move: $a into $b)$a $b", "5 5")]
    [InlineData("(set: any-type $a to 5)(move: $a into $b)$a $b", "0 5")]
    [InlineData("(set: any-type $a to (a: 1, 2))(move: $a's 1st into $b)(print: $a) $b", "2 1")]
    public void MoveOutOfARestrictedVariable_OnlyCopies(string source, string expected)
      => AssertRenders(source, expected);

    [Fact]
    public void Recursion_WorksWithinTheLimit()
      => AssertRenders("(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](output-data: 1 + ($r: _n - 1))]))($r: 100)", "100");

    [Fact]
    public void InfiniteRecursion_IsAnErrorNotACrash()
    {
      var result = Render("(set: $r to (macro: [(output-data: ($r:))]))($r:)");
      var errors = Errors(result);
      Assert.Single(errors);
      Assert.Contains("infinite loop", errors[0]);
      // The cause is reported once, not wrapped again per level.
      Assert.Equal(errors[0].IndexOf("The first was"), errors[0].LastIndexOf("The first was"));
    }

    [Fact]
    public void ACommandThatPrintsItselfTwice_StopsInsteadOfDoublingForever()
    {
      var watch = System.Diagnostics.Stopwatch.StartNew();
      var result = Render("(set: $m to (macro: [(output:)[q$c $c]]))(set: $c to ($m:))$c $c");
      Assert.NotEmpty(Errors(result));
      Assert.True(watch.Elapsed.TotalSeconds < 3, $"took {watch.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public void InfiniteRecursion_FitsASmallStack()
    {
      RenderResult result = null;
      var thread = new System.Threading.Thread(
        () => result = Render("(set: $r to (macro: [(output-data: (max: 0, 1 + (1 + (1 + ($r:)))))]))($r:)"), 256 * 1024);
      thread.Start();
      thread.Join();
      Assert.Single(Errors(result));
    }

    [Fact]
    public void Startup_RunsBeforeTheFirstPassageInNameOrder()
    {
      var story = Story(("Start", "$a $b _t"), ("z", "(set: $a to 2)(set: $b to $a + 1)"), ("Y", "(set: $a to 1)(set: _t to 9)"));
      story.GetPassage("z").Tags = new List<string> { "startup" };
      story.GetPassage("Y").Tags = new List<string> { "startup" };
      var result = new StorySession(story).Render();
      Assert.Empty(Errors(result));
      Assert.Equal("2 3 9", result.Text);
    }

    // Each list is the order real 3.3.9 ran these startup passages in.
    [Theory]
    [InlineData("1st|2nd|10th|_x|a|A|a b|a-b|ab|alpha|B|part 2|Part 9|Part 10|z|Zed")]
    [InlineData("macro|macro 10|Macro 2|macros|Macros|MACROS")]
    [InlineData("!bang|(paren|~tilde|setup|Setup|setup 1|setup 10|setup_3|setup-2|setup.4|x  y|x y")]
    [InlineData("001|01|1|1.10|1.5|2|10|a01|a1|a2|a10")]
    public void Startup_RunsInNaturalNameOrder(string expectedOrder)
    {
      var names = expectedOrder.Split('|');
      var passages = new List<(string name, string body)> { ("Start", "x") };
      // Added in reverse, so passing can't come from insertion order.
      for (int i = names.Length - 1; i >= 0; i--)
        passages.Add((names[i], "(set: $o to (str: $o) + '" + names[i] + "|')"));
      var story = Story(passages.ToArray());
      foreach (var name in names) story.GetPassage(name).Tags = new List<string> { "startup" };

      var session = new StorySession(story);
      session.Render();
      Assert.True(session.TryGetVariable("o", out var order));
      Assert.Equal("0" + expectedOrder + "|", order.AsString);
    }

    [Fact]
    public void Startup_RunsOnlyOnTheFirstTurn()
    {
      var story = Story(("Start", "[[Next]]"), ("Next", "$n"), ("setup", "(set: $n to it + 1)"));
      story.GetPassage("setup").Tags = new List<string> { "startup" };
      var session = new StorySession(story);
      session.Render();
      Assert.Equal("1", session.Goto("Next").Text);
    }

    [Fact]
    public void Startup_RunsWhenAHostNavigatesBeforeRendering()
    {
      var story = Story(("Start", "x"), ("Next", "$n"), ("setup", "(set: $n to 4)"));
      story.GetPassage("setup").Tags = new List<string> { "startup" };
      Assert.Equal("4", new StorySession(story).Goto("Next").Text);
    }

    [Fact]
    public void AMacroFromStartup_WorksOnLaterTurnsAndThroughUndo()
    {
      var story = Story(
        ("Start", "($m: 2)"), ("Next", "($m: 3)"),
        ("setup", "(set: $m to (macro: num-type _a, [(output-data: _a * 10)]))"));
      story.GetPassage("setup").Tags = new List<string> { "startup" };
      var session = new StorySession(story);
      Assert.Equal("20", session.Render().Text);
      Assert.Equal("30", session.Goto("Next").Text);
      Assert.True(session.Undo());
      Assert.Equal("20", session.Render().Text);
    }

    [Theory]
    [InlineData("(set: _x to 5)[(set: _y to 6)(link: 'go')[_x _y]]", "0", "5 6", 0)]
    [InlineData("(for: each _i, 1, 2)[(link: 'l')[_i]]", "1", "l2", 0)]
    [InlineData("(for: each _i, 1, 2)[(link: 'l')[_i]]", "0,0", "12", 0)]
    [InlineData("(set: _n to 0)(link-repeat: 'l')[(set: _n to it + 1)_n]", "0,0", "l12", 0)]
    [InlineData("(set: $m to (macro: str-type _s, [(output:)[(link-reveal: 'go')[_s]]]))($m: 'value')", "0", "govalue", 0)]
    [InlineData("(set: _a to 1)(link: 'x')[(set: _a to 2)](link: 'y')[_a]", "0,0", "2", 0)]
    [InlineData("(set: $m to (macro: [(set: _v to 3)(output:)[(link-repeat: 'go')[(set: _v to it + 1)_v]]]))($m:)", "0,0", "go45", 0)]
    [InlineData("(set: $m to (macro: [(output:)[(link: 'go')[_outer]]]))(set: _outer to 'seen')($m:)", "0", "seen", 0)]
    [InlineData("[(set: _h to 1)(link: 'a')[(set: _h to it + 1)_h]](set: _h to 9)(link: 'b')[_h]", "0,0", "29", 0)]
    [InlineData("(set: num-type _t to 1)(link: 'a')[(set: _t to 'x')_t]", "0", "1", 1)]
    [InlineData("(set: $l to (macro: str-type _t, [(output:)[(link-reveal: _t)[(print: _t + '!')]]]))(for: each _w, 'p', 'q')[($l: _w) ]", "1", "p qq! ", 0)]
    public void DeferredHooks_SeeTheTempsTheyWereArmedWith(string source, string clicks, string expected, int errors)
    {
      var session = new StorySession(Story(("Start", source)));
      var result = session.Render();
      foreach (var click in clicks.Split(','))
      {
        var regions = result.Entries.FindAll(e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
        result = session.DispatchEvent(regions[int.Parse(click)].Region.Id);
      }
      Assert.Equal(errors, Errors(result).Count);
      Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void AClickInACustomCommand_KeepsItsTempVariables()
    {
      var session = new StorySession(Story(
        ("Start", "(set: $m to (macro: str-type _s, [(output:)[(link-reveal: 'go')[_s]]]))($m: 'value')")));
      var first = session.Render();
      var link = first.Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
      var clicked = session.DispatchEvent(link.Region.Id);
      Assert.Empty(Errors(clicked));
      Assert.Contains("value", clicked.Text);
    }

    [Fact]
    public void AClickInANestedHook_KeepsItsTempVariables()
    {
      var session = new StorySession(Story(("Start", "(set: _x to 5)[(set: _y to 6)(link: 'go')[_x _y]]")));
      var link = session.Render().Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
      var clicked = session.DispatchEvent(link.Region.Id);
      Assert.Empty(Errors(clicked));
      Assert.Equal("5 6", clicked.Text);
    }

    [Fact]
    public void AClickInALoop_ReadsItsOwnIteration()
    {
      var session = new StorySession(Story(("Start", "(for: each _i, 1, 2)[(link: 'l')[_i]]")));
      var links = session.Render().Entries.FindAll(e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
      var clicked = session.DispatchEvent(links[1].Region.Id);
      Assert.Empty(Errors(clicked));
      Assert.Equal("l2", clicked.Text);
    }

    [Fact]
    public void RepeatedClicks_ShareTheirTempVariables()
    {
      var session = new StorySession(Story(("Start", "(set: _n to 0)(link-repeat: 'l')[(set: _n to it + 1)_n]")));
      var link = session.Render().Entries.Find(e => e.Kind == BufferedRenderOutput.Kind.BeginInteractive);
      session.DispatchEvent(link.Region.Id);
      var second = session.DispatchEvent(link.Region.Id);
      Assert.Empty(Errors(second));
      Assert.Equal("l12", second.Text);
    }

    [Fact]
    public void Saving_RoundTripsMacrosCommandsAndHooks()
    {
      var story = Story(
        ("Start", "(set: $m to (macro: any-type _a, any-type _b, [(out:)[_a..._b]]))"
          + "(set: $c to ($m: 3, 'a'), $d to ($m: red, num), $h to [x''y''], $p to (partial: 'max', 2))[[Next]]"),
        ("Next", "$c|$h|($p: 5)|(print: ($m: 1, 2) is a command)"));
      var storage = new InMemorySaveStorage();
      var first = new StorySession(story, storage);
      first.Render();
      first.Goto("Next");
      Assert.True(first.SaveGame("slot"), first.LastSaveError);

      var second = new StorySession(story, storage);
      Assert.True(second.LoadGame("slot"), second.LastLoadError);
      var result = second.Render();
      Assert.Empty(Errors(result));
      Assert.Equal("3...a|xy|5|true", result.Text);
    }

    [Fact]
    public void Saving_ParsesACodeHookUnderTheStorysOwnVersion()
    {
      // `--` is prose in a Harlowe 3 story, but a comment in the save format's profile.
      var story = Story(
        ("Start", "(set: $h to [a--b])(set: $m to (macro: [(output:)[c--d]]))(set: $c to ($m:))[[Next]]"),
        ("Next", "$h $c"));
      var storage = new InMemorySaveStorage();
      var first = new StorySession(story, storage);
      first.Render();
      first.Goto("Next");
      Assert.True(first.SaveGame("slot"), first.LastSaveError);

      var second = new StorySession(story, storage);
      Assert.True(second.LoadGame("slot"), second.LastLoadError);
      Assert.Equal("a--b c--d", second.Render().Text);
    }

    [Fact]
    public void Saving_RestoresTypeRestrictions()
    {
      var story = Story(("Start", "(set: num-type $a to 1)[[Next]]"), ("Next", "(set: $a to 'x')$a"));
      var storage = new InMemorySaveStorage();
      var first = new StorySession(story, storage);
      first.Render();
      first.Goto("Next");
      Assert.True(first.SaveGame("slot"), first.LastSaveError);

      var second = new StorySession(story, storage);
      Assert.True(second.LoadGame("slot"), second.LastLoadError);
      var result = second.Render();
      Assert.Single(Errors(result));
      Assert.Equal("1", result.Text);
    }

    [Fact]
    public void ABlobWithATypeRestriction_IsStampedForIt_AndOlderVersionsAreRefused()
    {
      var story = Story(("Start", "x"));
      var moment = new Moment
      {
        PassageName = "Start",
        TypeDelta = new Dictionary<string, HarloweValue> { ["a"] = HarloweValue.OfDatatype(DatatypeValue.FromLexeme("num")) },
      };
      string blob = SaveSerializer.SerialiseTimeline(new List<Moment>(), moment);
      Assert.Contains("\"version\":4", blob.Replace(" ", ""));

      var session = new MacroRegistry();
      StandardMacros(session);
      var refused = SaveSerializer.DeserialiseTimeline(
        blob.Replace("\"version\":4", "\"version\":3").Replace("\"version\": 4", "\"version\": 3"),
        story, session, new MacroContext { Store = new HarloweVariableStore(), Invoker = session, Profile = HarloweProfile.SaveFormat });
      Assert.False(refused.Ok);
    }

    private static void StandardMacros(MacroRegistry registry) => Harlowe.Runtime.Macros.StandardMacros.RegisterAll(registry);

    [Fact]
    public void Undo_LiftsARestrictionMadeOnTheUndoneTurn()
    {
      var story = Story(("Start", "[[Next]]"), ("Next", "(set: num-type $a to 1)[[Last]]"), ("Last", "x"));
      var session = new StorySession(story);
      session.Render();
      session.Goto("Next");
      session.Goto("Last");
      Assert.True(session.Undo());
      Assert.True(session.Undo());
      session.Render();
      Assert.False(session.TryGetVariable("a", out _));
    }

    [Fact]
    public void CodeHookContents_AreCheckedForBrokenLinksAndParseErrors()
    {
      var story = Story(("Start", "(set: $m to (macro: [[[Nowhere]](goto: 'Gone')(print: 1 2)(output-data: 1)]))"));
      var broken = story.GetBrokenLinks();
      Assert.Contains(broken, b => b.Target == "Nowhere");
      Assert.Contains(broken, b => b.Target == "Gone");
      Assert.NotEmpty(story.GetParseErrors());
    }

    [Fact]
    public void ACustomCallNamedLikeANavigationMacro_IsNotAReference()
    {
      var story = Story(("Start", "(set: _goto to (macro: str-type _a, [(output-data: _a)]))(_goto: 'Nowhere')"));
      Assert.Empty(story.GetBrokenLinks());
    }

    [Fact]
    public void ACustomCallNamedLikeAnAssignmentMacro_IsAnOrdinaryCall()
      => AssertRenders("(set: _set to (macro: num-type _a, [(output-data: _a)]))(_set: 4)", "4");
  }
}
