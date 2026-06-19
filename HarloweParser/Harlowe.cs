using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
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
      // Relative XPath (".//") scopes the search to the selected story. A bare
      // "//tw-passagedata" is document-absolute and would pull in every story's
      // passages from a multi-story archive (Twine's "Archive" export), merging
      // them under the first story's metadata or aborting on a cross-story
      // duplicate name.
      Parse(storyNode.SelectNodes(".//tw-passagedata"));
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
    /// <see cref="HarlowePassage.Branches"/> is collected from the AST if not
    /// already set; the source body (<see cref="HarlowePassage.Body"/> /
    /// <see cref="HarlowePassage.RawBody"/>, one and the same string) is left
    /// untouched so it round-trips intact.</para>
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
    /// Parse the passage's source body (<see cref="HarlowePassage.RawBody"/>,
    /// a.k.a. <see cref="HarlowePassage.Body"/>) into
    /// <see cref="HarlowePassage.Ast"/> and the derived
    /// <see cref="HarlowePassage.Branches"/>, for passages that were
    /// constructed by hand without going through the HTML or Twee loaders.
    /// Skips passages that already have an AST or whose body is null — both
    /// cases mean the caller is responsible for their own shape
    /// (loader-populated, deliberately empty, or AST built manually in tests).
    ///
    /// <para>A parse error doesn't throw out of <see cref="AddPassage"/> —
    /// the passage is hydrated with a synthetic <see cref="Ast.Body.ParseErrorNode"/>
    /// AST so the rest of the story remains usable and the broken passage
    /// renders an in-prose error at render time. Matches the bulk-loader
    /// recovery contract so the editing API and the file loaders behave the
    /// same way on identical input.</para>
    /// </summary>
    private static void HydratePassageFromBody(HarlowePassage passage)
    {
      if (passage.Ast != null) return;
      if (passage.RawBody == null) return;

      passage.Ast = ParseBodyToAst(passage.Name, passage.RawBody);
      if (passage.Branches == null) passage.Branches = BranchCollector.Collect(passage.Ast);
    }

    /// <summary>
    /// Tokenize + body-parse <paramref name="rawBody"/> into a decorated
    /// <see cref="Ast.Body.PassageBody"/>, applying the same per-passage
    /// parse-error recovery the loaders use: a tokenizer failure becomes a
    /// synthetic <see cref="Ast.Body.ParseErrorNode"/> stub, the body parser's
    /// own per-node recovery is prefixed with <paramref name="passageName"/>,
    /// and a wholly-failed stub gets its original source stashed. Shared by
    /// <see cref="HydratePassageFromBody"/> (initial hydrate of a hand-built
    /// passage) and <see cref="RewriteInboundLinks"/> (reparse after a
    /// link-target rewrite), so both produce the identical AST shape the bulk
    /// loaders do.
    /// </summary>
    private static Ast.Body.PassageBody ParseBodyToAst(string passageName, string rawBody)
    {
      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();
      Ast.Body.PassageBody ast;
      try
      {
        var tokens = tokenizer.Tokenize(rawBody);
        ast = bodyParser.Parse(tokens, rawBody);
      }
      catch (HarloweParseException ex)
      {
        ast = MakeParseErrorAst(passageName, ex, rawBody);
      }
      DecorateParseErrors(ast, passageName);
      EnsureWholeStubOriginalSource(ast, rawBody);
      return ast;
    }

    /// <summary>
    /// Build a synthetic <see cref="Ast.Body.PassageBody"/> wrapping a single
    /// <see cref="Ast.Body.ParseErrorNode"/>. Used by the loader paths and
    /// <see cref="AddPassage"/> to keep the rest of the story loadable when
    /// the tokenizer fails (the body parser handles its own per-node recovery
    /// internally — see <see cref="Parsing.HarloweBodyParser"/>). <c>internal</c>
    /// so the Twee reader can share the same recovery shape without
    /// duplicating the message format. <paramref name="originalSource"/> is
    /// stashed on the node so <see cref="Twee.MarkupPrinter"/> can round-trip
    /// the broken source even when the carrying passage has no
    /// <see cref="HarlowePassage.RawBody"/>.
    /// </summary>
    internal static Ast.Body.PassageBody MakeParseErrorAst(string passageName, HarloweParseException ex, string originalSource)
    {
      string detail = ex.RawMessage ?? ex.Message ?? "parse error";
      string where = ex.Line > 0 ? $" at line {ex.Line}, column {ex.Column}" : string.Empty;
      var message = $"parse error in passage '{passageName}'{where}: {detail}";
      var ast = new Ast.Body.PassageBody { Children = new List<Ast.Body.IBodyNode>() };
      ast.Children.Add(new Ast.Body.ParseErrorNode { Message = message, OriginalSource = originalSource });
      return ast;
    }

    /// <summary>
    /// Walk the top-level children of <paramref name="ast"/> and prepend the
    /// passage-name context to any <see cref="Ast.Body.ParseErrorNode"/>
    /// message. The body parser recovers per-node and emits these without
    /// knowing which passage it's parsing; the loaders call this once after
    /// parse to inject the surrounding context so the rendered error names
    /// the broken passage. Idempotent — re-running on the same AST does not
    /// stack prefixes (the helper checks for the marker).
    /// </summary>
    /// <summary>
    /// When <paramref name="ast"/> is the loader-stub shape (a single
    /// <see cref="Ast.Body.ParseErrorNode"/> child) and that node has no
    /// <see cref="Ast.Body.ParseErrorNode.OriginalSource"/>, set it from
    /// <paramref name="source"/>. Covers the case where the body parser's
    /// own per-node recovery (which doesn't know the source) failed at the
    /// very first node, leaving the AST indistinguishable from the
    /// tokenizer-failure stub but with <c>OriginalSource = null</c>.
    /// </summary>
    internal static void EnsureWholeStubOriginalSource(Ast.Body.PassageBody ast, string source)
    {
      if (source == null) return;
      if (!Ast.Body.ParseErrorNode.IsWhollyParseError(ast)) return;
      if (ast.Children[0] is Ast.Body.ParseErrorNode err && err.OriginalSource == null)
        err.OriginalSource = source;
    }

    /// <summary>
    /// Recursively walk <paramref name="ast"/> and prepend the passage-name
    /// context to every <see cref="Ast.Body.ParseErrorNode"/> message,
    /// including ones the body parser placed inside hook children (per-node
    /// recovery inside <c>[hook contents]</c>) or attached hooks on macro and
    /// changer-chain nodes. The body parser emits ParseErrorNodes without
    /// knowing which passage it's parsing; loaders call this once after parse
    /// so the rendered error names the broken passage.
    ///
    /// <para>Idempotent — re-running on the same AST does not stack prefixes
    /// (the marker check skips already-decorated nodes). String comparisons
    /// use <see cref="StringComparison.Ordinal"/> so the marker detection is
    /// locale-independent.</para>
    /// </summary>
    internal static void DecorateParseErrors(Ast.Body.PassageBody ast, string passageName)
    {
      if (ast?.Children == null) return;
      DecorateChildren(ast.Children, passageName);
    }

    private static void DecorateChildren(List<Ast.Body.IBodyNode> children, string passageName)
    {
      const string marker = "parse error in passage";
      const string prefix = "parse error";
      for (int i = 0; i < children.Count; i++)
      {
        var child = children[i];
        if (child is Ast.Body.ParseErrorNode err)
        {
          var msg = err.Message ?? string.Empty;
          if (msg.StartsWith(marker, StringComparison.Ordinal)) continue;
          // Drop the leading "parse error" since we re-introduce it with the
          // passage context so the rendered string reads naturally.
          if (msg.StartsWith(prefix, StringComparison.Ordinal))
            msg = msg.Substring(prefix.Length);
          err.Message = $"parse error in passage '{passageName}'{msg}";
        }
        else if (child is Ast.Body.HookNode hook && hook.Children != null)
        {
          DecorateChildren(hook.Children, passageName);
        }
        else if (child is Ast.Body.MacroNode macro && macro.AttachedHook?.Children != null)
        {
          DecorateChildren(macro.AttachedHook.Children, passageName);
        }
        else if (child is Ast.Body.ChangerChainNode chain && chain.AttachedHook?.Children != null)
        {
          DecorateChildren(chain.AttachedHook.Children, passageName);
        }
      }
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
    ///
    /// <para>When <paramref name="updateInboundLinks"/> is true (the default),
    /// inbound <c>[[…]]</c> links pointing at <paramref name="oldName"/> are
    /// retargeted to <paramref name="newName"/> across the whole story, so
    /// navigation and re-serialization keep working — see
    /// <see cref="RewriteInboundLinks"/> for the exact forms handled and the
    /// limitations (notably: macro string targets like <c>(goto: "old")</c> are
    /// <em>not</em> rewritten). Pass false to rename as a pure index re-key and
    /// take responsibility for link updates yourself — mirrors the Twine editor's
    /// <c>dontUpdateOthers</c> option.</para>
    /// </summary>
    public bool RenamePassage(string oldName, string newName, bool updateInboundLinks = true)
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
      if (updateInboundLinks) RewriteInboundLinks(oldName, newName);
      return true;
    }

    /// <summary>
    /// Retargets inbound <c>[[…]]</c> links to a renamed passage across the
    /// whole story, mirroring the Twine editor (klembot/twinejs
    /// <c>update-passage.ts</c>). The three Harlowe link forms —
    /// <c>[[old]]</c>, <c>[[display-&gt;old]]</c>, <c>[[old&lt;-display]]</c> —
    /// have their <em>target</em> rewritten to <paramref name="newName"/> in
    /// each passage's raw source (the collapsed <c>[[old]]</c> form updates its
    /// visible text too, since text and target are one and the same there).
    /// Formatting is preserved: <see cref="HarlowePassage.RawBody"/> is edited
    /// in place and the AST reparsed, rather than re-canonicalized through
    /// <see cref="Twee.MarkupPrinter"/>, so the change stays scoped to the link
    /// — consistent with the lazy-reserialization model. The renamed passage is
    /// included, so self-links update too.
    ///
    /// <para>Like Twine, only literal <c>[[…]]</c> link syntax is rewritten.
    /// Passage names referenced from macro string arguments —
    /// <c>(goto: "old")</c>, <c>(display: "old")</c>, <c>(link-goto:)</c> — are
    /// <em>not</em> updated: a string literal can't be reliably told apart from
    /// any other string, so rewriting it is the caller's responsibility.
    /// Passage names that themselves contain <c>[</c> or <c>]</c> are likewise
    /// not handled (bracket-in-link is ambiguous in the markup, as in Twine).</para>
    /// </summary>
    private void RewriteInboundLinks(string oldName, string newName)
    {
      string escaped = Regex.Escape(oldName);
      // '$' is the only metacharacter in a .NET replacement string; double it so
      // a new name containing '$' is inserted literally (matches Twine).
      string replacement = newName.Replace("$", "$$");

      // The target sits immediately before ]] in every form, so anchoring on
      // that keeps these from matching display text or unrelated prose.
      var simple = new Regex(@"\[\[" + escaped + @"\]\]");
      var rightArrow = new Regex(@"\[\[(.*?)->" + escaped + @"\]\]");
      var leftArrow = new Regex(@"\[\[" + escaped + @"(<-.*?)\]\]");

      foreach (var passage in _passages.Values)
      {
        if (passage.RawBody == null) continue;
        string body = passage.RawBody;
        string updated = simple.Replace(body, "[[" + replacement + "]]");
        updated = rightArrow.Replace(updated, "[[$1->" + replacement + "]]");
        updated = leftArrow.Replace(updated, "[[" + replacement + "$1]]");
        if (updated == body) continue;
        passage.RawBody = updated;
        passage.Ast = ParseBodyToAst(passage.Name, updated);
        passage.Branches = BranchCollector.Collect(passage.Ast);
      }
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
      // Attribute values arrive entity-encoded (name="Cake &amp; Tea") and must
      // be decoded like the body is, or names diverge from the decoded link
      // targets that reference them.
      StoryName = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("name", ""));
      StartNode = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("startnode", "0"));
      Creator = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("creator", ""));
      CreatorVersion = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("creator-version", ""));
      Ifid = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("ifid", ""));
      Format = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("format", ""));
      FormatVersion = HtmlEntity.DeEntitize(storyNode.GetAttributeValue("format-version", ""));
    }

    /// <summary>
    /// Looks up a passage by its author-facing name. Returns null if no such
    /// passage exists — does not throw on miss.
    ///
    /// <para>If a consumer has mutated <see cref="HarlowePassage.Name"/>
    /// directly, the lookup still returns the dictionary entry — the
    /// dictionary key is what callers asked for. The mismatch corrupts the
    /// internal index, but throwing here would propagate out of the runtime
    /// hot path (<c>(display:)</c> and goto both reach this method) and break
    /// the documented in-prose error contract. Use <see cref="RenamePassage"/>
    /// to rename atomically; that's the supported path.</para>
    /// </summary>
    public HarlowePassage GetPassage(string passageName)
    {
      if (passageName == null) return null;
      if (!_passages.TryGetValue(passageName, out var passage)) return null;
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
    /// Returns the raw author source of the named passage's body. Returns
    /// <see cref="string.Empty"/> for unknown or null names — matches the
    /// rest of the lookup API's null-safe contract.
    /// </summary>
    public string GetPassageBody(string passageName)
    {
      if (passageName == null) return string.Empty;
      if (!_passages.TryGetValue(passageName, out var passage)) return string.Empty;
      // A hand-built passage may have its body left null (the caller supplied a
      // pre-parsed Ast and never set it). Coerce so the documented
      // "never-null" return contract holds for those too.
      return passage.RawBody ?? string.Empty;
    }

    /// <summary>
    /// Returns the outgoing branch links for the named passage. Returns null
    /// for unknown or null names; returns an empty list for known passages
    /// with no links. Null-safe to match <see cref="GetPassage"/> and
    /// <see cref="RemovePassage"/>.
    /// </summary>
    public List<Branch> GetPassageBranches(string passageName)
    {
      if (passageName == null) return null;
      if (!_passages.TryGetValue(passageName, out var passage)) return null;

      return passage.Branches;
    }

    /// <summary>
    /// Walks every <c>&lt;tw-passagedata&gt;</c> node, runs its inner HTML
    /// through entity decoding → tokenizer → body parser, and indexes the
    /// resulting <see cref="HarlowePassage"/> by name. <see cref="HarlowePassage.Branches"/>
    /// is filled by collecting every <see cref="LinkNode"/> in the AST;
    /// <see cref="HarlowePassage.RawBody"/> holds the raw author source.
    ///
    /// <para>Null/missing structural pieces — no passages at all, a passage
    /// missing its <c>name</c> or <c>pid</c> attribute — surface as
    /// <see cref="HarloweParseException"/> so HTML loading stays on the
    /// project's documented error contract instead of NRE'ing on malformed
    /// input.</para>
    /// </summary>
    private void Parse(HtmlNodeCollection passageNodes)
    {
      _passages = new Dictionary<string, HarlowePassage>();
      _passageOrder = new List<string>();
      // HtmlAgilityPack returns null from SelectNodes when no nodes match. A
      // story with zero passages is structurally valid (matches the empty
      // story from `new Harlowe()`); treat it as such instead of throwing.
      if (passageNodes == null) return;

      var tokenizer = new HarloweTokenizer();
      var bodyParser = new HarloweBodyParser();

      foreach (var passageNode in passageNodes)
      {
        var nameAttr = passageNode.Attributes["name"];
        if (nameAttr == null)
          throw new HarloweParseException(
            "<tw-passagedata> is missing required 'name' attribute", -1, -1, null);
        // Decode entities in the name so the passage index key matches link
        // targets, which are decoded along with the body below.
        string passageName = HtmlEntity.DeEntitize(nameAttr.Value);

        var pidAttr = passageNode.Attributes["pid"];
        if (pidAttr == null)
          throw new HarloweParseException(
            "<tw-passagedata> is missing required 'pid' attribute", -1, -1, passageName);

        string raw = HtmlEntity.DeEntitize(passageNode.InnerHtml ?? string.Empty);

        Ast.Body.PassageBody ast;
        try
        {
          var tokens = tokenizer.Tokenize(raw);
          ast = bodyParser.Parse(tokens, raw);
        }
        catch (HarloweParseException ex)
        {
          // One broken passage shouldn't take the whole story down — a typo
          // in `(if: $x to 5)` used to load and error at runtime; we keep the
          // story loadable by substituting a synthetic AST that renders the
          // parse message in place of the passage's prose. The original
          // RawBody is preserved so Twee writers can still emit the source.
          ast = MakeParseErrorAst(passageName, ex, raw);
        }
        // The body parser also recovers per-node and may leave ParseErrorNodes
        // alongside successfully-parsed children — decorate those with the
        // passage name so the rendered error names the broken passage.
        DecorateParseErrors(ast, passageName);
        EnsureWholeStubOriginalSource(ast, raw);

        var passage = new HarlowePassage
        {
          Pid = HtmlEntity.DeEntitize(pidAttr.Value),
          Name = passageName,
          Tags = ParseTags(HtmlEntity.DeEntitize(passageNode.GetAttributeValue("tags", string.Empty))),
          Ast = ast,
          Branches = BranchCollector.Collect(ast),
          // RawBody holds the raw author source verbatim (Body is its alias),
          // so write-out round-trips clean passages and GetPassageBody returns
          // real source even for parse-error-recovered passages.
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
