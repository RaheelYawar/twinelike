// Reference Harlowe's own Jasmine specs, as checks both engines can run.
//
// usage: node specs.mjs extract [--any-node]   reference specs -> .cache/specs/specs.json
//        node specs.mjs real       run them on real Harlowe 3.3.9 -> .cache/specs/real.json
//
// extract runs the pinned reference's spec files against recording stubs, with no
// browser and no Harlowe: a test keeps its steps up to the first point that needs
// a real page (an element query, a click, a timer). The specs are third-party
// (Zlib), so nothing extracted from them is committed.
import { existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import vm from 'node:vm';
import { CACHE, REPO } from './lib/paths.mjs';
import { formatFile, pins } from './lib/pins.mjs';

const OUT = join(CACHE, 'specs');

function extract(specRoot) {
  const walk = d => readdirSync(d).flatMap(n => {
    const p = join(d, n);
    return statSync(p).isDirectory() ? walk(p) : p.endsWith('.js') ? [p] : [];
  });

  let current = null;
  const stopAtDom = why => { if (current && !current.inBeforeAll && !current.stoppedBy) current.stoppedBy = why; };
  // A beforeAll's passages are wiped by the suite's per-test reset, so only its option changes last.
  const record = step => {
    if (!current || current.stoppedBy) return;
    if (current.inBeforeAll && step.op !== 'option') return;
    current.steps.push(step);
  };

  function dom() {
    const target = function () {};
    return new Proxy(target, {
      get(_, key) {
        if (key === Symbol.toPrimitive) return () => { stopAtDom('dom'); return 'DOM'; };
        if (key === 'then') return undefined;
        if (typeof key === 'symbol') return undefined;
        stopAtDom('dom');
        if (key === 'length') return 1;
        return dom();
      },
      apply() { stopAtDom('dom'); return dom(); },
    });
  }
  const isDom = v => typeof v === 'function';

  const expected = e => e instanceof RegExp || Object.prototype.toString.call(e) === '[object RegExp]'
    ? { regex: e.source, flags: e.flags } : { text: e };

  function matchers(actual, negated) {
    const portable = typeof actual === 'string';
    const op = (name, build) => (...args) => {
      if (!portable) { stopAtDom('dom-assert'); return; }
      if (build) record(build(...args));
      else stopAtDom(name);
    };
    const m = {
      markupToPrint: op('print', e => ({ op: negated ? 'notPrint' : 'print', source: actual, expected: expected(e) })),
      markupToError: op('error', () => ({ op: negated ? 'notError' : 'error', source: actual })),
      markupToBecome: op('markupToBecome'),
      markupToLoad: op('markupToLoad'),
      markupToMetadataError: op('markupToMetadataError'),
    };
    return new Proxy(m, {
      get(t, key) {
        if (key === 'not') return matchers(actual, !negated);
        if (key in t) return t[key];
        if (typeof key === 'symbol') return undefined;
        return () => { if (!portable || isDom(actual)) stopAtDom('dom-assert'); };
      },
    });
  }

  const scopes = [];
  const tests = [];
  const anyStub = () => new Proxy(function () {}, {
    get: (_, k) => (typeof k === 'symbol' ? undefined : anyStub()),
    apply: () => { stopAtDom('host-api'); return anyStub(); },
    construct: () => { stopAtDom('host-api'); return anyStub(); },
  });

  const utilsOptions = new Proxy({}, {
    set(target, key, value) {
      if (typeof value !== 'object') record({ op: 'option', name: String(key), value });
      target[key] = value;
      return true;
    },
  });

  const sandbox = {
    console,
    describe(title, fn) { scopes.push({ title, before: [], once: [] }); try { fn(); } finally { scopes.pop(); } },
    it(title, fn) { tests.push({ path: scopes.map(s => s.title), title, fn, once: scopes.flatMap(s => s.once), before: scopes.flatMap(s => s.before) }); },
    beforeEach(fn) { if (scopes.length) scopes[scopes.length - 1].before.push(fn); },
    beforeAll(fn) { if (scopes.length) scopes[scopes.length - 1].once.push(fn); },
    afterEach() {}, afterAll() {},
    expect: actual => matchers(actual, false),
    runPassage(text, name, tags) { record({ op: 'run', source: String(text), name: name ?? 'test', tags: tags ?? null }); return dom(); },
    createPassage(text, name, tags) { record({ op: 'create', source: String(text), name: name ?? 'test', tags: tags ?? null }); return []; },
    deletePassage(name) { record({ op: 'delete', name }); },
    goToPassage() { stopAtDom('goToPassage'); return dom(); },
    waitForGoto() { stopAtDom('timer'); },
    clearState() { record({ op: 'reset' }); },
    deserialiseState() { stopAtDom('host-api'); },
    loadAssets() { stopAtDom('host-api'); },
    checkMacroTypeSignature() { stopAtDom('type-signature'); },
    setTimeout() { stopAtDom('timer'); return 0; },
    clearTimeout() {}, setInterval() { stopAtDom('timer'); return 0; }, clearInterval() {},
    requestAnimationFrame() { stopAtDom('timer'); return 0; },
    jasmine: anyStub(), Macros: anyStub(), Utils: new Proxy(anyStub(), { get: (t, k) => (k === 'options' ? utilsOptions : t[k]) }), Passages: anyStub(), State: anyStub(), Engine: anyStub(),
    $: () => { stopAtDom('dom'); return dom(); },
    document: anyStub(), localStorage: anyStub(), location: { search: '' },
  };
  sandbox.window = sandbox;
  sandbox.fit = sandbox.it;
  sandbox.xit = () => {};
  sandbox.fdescribe = sandbox.describe;
  sandbox.xdescribe = () => {};
  const context = vm.createContext(sandbox);

  const failedToLoad = [];
  const files = walk(specRoot).sort();
  for (const file of files) {
    const rel = relative(specRoot, file).replace(/\\/g, '/');
    const before = tests.length;
    try { vm.runInContext(readFileSync(file, 'utf8'), context, { filename: rel, timeout: 5000 }); }
    catch (e) { failedToLoad.push(`${rel}: ${e.message}`); }
    for (let i = before; i < tests.length; i++) tests[i].file = rel;
  }

  const out = [];
  const stopped = {};
  let assertions = 0;
  for (const t of tests) {
    current = { steps: [], stoppedBy: null, inBeforeAll: true };
    try { for (const b of t.once) b(() => {}); } catch { /* A beforeAll that needs a DOM adds nothing. */ }
    current.inBeforeAll = false;
    try {
      for (const b of t.before) b(() => {});
      t.fn(() => {});
    } catch { stopAtDom('exception'); }
    const c = current;
    current = null;
    const checks = c.steps.filter(s => !['run', 'create', 'delete', 'reset', 'option'].includes(s.op)).length;
    assertions += checks;
    if (c.stoppedBy) stopped[c.stoppedBy] = (stopped[c.stoppedBy] ?? 0) + 1;
    out.push({ file: t.file, path: t.path, title: t.title, steps: c.steps, stoppedBy: c.stoppedBy, checks });
  }
  const portable = out.filter(t => t.checks > 0);
  console.log(`spec files ${files.length}, tests ${out.length}`);
  console.log(`tests with at least one portable check: ${portable.length} (fully portable: ${portable.filter(t => !t.stoppedBy).length})`);
  console.log(`portable checks: ${assertions}`);
  console.log(`why extraction stopped: ${JSON.stringify(stopped)}`);
  if (failedToLoad.length) console.log(`files that failed to load:\n  ${failedToLoad.join('\n  ')}`);
  return out;
}

// Runs in the page, with the same helpers and matchers as the reference's test_suite.html.
function runSpecTest(test) {
  const { $, State, Engine, Passages, Section, VarScope } = window.__H;
  const metadataSection = () => {
    const s = Section.create($('<p>'));
    s.stack = [{ tempVariables: Object.create(VarScope) }];
    return s;
  };
  const createPassage = (text, name, tags) => {
    Passages.clearTreeCache?.();
    Passages.clearStoryletCache?.();
    Passages.clearTagCache?.();
    Passages.set(name, Passages.create($('<div name="' + name + '" tags="' + (Array.isArray(tags) ? tags : tags ? [tags] : []).join(' ') + '">').text(text)));
    return Passages.loadMetadata(metadataSection());
  };
  const runPassage = (text, name, tags) => {
    name = name || 'test';
    createPassage(text, name, tags);
    Engine.goToPassage(name);
    return $('tw-passage:last-of-type > tw-transition-container, tw-passage > tw-hook');
  };
  const clearState = () => { State.reset(); Passages.clear(); };

  // The reference suite's global beforeEach. The debug flag is off outside the suites that set it.
  window.__H.Utils.options.debug = false;
  $('tw-story').empty();
  clearState();
  localStorage.clear();

  const results = [];
  for (const step of test.steps) {
    try {
      switch (step.op) {
        case 'option': window.__H.Utils.options[step.name] = step.value; break;
        case 'reset': clearState(); break;
        case 'create': createPassage(step.source, step.name, step.tags); break;
        case 'delete': Passages.delete(step.name); break;
        case 'run': runPassage(step.source, step.name, step.tags); break;
        case 'print':
        case 'notPrint': {
          const actual = runPassage(step.source).find('br').replaceWith(document.createTextNode('\n')).end().find('script').remove().end().text() || '';
          const e = step.expected;
          const hit = 'regex' in e ? new RegExp(e.regex, e.flags).exec(actual) !== null : actual === e.text;
          results.push({ op: step.op, source: step.source, passed: step.op === 'print' ? hit : !hit, actual: actual.slice(0, 160), expected: 'regex' in e ? `/${e.regex}/${e.flags}` : e.text });
          break;
        }
        case 'error': {
          const p = runPassage(step.source);
          const passed = p.find('tw-error:not(.javascript)').length > 0 && p.find('tw-error.javascript').length === 0;
          results.push({ op: step.op, source: step.source, passed, actual: passed ? '' : (p.text() || '').slice(0, 160) });
          break;
        }
        case 'notError': {
          const p = runPassage(step.source);
          const err = p.find('tw-error');
          results.push({ op: step.op, source: step.source, passed: err.length === 0, actual: err.length ? (err.contents()[0]?.textContent || '').slice(0, 160) : '' });
          break;
        }
      }
    } catch (e) {
      if (['print', 'notPrint', 'error', 'notError'].includes(step.op)) results.push({ op: step.op, source: step.source, passed: false, thrown: String(e.message || e).slice(0, 160) });
      break; // A thrown error ends a Jasmine test.
    }
  }
  return results;
}

async function real() {
  const { handWritten, launch, loadFormat, openPage, pageHtml, settle } = await import('./lib/harness.mjs');
  const format = loadFormat(formatFile());
  const tests = JSON.parse(readFileSync(join(OUT, 'specs.json'), 'utf8')).filter(t => t.checks > 0);
  const html = pageHtml(format, handWritten(format, { Start: 'ready' }), '42');
  const browser = await launch();
  const open = async () => { const s = await openPage(browser, html); await settle(s.page); return s; };
  let session = await open();
  const started = Date.now();
  const seenKeys = new Map();
  const rows = [];
  let hung = 0;
  for (const test of tests) {
    let results;
    try {
      results = await Promise.race([
        session.page.evaluate(runSpecTest, { steps: test.steps }),
        new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), 8000)),
      ]);
    } catch (e) {
      hung++;
      results = test.steps.filter(s => ['print', 'notPrint', 'error', 'notError'].includes(s.op))
        .map(s => ({ op: s.op, source: s.source, passed: false, thrown: `run failed: ${String(e.message).slice(0, 80)}` }));
      await session.context.close().catch(() => {});
      session = await open();
    }
    for (const r of results) {
      const base = [test.file, test.title, r.op, r.source].join('\u0001');
      const n = seenKeys.get(base) ?? 0;
      seenKeys.set(base, n + 1);
      rows.push({ key: `${base}\u0001${n}`, file: test.file, title: test.title, ...r });
    }
  }
  await browser.close();
  writeFileSync(join(OUT, 'real.json'), JSON.stringify(rows));
  const passed = rows.filter(r => r.passed).length;
  console.log(`real Harlowe ${format.version}: ${tests.length} tests, ${rows.length} checks, ${passed} pass (${(100 * passed / rows.length).toFixed(1)}%), ${rows.filter(r => r.thrown).length} threw, ${hung} tests hung or crashed, ${((Date.now() - started) / 1000).toFixed(1)} s`);
}

const command = process.argv[2];
mkdirSync(OUT, { recursive: true });
if (command === 'extract') {
  const root = join(REPO, pins.reference.dir);
  const archival = join(root, '.hg_archival.txt');
  const node = existsSync(archival) ? /^node: (\w+)/m.exec(readFileSync(archival, 'utf8'))?.[1] : null;
  if (!node) { console.error(`no reference at ${pins.reference.dir}: see tools/README.md`); process.exit(2); }
  if (node !== pins.reference.node && !process.argv.includes('--any-node')) {
    console.error(`the reference is node ${node}, pinned ${pins.reference.node}: re-pin it in tools/pins.json, or pass --any-node to extract anyway`);
    process.exit(2);
  }
  writeFileSync(join(OUT, 'specs.json'), JSON.stringify(extract(join(root, 'test', 'spec'))));
} else if (command === 'real') {
  await real();
} else {
  console.error('usage: node specs.mjs extract | real');
  process.exit(2);
}
