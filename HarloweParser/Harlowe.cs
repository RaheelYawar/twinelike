using System;
using System.Collections.Generic;
using System.Text;
using Harlowe.Ast.Body;
using Harlowe.Parsing;
using Harlowe.Tokens;
using HtmlAgilityPack;
using HapHtmlNode = HtmlAgilityPack.HtmlNode;
using HtmlNode = Harlowe.Ast.Body.HtmlNode;

namespace Harlowe
{
  public class Harlowe
  {
    private Dictionary<string, HarlowePassage> _passages;

    /// <summary>The story's author-facing name from <c>&lt;tw-storydata name="…"&gt;</c> or the body of <c>:: StoryTitle</c>. Empty string if absent.</summary>
    public string StoryName { get; internal set; }

    /// <summary>The pid of the start passage. From <c>&lt;tw-storydata startnode="…"&gt;</c> for HTML; for Twee-loaded stories it is the synthesized pid corresponding to the StoryData JSON's <c>start</c> field. Defaults to "0" if absent.</summary>
    public string StartNode { get; internal set; }

    /// <summary>The authoring tool that produced the story from <c>&lt;tw-storydata creator="…"&gt;</c> (typically "Twine"). Empty string if absent. Twee 3 source does not carry this field, so Twee-loaded stories leave it empty.</summary>
    public string Creator { get; internal set; }

    /// <summary>The version of the authoring tool from <c>&lt;tw-storydata creator-version="…"&gt;</c>. Empty string if absent. Twee 3 source does not carry this field, so Twee-loaded stories leave it empty.</summary>
    public string CreatorVersion { get; internal set; }

    /// <summary>The story's IFID (Interactive Fiction Identifier) from <c>&lt;tw-storydata ifid="…"&gt;</c> or the StoryData JSON's <c>ifid</c> key. Empty string if absent.</summary>
    public string Ifid { get; internal set; }

    /// <summary>The story format name from <c>&lt;tw-storydata format="…"&gt;</c> or StoryData JSON (<c>format</c>). Typically <c>"Harlowe"</c>. Empty string if absent.</summary>
    public string Format { get; internal set; }

    /// <summary>The story format version from <c>&lt;tw-storydata format-version="…"&gt;</c> or StoryData JSON (<c>format-version</c>). Empty string if absent.</summary>
    public string FormatVersion { get; internal set; }

    /// <summary>
    /// The full <c>:: StoryData</c> JSON object as parsed by
    /// <see cref="Twee.JsonReader"/>, kept verbatim for round-trip preservation.
    /// Holds every key the source carried, including ones we don't surface as
    /// typed properties (<c>tag-colors</c>, <c>zoom</c>, anything Twine adds
    /// later). On emit, <see cref="Twee.TweeWriter"/> overlays the typed
    /// fields onto a copy of this dictionary so future Twine-introduced fields
    /// pass through automatically. <c>null</c> for HTML-loaded stories — the
    /// Twee 3 StoryData object only exists in the Twee front-end.
    /// </summary>
    public Dictionary<string, object> StoryDataExtras { get; internal set; }

    public int PassageCount => _passages.Count;

    /// <summary>
    /// Parses a full Harlowe-format Twine HTML export. Extracts story metadata
    /// from <c>&lt;tw-storydata&gt;</c> and one <see cref="HarlowePassage"/>
    /// per <c>&lt;tw-passagedata&gt;</c> element. Each passage's body is
    /// HTML-entity-decoded, tokenized, and parsed into an
    /// <see cref="HarlowePassage.Ast"/> tree; <see cref="HarlowePassage.Body"/>
    /// and <see cref="HarlowePassage.Branches"/> are derived from the AST so
    /// the legacy public API keeps working.
    /// </summary>
    public Harlowe(string htmlText)
    {
      var htmlDoc = new HtmlDocument();
      htmlDoc.LoadHtml(htmlText);

      HapHtmlNode storyNode = htmlDoc.DocumentNode.SelectSingleNode("//tw-storydata");
      if (storyNode == null)
      {
        throw new HarloweParseException("Invalid Harlowe HTML file: <tw-storydata> not found.");
      }

      ParseStoryData(ref storyNode);
      Parse(storyNode.SelectNodes("//tw-passagedata"));
    }

    /// <summary>
    /// Internal constructor for alternate loaders (currently
    /// <see cref="Twee.TweeReader"/>) to populate the story incrementally
    /// rather than from an HTML document. Initializes the passage dictionary
    /// and metadata defaults; the caller is responsible for setting story-level
    /// fields and calling <see cref="AddPassage"/> for each passage. Not
    /// exposed publicly to keep the construction surface narrow.
    /// </summary>
    internal Harlowe()
    {
      _passages = new Dictionary<string, HarlowePassage>();
      StoryName = string.Empty;
      StartNode = "0";
      Creator = string.Empty;
      CreatorVersion = string.Empty;
      Ifid = string.Empty;
      Format = string.Empty;
      FormatVersion = string.Empty;
    }

    /// <summary>
    /// Adds a fully-populated <see cref="HarlowePassage"/> to the story,
    /// indexed by name. Used by alternate loaders (e.g.
    /// <see cref="Twee.TweeReader"/>) that build passages outside the HTML
    /// path. Throws on duplicate names because Harlowe passage names are
    /// unique by spec.
    /// </summary>
    internal void AddPassage(HarlowePassage passage)
    {
      _passages.Add(passage.Name, passage);
    }

    /// <summary>
    /// Enumerates passages in load order — the order they were added to the
    /// story. Used by <see cref="Twee.TweeWriter"/> to produce stable output.
    /// Internal for now; if a public enumeration API is wanted, lift to a
    /// public IEnumerable property.
    /// </summary>
    internal IEnumerable<HarlowePassage> Passages => _passages.Values;

    /// <summary>
    /// Pulls story-level attributes off the <c>&lt;tw-storydata&gt;</c> node:
    /// name, start-passage pid, creator tool/version, and the format
    /// attributes (<c>ifid</c>, <c>format</c>, <c>format-version</c>). Missing
    /// attributes default to empty rather than throwing.
    /// </summary>
    private void ParseStoryData(ref HapHtmlNode storyNode)
    {
      StoryName = storyNode.GetAttributeValue("name", "");
      StartNode = storyNode.GetAttributeValue("startnode", "0");
      Creator = storyNode.GetAttributeValue("creator", "");
      CreatorVersion = storyNode.GetAttributeValue("creator-version", "");
      Ifid = storyNode.GetAttributeValue("ifid", "");
      Format = storyNode.GetAttributeValue("format", "");
      FormatVersion = storyNode.GetAttributeValue("format-version", "");
    }

    /// <summary>
    /// Looks up a passage by its author-facing name. Returns null if no such
    /// passage exists — does not throw.
    /// </summary>
    public HarlowePassage GetPassage(string passageName)
    {
      return !_passages.ContainsKey(passageName) ? null : _passages[passageName];
    }

    /// <summary>
    /// Returns the passage whose <see cref="HarlowePassage.Pid"/> matches
    /// <see cref="StartNode"/>. Returns null if the story has no passages or
    /// the start node pid does not match any passage. Used by
    /// <c>StorySession</c> to find the initial passage by pid without exposing
    /// the internal pid-indexed structure.
    /// </summary>
    public HarlowePassage GetStartPassage()
    {
      foreach (var p in _passages.Values)
        if (p.Pid == StartNode) return p;
      return null;
    }

    /// <summary>
    /// Returns the body text of the named passage with branch links stripped.
    /// Returns <see cref="string.Empty"/> for unknown names.
    /// </summary>
    public string GetPassageBody(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return string.Empty;

      return passage.Body;
    }

    /// <summary>
    /// Returns the outgoing branch links for the named passage. Returns null
    /// for unknown names; returns an empty list for known passages with no
    /// links.
    /// </summary>
    public List<Branch> GetPassageBranches(string passageName)
    {
      if (!_passages.TryGetValue(passageName, out var passage)) return null;

      return passage.Branches;
    }

    /// <summary>
    /// Walks every <c>&lt;tw-passagedata&gt;</c> node, runs its inner HTML
    /// through entity decoding → tokenizer → body parser, and indexes the
    /// resulting <see cref="HarlowePassage"/> by name. <see cref="HarlowePassage.Branches"/>
    /// is filled by collecting every <see cref="LinkNode"/> in the AST;
    /// <see cref="HarlowePassage.Body"/> is the AST rendered back to plain
    /// prose with link markup stripped.
    /// </summary>
    private void Parse(HtmlNodeCollection passageNodes)
    {
      _passages = new Dictionary<string, HarlowePassage>();
      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();

      foreach (var passageNode in passageNodes)
      {
        string passageName = passageNode.Attributes["name"].Value;
        string raw = HtmlEntity.DeEntitize(passageNode.InnerHtml ?? string.Empty);

        Ast.Body.PassageBody ast;
        try
        {
          var tokens = tokenizer.Tokenize(raw);
          ast = bodyParser.Parse(tokens);
        }
        catch (HarloweParseException ex) when (ex.PassageName == null)
        {
          // Inner parsers don't know which passage they're inside. Re-throw
          // with the passage name attached so the caller's error message
          // points at the right place.
          throw new HarloweParseException(ex.RawMessage, ex.Line, ex.Column, passageName, ex);
        }

        var passage = new HarlowePassage
        {
          Pid = passageNode.Attributes["pid"].Value,
          Name = passageName,
          Tags = ParseTags(passageNode.GetAttributeValue("tags", string.Empty)),
          Ast = ast,
          Branches = ExtractBranches(ast),
          Body = RenderBody(ast),
          RawBody = raw,
        };

        _passages.Add(passage.Name, passage);
      }
    }

    /// <summary>
    /// Splits a Twine <c>tags="…"</c> attribute on whitespace into a list of
    /// tag names. Empty / whitespace-only / missing attributes return an empty
    /// list, matching the rest of the API's "empty list, not null, for present
    /// passages" pattern.
    /// </summary>
    private static List<string> ParseTags(string raw)
    {
      var result = new List<string>();
      if (string.IsNullOrWhiteSpace(raw)) return result;
      var parts = raw.Split(new[] { ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
      for (int i = 0; i < parts.Length; i++) result.Add(parts[i]);
      return result;
    }

    /// <summary>
    /// Walks the body AST and collects every <see cref="LinkNode"/> into a
    /// flat list of <see cref="Branch"/>es, preserving source order. Replaces
    /// the legacy string-split branch parser.
    /// </summary>
    private static List<Branch> ExtractBranches(PassageBody ast)
    {
      var visitor = new BranchCollector();
      foreach (var child in ast.Children) child.Accept(visitor);
      return visitor.Branches;
    }

    /// <summary>
    /// Walks the body AST and reconstructs a plain-prose representation:
    /// text, newlines, variable references (re-prefixed with <c>$</c> /
    /// <c>_</c>), HTML tags, and hook contents are emitted in source order;
    /// links and macros are skipped. This matches the spirit of the legacy
    /// <c>ParseBody</c> output (prose with links stripped) while routing
    /// through the new pipeline.
    /// </summary>
    private static string RenderBody(PassageBody ast)
    {
      var visitor = new BodyTextRenderer();
      foreach (var child in ast.Children) child.Accept(visitor);
      return visitor.Result;
    }

    /// <summary>
    /// Visitor that pulls every <see cref="LinkNode"/> out of a body AST and
    /// converts it into a <see cref="Branch"/>. Recurses into hooks and any
    /// hooks attached to macros so links inside conditional blocks are still
    /// collected.
    /// </summary>
    private class BranchCollector : IBodyVisitor
    {
      public readonly List<Branch> Branches = new List<Branch>();

      public void Visit(TextNode node) { }
      public void Visit(NewlineNode node) { }
      public void Visit(VariableNode node) { }
      public void Visit(HtmlNode node) { }

      public void Visit(MacroNode node)
      {
        if (node.AttachedHook != null) node.AttachedHook.Accept(this);
      }

      public void Visit(HookNode node)
      {
        if (node.Children == null) return;
        foreach (var child in node.Children) child.Accept(this);
      }

      public void Visit(LinkNode node)
      {
        Branches.Add(new Branch { Text = node.Text, Name = node.Target });
      }
    }

    /// <summary>
    /// Visitor that renders a body AST back to a plain-prose string. Links are
    /// dropped (matching the legacy stripped-links behaviour); macros are
    /// dropped because their source text is no longer recoverable from the AST
    /// without round-tripping arguments through a printer. Variables are
    /// re-emitted with their sigil so prose like "Hello $name." round-trips.
    /// </summary>
    private class BodyTextRenderer : IBodyVisitor
    {
      private readonly StringBuilder _sb = new StringBuilder();

      public string Result => _sb.ToString();

      public void Visit(TextNode node) => _sb.Append(node.Content);
      public void Visit(NewlineNode node) => _sb.Append('\n');
      public void Visit(VariableNode node) => _sb.Append(node.IsTemporary ? '_' : '$').Append(node.Name);
      public void Visit(HtmlNode node) => _sb.Append(node.RawHtml);
      public void Visit(LinkNode node) { }

      public void Visit(MacroNode node) { }

      public void Visit(HookNode node)
      {
        if (node.Children == null) return;
        foreach (var child in node.Children) child.Accept(this);
      }
    }
  }
}
