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

    private HarloweProfile _profileOverride;   // null = follow FormatVersion

    /// <summary>
    /// The compatibility profile this story's semantics come from: the major
    /// declared in <see cref="FormatVersion"/>, or an explicit override set by
    /// the host. Delivers per-major lock-in — a story keeps the semantics of
    /// the major it declares, indefinitely. See <see cref="HarloweProfile"/>
    /// for the switch model and <see cref="GetCompatibilityNotices"/> for the
    /// cases worth telling the author about.
    ///
    /// <para><b>Computed, never cached</b>, so it cannot fall out of step with
    /// <see cref="FormatVersion"/> — the same per-call model
    /// <see cref="GetParseErrors"/> and <see cref="GetBrokenLinks"/> use.</para>
    ///
    /// <para><b>Setting this does not re-parse passages already loaded</b>, and
    /// neither does assigning <see cref="FormatVersion"/>. Some switches (the
    /// comment markup among them) are lexical, so a story's bodies are already
    /// tokenized by the time either can be assigned; a metadata assignment
    /// silently re-tokenizing the whole story would be more surprising than
    /// the stale rule. To load <em>under</em> a profile, pass it to the loader
    /// — <see cref="Harlowe(string, HarloweProfile)"/> or
    /// <see cref="Twee.TweeReader(HarloweProfile)"/> — which is the only way
    /// to affect lexical switches. Passages added or re-parsed after the
    /// assignment do follow it.</para>
    /// </summary>
    public HarloweProfile Profile
    {
      get { return _profileOverride ?? HarloweProfile.Resolve(FormatVersion); }
      set { _profileOverride = value; }
    }

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
    public Harlowe(string htmlText) : this(htmlText, null) { }

    /// <summary>
    /// Parses a Twine HTML export under an explicit compatibility profile,
    /// overriding the major the export's <c>format-version</c> declares. Null
    /// follows the declaration, making this identical to
    /// <see cref="Harlowe(string)"/>.
    ///
    /// <para>The override belongs on the loader rather than on
    /// <see cref="Profile"/> because some switches are lexical: by the time a
    /// caller could assign the property, every passage body has been
    /// tokenized. This is the only entry point that can affect them.</para>
    /// </summary>
    public Harlowe(string htmlText, HarloweProfile profile)
    {
      // Before any parsing: Parse() lexes passage bodies under Profile, and a
      // lexical switch cannot be applied retroactively.
      _profileOverride = profile;

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
    /// spec, and on an explicit <see cref="HarlowePassage.Pid"/> another
    /// passage already carries — pid lookups (<see cref="GetPassageByPid"/>,
    /// and through it <see cref="GetStartPassage"/>) resolve a shared pid to
    /// whichever passage enumerates first, which isn't deterministic across
    /// runtimes. If <see cref="HarlowePassage.Pid"/> is null/empty a fresh
    /// numeric pid is synthesized — the maximum existing numeric pid plus one,
    /// so removals and explicit pid assignment can't collide with the
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
      else if (GetPassageByPid(passage.Pid) != null)
        throw new ArgumentException(
          $"a passage with pid '{passage.Pid}' already exists", nameof(passage));
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
    private void HydratePassageFromBody(HarlowePassage passage)
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
    ///
    /// <para>Instance-level because it lexes under <see cref="Profile"/>: a
    /// passage added to — or re-parsed within — a story declaring Harlowe 3
    /// must follow that story's rules, not the newest.</para>
    /// </summary>
    private Ast.Body.PassageBody ParseBodyToAst(string passageName, string rawBody)
    {
      var tokenizer = new HarloweTokenizer(Profile);
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
      var ast = new Ast.Body.PassageBody { Children = new List<Ast.Body.IBodyNode>() };
      ast.Children.Add(new Ast.Body.ParseErrorNode
      {
        Message = FormatParseError(passageName, detail, ex.Line, ex.Column),
        Detail = detail,
        Line = ex.Line,
        Column = ex.Column,
        OriginalSource = originalSource
      });
      return ast;
    }

    /// <summary>The rendered form of a parse error: <c>parse error in passage 'X' at line N, column N: detail</c> (position omitted when unknown).</summary>
    internal static string FormatParseError(string passageName, string detail, int line, int column)
      => $"parse error in passage '{passageName}'{SourcePosition.AtClause(line, column)}: {detail}";

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
    /// Rebuild every <see cref="Ast.Body.ParseErrorNode"/> message in
    /// <paramref name="ast"/> with the passage-name context, via
    /// <see cref="ParseErrorCollector"/> — the same walk
    /// <see cref="GetParseErrors"/> uses, so an error anywhere that report can
    /// see (hooks, attached hooks, inline-format spans) renders decorated too.
    /// The body parser emits ParseErrorNodes without knowing which passage it's
    /// parsing; loaders call this once after parse. Rebuilding from
    /// <see cref="Ast.Body.ParseErrorNode.Detail"/> is inherently idempotent; a
    /// hand-built node with no <c>Detail</c> keeps its message untouched.
    /// </summary>
    internal static void DecorateParseErrors(Ast.Body.PassageBody ast, string passageName)
    {
      var nodes = ParseErrorCollector.Collect(ast);
      for (int i = 0; i < nodes.Count; i++)
      {
        var err = nodes[i];
        if (err.Detail == null) continue;
        err.Message = FormatParseError(passageName, err.Detail, err.Line, err.Column);
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
    /// Anything worth telling the author about how this story's declared
    /// format version was interpreted. Empty in the nominal case — a story
    /// declaring a major we implement — and never throws.
    ///
    /// <para><b>What this is for.</b> The third sibling to
    /// <see cref="GetBrokenLinks"/> and <see cref="GetParseErrors"/>: a host
    /// engine calls it once at load and shows the results to the developer.
    /// Loading never fails over a format version, so without this the choice
    /// of profile is invisible — and a story running under semantics it didn't
    /// ask for shows symptoms far from the cause.</para>
    ///
    /// <para>Four cases, in the shape a caller has to handle them:</para>
    /// <list type="bullet">
    ///   <item><description>No version declared — <see cref="NoticeSeverity.Info"/>.
    ///     Ordinary for hand-built and test stories; real Twine exports always
    ///     declare one. Runs under the newest profile.</description></item>
    ///   <item><description>A major we implement — no notice.</description></item>
    ///   <item><description>A major below 3 — <see cref="NoticeSeverity.Warning"/>,
    ///     clamped to <see cref="HarloweProfile.V3"/>: 1.x/2.x were never
    ///     audited here, so their semantics aren't reconstructed, but the
    ///     nearest audited major is closer than the newest.</description></item>
    ///   <item><description>A major newer than we implement, or an unparseable
    ///     version — <see cref="NoticeSeverity.Warning"/>, running under the
    ///     newest. The two are distinguished in
    ///     <see cref="CompatibilityNotice.Detail"/>, since one means "this
    ///     library is behind" and the other means "this metadata is
    ///     malformed".</description></item>
    /// </list>
    ///
    /// <para>An explicit <see cref="Profile"/> override is reported too — the
    /// host chose it, but nothing else records that the story's own
    /// declaration was set aside.</para>
    /// </summary>
    public IReadOnlyList<CompatibilityNotice> GetCompatibilityNotices()
    {
      var notices = new List<CompatibilityNotice>();
      string declared = FormatVersion ?? string.Empty;
      var profile = Profile;

      if (_profileOverride != null)
      {
        string notice = declared.Length == 0
          ? "the host set the compatibility profile explicitly (the story declares no format-version)"
          : "the host set the compatibility profile explicitly, overriding the story's declared format-version '"
            + declared + "'";
        notices.Add(new CompatibilityNotice
        {
          Severity = NoticeSeverity.Info,
          DeclaredVersion = declared,
          Profile = profile,
          Detail = notice,
        });
        return notices;
      }

      if (declared.Length == 0)
      {
        notices.Add(new CompatibilityNotice
        {
          Severity = NoticeSeverity.Info,
          DeclaredVersion = declared,
          Profile = profile,
          Detail = "no format-version was declared, so the newest supported semantics are used",
        });
        return notices;
      }

      // The same parse the profile was selected by, so a notice can never
      // disagree with the profile it describes about what the version said.
      // Classified separately from Resolve because "clamped down" and
      // "defaulted up" are opposite situations wanting opposite advice, and
      // Resolve returns only the destination.
      int major = HarloweProfile.ParseMajor(declared);
      string detail = null;

      if (major < 0)
        detail = "the declared format-version '" + declared
          + "' could not be read as a version number, so the newest supported semantics are used";
      else if (major < 3)
        detail = "the declared format-version '" + declared
          + "' predates Harlowe 3, the oldest major this library reproduces";
      else if (major > HarloweProfile.LatestKnownMajor)
        detail = "the declared format-version '" + declared
          + "' is from a newer major than this library implements, so the newest supported semantics are used";

      if (detail != null)
        notices.Add(new CompatibilityNotice
        {
          Severity = NoticeSeverity.Warning,
          DeclaredVersion = declared,
          Profile = profile,
          Detail = detail,
        });

      return notices;
    }

    /// <summary>
    /// Every place a passage failed to parse — in passage order, then source
    /// order. Empty when the story is clean; never throws.
    ///
    /// <para><b>What this is for.</b> The sibling of <see cref="GetBrokenLinks"/>,
    /// and the same deal: a host engine calls it once at load and shows the
    /// results to the developer. It's needed precisely <em>because</em> the
    /// loaders are tolerant — a passage that won't parse doesn't abort the load,
    /// it gets a synthetic error stub and stays quiet until a player walks into
    /// it. Without this, a syntax error down an unplayed branch ships.</para>
    ///
    /// <para><b>Two shapes.</b> <see cref="ParseError.IsWholePassage"/> tells them
    /// apart: either the whole body failed (the passage is a stub and will render
    /// nothing but the error), or the parser recovered around one bad construct
    /// and the rest of the passage still works. Recovered errors nested inside
    /// hooks are found too, so a broken macro inside an <c>(if:)</c> branch is
    /// reported.</para>
    /// </summary>
    public IReadOnlyList<ParseError> GetParseErrors()
    {
      var errors = new List<ParseError>();
      for (int i = 0; i < _passageOrder.Count; i++)
      {
        var passage = _passages[_passageOrder[i]];
        if (passage.Ast == null) continue;

        bool whole = Ast.Body.ParseErrorNode.IsWhollyParseError(passage.Ast);
        var nodes = ParseErrorCollector.Collect(passage.Ast);
        for (int j = 0; j < nodes.Count; j++)
        {
          var node = nodes[j];
          errors.Add(new ParseError
          {
            PassageName = passage.Name,
            Detail = node.Detail,
            Source = node.OriginalSource,
            Line = node.Line,
            Column = node.Column,
            IsWholePassage = whole
          });
        }
      }
      return errors;
    }

    /// <summary>
    /// Every navigation reference in the story that points at a passage which
    /// doesn't exist — in passage order, then source order. Empty when the story
    /// is clean; never throws.
    ///
    /// <para><b>What this is for.</b> A host engine calls it once at load and
    /// shows the results to the developer: each <see cref="BrokenLink"/> carries
    /// its passage, line, column, and a ready-to-print
    /// <see cref="BrokenLink.Message"/>. It is also the answer to the mess
    /// <see cref="RemovePassage"/> leaves behind — deleting a passage silently
    /// orphans every reference into it.</para>
    ///
    /// <para><b>Both syntaxes.</b> The <c>[[…]]</c> forms <em>and</em> a literal
    /// passage name given to <c>(goto:)</c>, <c>(display:)</c>,
    /// <c>(link-goto:)</c>, or the <c>(click-goto:)</c> family — including calls
    /// nested inside hooks and expressions, so a <c>(goto: "Typo")</c> buried in
    /// an <c>(if:)</c> branch is found. Reporting only the <c>[[…]]</c> half
    /// would be worse than useless: an engine would tell the author their
    /// navigation is clean while a dead <c>(goto:)</c> waits down a branch they
    /// hadn't played.</para>
    ///
    /// <para><b>What it can't see.</b> A <em>computed</em> target
    /// (<c>(goto: $next)</c>) is beyond any static check and is skipped rather
    /// than guessed at — those still surface as in-prose errors at render time.
    /// And a <em>wholly</em> unparseable passage is a synthetic stub with no
    /// references left to find, so nothing in it is reported here —
    /// <see cref="GetParseErrors"/> is what surfaces that passage. (A passage the
    /// parser merely <em>recovered</em> inside still has a real AST, so its
    /// surviving links and macros are scanned as normal.)</para>
    /// </summary>
    public IReadOnlyList<BrokenLink> GetBrokenLinks()
    {
      var broken = new List<BrokenLink>();
      for (int i = 0; i < _passageOrder.Count; i++)
      {
        var passage = _passages[_passageOrder[i]];
        if (passage.Ast == null) continue;

        var references = ReferenceCollector.Collect(passage.Ast);
        for (int j = 0; j < references.Count; j++)
        {
          var reference = references[j];
          if (reference.Target != null && _passages.ContainsKey(reference.Target)) continue;
          broken.Add(new BrokenLink
          {
            PassageName = passage.Name,
            Target = reference.Target,
            LinkText = reference.LinkText,
            MacroName = reference.MacroName,
            Line = reference.Line,
            Column = reference.Column
          });
        }
      }
      return broken;
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
    ///
    /// <para>A <paramref name="newName"/> containing the link markup's own
    /// separators — <c>-&gt;</c>, <c>&lt;-</c>, <c>|</c>, or <c>]]</c> — cannot
    /// be written into <c>[[…]]</c> syntax without changing how the link parses
    /// (e.g. retargeting <c>[[Old]]</c> at <c>A-&gt;B</c> yields <c>[[A-&gt;B]]</c>,
    /// text <c>A</c> linking to <c>B</c>). The rename is <em>refused</em> (returns
    /// false, nothing mutated) rather than corrupting every inbound link; rename
    /// with <paramref name="updateInboundLinks"/> false if such a name is really
    /// wanted, and retarget inbound references yourself.</para>
    /// </summary>
    public bool RenamePassage(string oldName, string newName, bool updateInboundLinks = true)
    {
      if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return false;
      if (oldName == newName) return _passages.ContainsKey(oldName);
      if (!_passages.TryGetValue(oldName, out var passage)) return false;
      if (_passages.ContainsKey(newName)) return false;
      if (updateInboundLinks && ContainsLinkSeparator(newName)) return false;
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
    /// not handled (bracket-in-link is ambiguous in the markup, as in Twine).
    /// The new name is guaranteed free of link separators — callers reach here
    /// only through <see cref="RenamePassage"/>, which refuses names that
    /// <see cref="ContainsLinkSeparator"/> flags.</para>
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
    /// True when <paramref name="name"/> contains a sequence the <c>[[…]]</c>
    /// link markup treats as structure — <c>-&gt;</c>, <c>&lt;-</c>, <c>|</c>,
    /// or <c>]]</c> — so substituting it into a link's target slot would change
    /// how the link parses (rightmost <c>-&gt;</c>/<c>|</c> wins, else leftmost
    /// <c>&lt;-</c>, and <c>]]</c> ends the link outright).
    /// </summary>
    private static bool ContainsLinkSeparator(string name)
      => name.Contains("->") || name.Contains("<-") || name.IndexOf('|') >= 0 || name.Contains("]]");

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
    /// missing its <c>name</c> or <c>pid</c> attribute, a pid two passages
    /// share — surface as <see cref="HarloweParseException"/> so HTML loading
    /// stays on the project's documented error contract instead of NRE'ing on
    /// malformed input. The duplicate-pid check exists because pid lookups
    /// (<see cref="GetStartPassage"/>) resolve a shared pid to whichever
    /// passage enumerates first — non-deterministic across runtimes on a
    /// story that would otherwise load without error.</para>
    /// </summary>
    private void Parse(HtmlNodeCollection passageNodes)
    {
      _passages = new Dictionary<string, HarlowePassage>();
      _passageOrder = new List<string>();
      // HtmlAgilityPack returns null from SelectNodes when no nodes match. A
      // story with zero passages is structurally valid (matches the empty
      // story from `new Harlowe()`); treat it as such instead of throwing.
      if (passageNodes == null) return;

      // Lexes under the story's profile: ParseStoryData ran first, so
      // FormatVersion (and any ctor-supplied override) is already in place and
      // Profile resolves to the major this story declared.
      var tokenizer = new HarloweTokenizer(Profile);
      var bodyParser = new HarloweBodyParser();
      var pidOwners = new Dictionary<string, string>();   // pid -> first passage carrying it

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
        string pid = HtmlEntity.DeEntitize(pidAttr.Value);
        if (pidOwners.TryGetValue(pid, out string firstOwner))
          throw new HarloweParseException(
            $"duplicate passage pid '{pid}' (passages '{firstOwner}' and '{passageName}')",
            -1, -1, passageName);
        pidOwners.Add(pid, passageName);

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
          Pid = pid,
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
