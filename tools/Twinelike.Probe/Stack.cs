using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Harlowe;
using Harlowe.Runtime;

namespace Twinelike.Probe
{
  /// <summary>
  /// Deep recursion of every kind the runtime bounds, run on a thread with a
  /// small stack. A stack overflow kills the process rather than throwing, so
  /// the caller runs each shape in its own process and reads the exit code.
  /// </summary>
  internal static class Stack
  {
    // Initialised in this order: BuildShapes fills Order.
    private static readonly List<string> Order = new List<string>();
    private static readonly Dictionary<string, string> Shapes = BuildShapes();

    private static Dictionary<string, string> BuildShapes()
    {
      var s = new Dictionary<string, string>();
      void Add(string name, string body) { s[name] = body; Order.Add(name); }

      Add("plain", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](output-data: 1 + ($r: _n - 1))]))($r: 100000)");
      Add("hook", "(set: $r to (macro: num-type _n, [(if: _n > 0)[(output-data: 1 + ($r: _n - 1))](output-data: 0)]))($r: 100000)");
      Add("hooks3", "(set: $r to (macro: num-type _n, [(if: _n > 0)[(if: true)[(if: true)[(output-data: 1 + ($r: _n - 1))]]](output-data: 0)]))($r: 100000)");
      Add("command", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](output:)[($r: _n - 1)]]))($r: 100000)");
      Add("nested-args", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](output-data: (max: 0, (min: 100000, (max: 1, 1 + (1 + (1 + ($r: _n - 1))))))) ]))($r: 100000)");
      Add("for", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](for: each _i, 1)[(output-data: 1 + ($r: _n - 1))]]))($r: 100000)");
      Add("lambda", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](output-data: (altered: via ($r: _n - 1), 1)'s 1st)]))($r: 100000)");
      Add("display", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](display: 'again')(output-data: 1)]))(set: $n to 100000)($r: 100000)");
      Add("printed", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](print: \"(print: ($r: \" + (str: _n - 1) + \"))\")(output-data: 1)]))($r: 100000)");
      Add("partial", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](set: _p to (partial: $r, _n - 1))(output-data: 1 + (_p:))]))($r: 100000)");
      Add("attached", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](output:)[(text-style: 'bold')($r: _n - 1)]]))($r: 100000)");
      Add("plus-chain", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: (text-style: 'bold'))](output-data: (text-style: 'italic'))]))(set: $s to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](output:)[(text-style: 'bold')+($r: _n)($s: _n - 1)]]))($s: 100000)");
      Add("plus-custom", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](output:)[(text-style: 'bold')+($r: _n - 1)[y]]]))($r: 100000)");
      Add("print-arg", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](output:)[(text-style: 'bold')(print: ($r: _n - 1))]]))($r: 100000)");
      Add("stored", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[x]](set: _c to (print: ($r: _n - 1)))(output:)[(if: true)_c]]))($r: 100000)");
      Add("stored-display", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](set: _d to (display: 'again'))(if: true)_d(output-data: 1)]))($r: 100000)");
      Add("link-loop", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output:)[[[b]]]](output:)[(for: each _i, 1)($r: _n - 1)]]))($r: 100000)");
      Add("macro-nest", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](output-data: "
        + Nest("(max: 0, ", ")", 40, "($r: _n - 1)") + ")]))($r: 100000)");
      Add("paren-nest", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)](output-data: "
        + Nest("1 + (", ")", 60, "($r: _n - 1)") + ")]))($r: 100000)");
      Add("hook-nest", "(set: $r to (macro: num-type _n, [(if: _n <= 0)[(output-data: 0)]"
        + Nest("(if: true)[", "]", 40, "(output-data: 1 + ($r: _n - 1))") + "]))($r: 100000)");
      Add("show-self", "(set: $x to \"|a)[$x(show: ?a)]\")$x(show: ?a)");
      Add("show-self2", "(set: $x to \"|a)[$x$x(show: ?a)]\")$x(show: ?a)");
      Add("show-macro", "(set: $r to (macro: num-type _n, [|a)[(if: _n > 0)[(output-data: 1 + ($r: _n - 1))]](show: ?a)(output-data: 1)]))($r: 100000)");
      Add("show-nest", Nest("|a)[(show: ?a)", "]", 120, "x") + "(show: ?a)");
      Add("show-hidden-nest", Nest("(hidden:)|a>[(show: ?a)", "]", 120, "x") + "(show: ?a)");
      Add("show-display", "(set: $n to 0)|a)[(display: 'again2')](show: ?a)");
      Add("rev-macro", "|a>[x](set: $r to (macro: num-type _n, [(if: _n > 0)[(replace: ?a)[($r: _n - 1)]](output-data: 1)]))($r: 100000)");
      Add("rev-many", Repeat("|a>[x]", 200) + "(replace: ?a)[(append: ?a)[(append: ?a)[(append: ?a)[q]]]]");
      Add("rev-nest", "|a>[x]|a>[y]" + Nest("(append: ?a)[", "]", 120, "q"));
      Add("rev-display", "|a>[x](replace: ?a)[(display: 'again3')]");
      const string Grow = "(set: $d to 0)(for: each _i, ...(range: 1, 150000))[(set: $d to {0})]";
      foreach (var (name, wrap) in new[] { ("array", "(a: $d)"), ("ds", "(ds: $d)"), ("dm", "(dm: \"k\", $d)") })
      {
        string grown = Grow.Replace("{0}", wrap);
        Add("deep-" + name + "-print", grown + "(print: $d)");
        Add("deep-" + name + "-var", grown + "$d");
        Add("deep-" + name + "-is", grown + "(print: $d is $d)");
        Add("deep-" + name + "-match", grown + "(print: $d matches $d)");
        Add("deep-" + name + "-contains", grown + "(print: (ds: $d) contains $d)");
        Add("deep-" + name + "-spread", grown + "(print: (a: ...(ds: $d, 1))'s length)");
        Add("deep-" + name + "-add", grown + "(print: ((ds: $d) + (ds: $d))'s length)");
        Add("deep-" + name + "-str", grown + "(print: (a: $d) is (a: $d))(set: $e to $d)(print: 1)");
      }

      // Flat source the parser turns into a deep tree: an operator chain is as
      // deep as it's long, and each changer wraps what it styles.
      Add("chain-plus", "(print: 1" + Repeat(" + 1", 100000) + ")");
      Add("chain-mixed", "(print: 1" + Repeat(" + 1 * 1", 20000) + ")");
      Add("chain-possessive", "(set: $a to (a: 1))(print: $a" + Repeat("'s 1st", 20000) + ")");
      Add("chain-type", "(print: num" + Repeat("-type num", 20000) + "-type _a)");
      Add("chain-set-lambda", "(set: $x to 1" + Repeat(" + 1", 20000) + ")(print: (altered: via it" + Repeat(" + it", 20000) + ", 1))");
      const string Doubled = "(set: $c to (text-style: 'bold'))";
      Add("changer-doubled", Doubled + Repeat("(set: $c to $c + $c)", 30) + "$c[x]");
      Add("changer-grown", Doubled + "(for: each _i, ...(range: 1, 5000))[(set: $c to $c + (text-style: 'italic'))]$c[x]");
      Add("changer-inline", Repeat("(text-style: 'bold')+", 10000) + "(text-style: 'bold')[x]");
      Add("changer-1024-nested", Doubled + Repeat("(set: $c to $c + $c)", 10) + Nest("$c[", "]", 60, "x"));
      Add("changer-1024-enchant", Doubled + Repeat("(set: $c to $c + $c)", 10) + Nest("|a>[", "]", 60, "x") + "(enchant: ?a, $c)");
      return s;
    }

    private static string Nest(string open, string close, int n, string inner)
    {
      var sb = new StringBuilder();
      for (int i = 0; i < n; i++) sb.Append(open);
      sb.Append(inner);
      for (int i = 0; i < n; i++) sb.Append(close);
      return sb.ToString();
    }

    private static string Repeat(string s, int n)
    {
      var sb = new StringBuilder();
      for (int i = 0; i < n; i++) sb.Append(s);
      return sb.ToString();
    }

    public static int List()
    {
      foreach (var name in Order) Console.WriteLine(name);
      return 0;
    }

    /// <summary>Renders one shape on a <paramref name="stackKb"/> KB stack and prints the error count.</summary>
    public static int Run(string shape, int stackKb)
    {
      if (!Shapes.TryGetValue(shape, out var body)) { Console.Error.WriteLine("no shape " + shape); return 2; }
      string result = null;
      var thread = new Thread(() =>
      {
        var story = new HarloweStory { FormatVersion = "3.3.9" };
        story.AddPassage(new HarlowePassage { Name = "Start", RawBody = body });
        story.AddPassage(new HarlowePassage { Name = "again", RawBody = "($r: 100000)" });
        story.AddPassage(new HarlowePassage { Name = "again2", RawBody = "|a)[(display: 'again2')](show: ?a)" });
        story.AddPassage(new HarlowePassage { Name = "again3", RawBody = "|a>[y](replace: ?a)[(display: 'again3')]" });
        story.StartNode = story.GetPassage("Start").Pid;
        var r = new StorySession(story).Render();
        int errors = 0;
        string first = "";
        foreach (var e in r.Entries)
          if (e.Kind == BufferedRenderOutput.Kind.Error) { if (errors++ == 0) first = e.Content; }
        if (first.Length > 110) first = first.Substring(0, 110);
        result = $"ok, {errors} error(s): {first}";
      }, stackKb * 1024);
      thread.Start();
      thread.Join();
      Console.WriteLine(result);
      return 0;
    }
  }
}
