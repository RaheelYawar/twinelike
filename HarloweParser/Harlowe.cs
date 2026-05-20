using System;
using System.Collections.Generic;
using System.Globalization;
using Harlowe.Parsing;
using Harlowe.Tokens;
using HtmlAgilityPack;
using HapHtmlNode = HtmlAgilityPack.HtmlNode;

namespace Harlowe
{
  public class Harlowe
  {
    private Dictionary<string, HarlowePassage> _passages;

    // Canonical iteration order, kept independent of the dictionary so it
    // survives renames (which rekey the dict and would otherwise reorder)
    // and doesn't rely on Dictionary's insertion-order behaviour being a
    // stable cross-runtime contract — it isn't.
    private List<string> _passageOrder;

    /// <summary>The story's author-facing name from <c>&lt;tw-storydata name="…"&gt;</c> or the body of <c>:: StoryTitle</c>. Empty string if absent.</summary>
    public string StoryName { get; set; }

    /// <summary>The pid of the start passage. From <c>&lt;tw-storydata startnode="…"&gt;</c> for HTML; for Twee-loaded stories it is the synthesized pid corresponding to the StoryData JSON's <c>start</c> field. Defaults to "0" if absent.</summary>
    public string StartNode { get; set; }

    /// <summary>The authoring tool that produced the story from <c>&lt;tw-storydata creator="…"&gt;</c> (typically "Twine"). Empty string if absent. Twee 3 source does not carry this field, so Twee-loaded stories leave it empty.</summary>
    public string Creator { get; set; }

    /// <summary>The version of the authoring tool from <c>&lt;tw-storydata creator-version="…"&gt;</c>. Empty string if absent. Twee 3 source does not carry this field, so Twee-loaded stories leave it empty.</summary>
    public string CreatorVersion { get; set; }

    /// <summary>The story's IFID (Interactive Fiction Identifier) from <c>&lt;tw-storydata ifid="…"&gt;</c> or the StoryData JSON's <c>ifid</c> key. Empty string if absent.</summary>
    public string Ifid { get; set; }

    /// <summary>The story format name from <c>&lt;tw-storydata format="…"&gt;</c> or StoryData JSON (<c>format</c>). Typically <c>"Harlowe"</c>. Empty string if absent.</summary>
    public string Format { get; set; }

    /// <summary>The story format version from <c>&lt;tw-storydata format-version="…"&gt;</c> or StoryData JSON (<c>format-version</c>). Empty string if absent.</summary>
    public string FormatVersion { get; set; }

    /// <summary>
    /// The full <c>:: StoryData</c> JSON object as parsed by
    /// <see cref="Twee.JsonReader"/>, kept verbatim for round-trip preservation.
    /// Holds every key the source carried, including ones we don't surface as
    /// typed properties (<c>tag-colors</c>, <c>zoom</c>, anything Twine adds
    /// later). On emit, <see cref="Twee.TweeWriter"/> overlays the typed
    /// fields onto a copy of this dictionary so future Twine-introduced fields
    /// pass through automatically. <c>null</c> for HTML-loaded stories — the
    /// Twee 3 StoryData object only exists in the Twee front-end. Editing
    /// consumers may write through this dictionary directly to set
    /// <c>tag-colors</c>, <c>zoom</c>, or other extras.
    /// </summary>
    public Dictionary<string, object> StoryDataExtras { get; set; }

    public int PassageCount => _passages.Count;

    /// <summary>
    /// Parses a full Harlowe-format Twine HTML export. Extracts story metadata
    /// from <c>&lt;tw-storydata&gt;</c> and one <see cref="HarlowePassage"/>
    /// per <c>&lt;tw-passagedata&gt;</c> element. Each passage's body is
    /// HTML-entity-decoded, tokenized, and parsed into an
    /// <see cref="HarlowePassage.Ast"/> tree; <see cref="HarlowePassage.Body"/>
    /// and <see cref="HarlowePassage.Branches"/> are derived views over the AST.
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
    /// Builds an empty story. Used by alternate loaders (e.g.
    /// <see cref="Twee.TweeReader"/>) and by editing consumers constructing
    /// a story from scratch. Initializes the passage dictionary and metadata
    /// defaults; populate the story by setting fields and calling
    /// <see cref="AddPassage"/>.
    /// </summary>
    public Harlowe()
    {
      _passages = new Dictionary<string, HarlowePassage>();
      _passageOrder = new List<string>();
      StoryName = string.Empty;
      StartNode = "0";
      Creator = string.Empty;
      CreatorVersion = string.Empty;
      Ifid = string.Empty;
      Format = string.Empty;
      FormatVersion = string.Empty;
    }

    /// <summary>
    /// Adds a <see cref="HarlowePassage"/> to the story, indexed by name.
    /// Throws on duplicate names because Harlowe passage names are unique by
    /// spec. If <see cref="HarlowePassage.Pid"/> is null/empty a fresh numeric
    /// pid is synthesized — the maximum existing numeric pid plus one, so
    /// removals and explicit pid assignment can't collide with the
    /// synthesizer.
    ///
    /// <para>When <see cref="HarlowePassage.Ast"/> is null and
    /// <see cref="HarlowePassage.Body"/> is set, the body is tokenized + parsed
    /// here so the documented from-scratch shorthand
    /// <c>new HarlowePassage { Name = "Foo", Body = "..." }</c> produces a
    /// passage that actually renders and serializes. Callers that supply a
    /// pre-populated <see cref="HarlowePassage.Ast"/> (the HTML and Twee
    /// loaders, test fixtures with a hand-built AST) bypass this step.
    /// <see cref="HarlowePassage.RawBody"/> is filled from the body source if
    /// not already set, <see cref="HarlowePassage.Branches"/> is collected from
    /// the AST if not already set, and <see cref="HarlowePassage.Body"/> is
    /// replaced with the renderer-canonical prose so it matches the loader-set
    /// shape.</para>
    /// </summary>
    public void AddPassage(HarlowePassage passage)
    {
      if (passage == null) throw new ArgumentNullException(nameof(passage));
      if (string.IsNullOrEmpty(passage.Pid))
        passage.Pid = NextAvailablePid().ToString(CultureInfo.InvariantCulture);
      HydratePassageFromBody(passage);
      _passages.Add(passage.Name, passage); // throws on duplicate name; list stays clean
      _passageOrder.Add(passage.Name);
    }

    /// <summary>
    /// Parse <see cref="HarlowePassage.Body"/> into <see cref="HarlowePassage.Ast"/>
    /// (and derived <see cref="HarlowePassage.RawBody"/>/
    /// <see cref="HarlowePassage.Branches"/>/<see cref="HarlowePassage.Body"/>)
    /// for passages that were constructed by hand without going through the
    /// HTML or Twee loaders. Skips passages that already have an AST or whose
    /// body is null — both cases mean the caller is responsible for their own
    /// shape (loader-populated, deliberately empty, or AST built manually in
    /// tests). Inner parse errors are rewrapped with the passage name so the
    /// caller sees the same diagnostic the bulk loaders produce.
    /// </summary>
    private static void HydratePassageFromBody(HarlowePassage passage)
    {
      if (passage.Ast != null) return;
      if (passage.Body == null) return;

      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();
      Ast.Body.PassageBody ast;
      try
      {
        var tokens = tokenizer.Tokenize(passage.Body);
        ast = bodyParser.Parse(tokens);
      }
      catch (HarloweParseException ex) when (ex.PassageName == null)
      {
        throw new HarloweParseException(ex.RawMessage, ex.Line, ex.Column, passage.Name, ex);
      }

      passage.Ast = ast;
      if (passage.RawBody == null) passage.RawBody = passage.Body;
      if (passage.Branches == null) passage.Branches = BranchCollector.Collect(ast);
      passage.Body = BodyTextRenderer.Render(ast);
    }

    /// <summary>
    /// Compute the next free numeric pid: the maximum existing numeric pid
    /// plus one, or 1 if no numeric pids exist yet. Skips non-numeric pids
    /// rather than rejecting them — a story may legitimately carry pids the
    /// reader was given verbatim. The synthesized value is always numeric so
    /// it round-trips through Twine 2's <c>pid="…"</c> attribute.
    /// </summary>
    private int NextAvailablePid()
    {
      int max = 0;
      foreach (var p in _passages.Values)
      {
        if (int.TryParse(p.Pid, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > max)
          max = n;
      }
      return max + 1;
    }

    /// <summary>
    /// Removes the passage with <paramref name="name"/>. Returns true when a
    /// passage was removed, false when no passage with that name existed —
    /// matches the <see cref="Dictionary{TKey, TValue}.Remove(TKey)"/>
    /// contract callers will expect. Removing the start passage leaves
    /// <see cref="StartNode"/> dangling; <see cref="GetStartPassage"/>
    /// returns null in that case, the same as for any other unresolvable
    /// pid. Pids of remaining passages are not renumbered.
    /// </summary>
    public bool RemovePassage(string name)
    {
      if (name == null) return false;
      if (!_passages.Remove(name)) return false;
      _passageOrder.Remove(name);
      return true;
    }

    /// <summary>
    /// Renames the passage <paramref name="oldName"/> to
    /// <paramref name="newName"/>, re-keying the internal lookup so
    /// <see cref="GetPassage"/> still works after the rename. Returns false
    /// if no passage with <paramref name="oldName"/> exists or if a
    /// different passage already uses <paramref name="newName"/>; in that
    /// case nothing is mutated. Mutating <see cref="HarlowePassage.Name"/>
    /// directly silently corrupts the lookup, so always go through this
    /// method.
    /// </summary>
    public bool RenamePassage(string oldName, string newName)
    {
      if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return false;
      if (oldName == newName) return _passages.ContainsKey(oldName);
      if (!_passages.TryGetValue(oldName, out var passage)) return false;
      if (_passages.ContainsKey(newName)) return false;
      _passages.Remove(oldName);
      passage.Name = newName;
      _passages.Add(newName, passage);
      // Replace the entry in the order list in place so the rename keeps the
      // passage's iteration position — important for the Twee writer, which
      // emits in this order, and for any editing UI that displays it.
      int idx = _passageOrder.IndexOf(oldName);
      if (idx >= 0) _passageOrder[idx] = newName;
      return true;
    }

    /// <summary>
    /// Enumerates passages in load order — the order they were added to the
    /// story, preserved across renames and surviving removals. Backed by an
    /// explicit list so the order is a real API contract and not a side
    /// effect of <see cref="Dictionary{TKey, TValue}"/>'s insertion-order
    /// behaviour (which isn't documented as stable across runtimes). Used by
    /// <see cref="Twee.TweeWriter"/> to produce stable output; also useful
    /// to editing consumers iterating the story.
    /// </summary>
    public IEnumerable<HarlowePassage> Passages
    {
      get
      {
        for (int i = 0; i < _passageOrder.Count; i++)
          yield return _passages[_passageOrder[i]];
      }
    }

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
    /// passage exists — does not throw on miss.
    ///
    /// <para>Throws <see cref="InvalidOperationException"/> if the resolved
    /// passage's <see cref="HarlowePassage.Name"/> no longer matches the
    /// requested key, which means a consumer has mutated the public
    /// <see cref="HarlowePassage.Name"/> field directly and corrupted the
    /// internal index. Use <see cref="RenamePassage"/> instead — it re-keys
    /// the lookup atomically.</para>
    /// </summary>
    public HarlowePassage GetPassage(string passageName)
    {
      if (passageName == null) return null;
      if (!_passages.TryGetValue(passageName, out var passage)) return null;
      if (passage.Name != passageName)
        throw new InvalidOperationException(
          $"Passage lookup integrity error: dictionary key '{passageName}' does not match passage.Name '{passage.Name}'. " +
          "Did you mutate HarlowePassage.Name directly? Use Harlowe.RenamePassage instead.");
      return passage;
    }

    /// <summary>
    /// Returns the passage whose <see cref="HarlowePassage.Pid"/> matches
    /// <see cref="StartNode"/>. Returns null if the story has no passages or
    /// the start node pid does not match any passage. Used by
    /// <c>StorySession</c> to find the initial passage by pid without exposing
    /// the internal pid-indexed structure.
    /// </summary>
    public HarlowePassage GetStartPassage() => GetPassageByPid(StartNode);

    /// <summary>
    /// Look up a passage by its <see cref="HarlowePassage.Pid"/>. Returns null
    /// when no passage carries that pid or when <paramref name="pid"/> is
    /// null/empty. Useful for tooling that walks Twine's pid-indexed graph
    /// (story maps, link targets) without scanning <see cref="Passages"/>.
    /// Lookup is linear in passage count; cache the result if hot.
    /// </summary>
    public HarlowePassage GetPassageByPid(string pid)
    {
      if (string.IsNullOrEmpty(pid)) return null;
      foreach (var p in _passages.Values)
        if (p.Pid == pid) return p;
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
      _passageOrder = new List<string>();
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
          Branches = BranchCollector.Collect(ast),
          Body = BodyTextRenderer.Render(ast),
          RawBody = raw,
        };

        try { _passages.Add(passage.Name, passage); }
        catch (ArgumentException ex)
        {
          // Dictionary.Add throws on duplicate. Surface as the canonical
          // loader exception so HTML and Twee front-ends report bad input
          // the same way.
          throw new HarloweParseException(
            $"duplicate passage name '{passage.Name}'", -1, -1, passage.Name, ex);
        }
        _passageOrder.Add(passage.Name);
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

  }
}
