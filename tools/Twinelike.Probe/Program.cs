using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;

namespace Twinelike.Probe
{
  /// <summary>
  /// Twinelike's side of every comparison in <c>tools/bench</c>. Each command
  /// reads cases from a file and writes what Twinelike shows, in the shape the
  /// matching real-Harlowe recorder writes, so the two compare item by item.
  /// </summary>
  internal static class Program
  {
    private const string Usage =
      "usage: Twinelike.Probe <command> ...\n" +
      "  markup <cases.json> <out.json> [lists|plain] render snippets, normalised as `record markup`, `lists` or `errors`\n" +
      "  clicks <cases.json> <out.json>               render, fire regions by index, text after each\n" +
      "  nav <cases.json> <out.json>                  go to passages in turn (\"<undo>\" undoes)\n" +
      "  live <cases.json> <out.json>                 tick 16 ms frames, with clicks and held input\n" +
      "  walks <cases.json> <out.json>                replay walks recorded on real Harlowe\n" +
      "  specs <specs.json> <version> <out-dir> <label>  replay the reference spec checks\n" +
      "  fuzz <feature-coverage.twee> <report.txt> <count> <seed> [cases.json ...]\n" +
      "  timecase <case.txt>                          run one fuzz case and time it\n" +
      "  stack [<shape> <stack-kb>]                   list the shapes, or run one on a small stack";

    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions
    {
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static int Main(string[] args)
    {
      Console.OutputEncoding = new UTF8Encoding(false);
      int code = 2;
      // Deeply nested cases recurse further than a 1 MB main-thread stack allows.
      var worker = new Thread(() => code = Run(args), 16 * 1024 * 1024);
      worker.Start();
      worker.Join();
      return code;
    }

    private static int Run(string[] args)
    {
      string command = args.Length > 0 ? args[0] : "";
      switch (command)
      {
        case "markup" when args.Length >= 3:
          return Markup.Run(args[1], args[2], args.Length > 3 ? args[3] : "markup");
        case "clicks" when args.Length == 3:
          return Steps.Clicks(args[1], args[2]);
        case "nav" when args.Length == 3:
          return Steps.Nav(args[1], args[2]);
        case "live" when args.Length == 3:
          return Steps.Live(args[1], args[2]);
        case "walks" when args.Length == 3:
          return Walks.Run(args[1], args[2]);
        case "specs" when args.Length == 5:
          return Specs.Run(args[1], args[2], args[3], args[4]);
        case "fuzz" when args.Length >= 5:
          var extra = new List<string>();
          for (int i = 5; i < args.Length; i++) extra.Add(args[i]);
          return Fuzz.Run(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]), extra);
        case "timecase" when args.Length == 2:
          return Fuzz.TimeCase(args[1]);
        case "stack" when args.Length == 1 || args.Length == 3:
          return args.Length == 1 ? Stack.List() : Stack.Run(args[1], int.Parse(args[2]));
        default:
          Console.Error.WriteLine(Usage);
          return 2;
      }
    }

    internal static void WriteJson(string path, object value)
    {
      Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
      File.WriteAllText(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
    }
  }
}
