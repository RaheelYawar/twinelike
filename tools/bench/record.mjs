// Records what real Harlowe 3.3.9 shows, for the probe's output to be compared with.
//
// usage: node record.mjs <kind> <cases.json> <out.json>
//        node record.mjs walks [story-id,…]
//
// Kinds, each the shape the matching probe command writes:
//   markup        each snippet's page as text, formatting as ⟦b:…⟧ and the like, a list as ⟦list:…⟧
//   lists         the same, keeping list structure as ⟦ul:⟦li:…⟧⟧
//   errors        plain text, keeping each error's message as ⟦error: …⟧
//   clicks        { source, clicks: [linkIndex, …] }: the page after each click
//   nav           { passages: { name: [tags, text] }, go: [name | "<undo>", …] }: the page after each
//   live          { source, steps: [ms, …], clicks?, holds? }: the page after each stretch of time
//   live-errors   the same, keeping each error's message
//   walks         seeded random walks of the pinned stories that have "walks", into .cache/walks
//
// Each case renders as passage "test". Snippets also get a passage "b" = B, and live
// cases "b" = B(print: time) and "c" = (live: 100ms)[c(stop:)]. Run fetch.mjs first.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { handWritten, launch, loadFormat, openPage, pageHtml, recordWalk, replay, settle, storyDataOf, tweePassages } from './lib/harness.mjs';
import { CACHE } from './lib/paths.mjs';
import { formatFile, pins, storyFile } from './lib/pins.mjs';

const [kind, casesFile, outFile] = process.argv.slice(2);
const format = loadFormat(formatFile());
const readJson = f => JSON.parse(readFileSync(f, 'utf8').replace(/^\uFEFF/, ''));
const SEED = '42';
const blank = () => pageHtml(format, handWritten(format, { Start: 'ready' }), SEED);

// ----- snippets -----

// Runs in the page.
function renderBatch({ sources, style }) {
  const { $, State, Engine, Passages } = window.__H;
  const marked = node => {
    let out = '';
    for (const c of node.childNodes) {
      if (c.nodeType === 3) { out += c.data; continue; }
      if (c.nodeType !== 1) continue;
      const t = c.tagName;
      if (t === 'TW-SIDEBAR' || t === 'SCRIPT' || t === 'STYLE') continue;
      if (t === 'TW-ERROR') { out += '⟦error⟧'; continue; }
      if (t === 'BR' || t === 'TW-CONSECUTIVE-BR') { out += '\n'; continue; }
      if (t === 'HR') { out += '⟦hr⟧'; continue; }
      // Twinelike prints a broken link's label plus an error, on purpose.
      if (t === 'TW-BROKEN-LINK') { out += `${c.textContent}⟦error⟧`; continue; }
      const inner = marked(c);
      if (t === 'EM' || t === 'I') out += `⟦i:${inner}⟧`;
      else if (t === 'STRONG' || t === 'B') out += `⟦b:${inner}⟧`;
      else if (t === 'S') out += `⟦s:${inner}⟧`;
      else if (t === 'SUP') out += `⟦sup:${inner}⟧`;
      else if (t === 'TW-LINK') out += `⟦link:${inner}⟧`;
      else if (style === 'lists' && t === 'UL') out += `⟦ul:${inner}⟧`;
      else if (style === 'lists' && t === 'OL') out += `⟦ol:${inner}⟧`;
      else if (style === 'lists' && t === 'LI') out += `⟦li:${inner}⟧`;
      else if (style !== 'lists' && (t === 'UL' || t === 'OL')) out += `⟦list:${inner}⟧`;
      else out += inner;
    }
    return out;
  };
  const plain = node => {
    let out = '';
    for (const c of node.childNodes) {
      if (c.nodeType === 3) { out += c.data; continue; }
      if (c.nodeType !== 1) continue;
      const t = c.tagName;
      if (t === 'TW-SIDEBAR' || t === 'SCRIPT' || t === 'STYLE') continue;
      if (t === 'TW-ERROR') { out += `⟦error: ${c.textContent.slice(0, 200)}⟧`; continue; }
      if (t === 'TW-DIALOG') continue;
      // As in marked(): Twinelike prints a broken link's label plus an error.
      if (t === 'TW-BROKEN-LINK') { out += `${c.textContent}⟦error⟧`; continue; }
      if (t === 'BR' || t === 'TW-CONSECUTIVE-BR') { out += '\n'; continue; }
      out += plain(c);
    }
    return out;
  };
  const put = (name, text) => Passages.set(name, Passages.create($(`<div name="${name}" tags="">`).text(text)));
  return sources.map(source => {
    try {
      $('tw-story').empty();
      State.reset();
      Passages.clear();
      localStorage.clear();
      put('b', 'B');
      put('test', source);
      Engine.goToPassage('test');
      const all = document.querySelectorAll('tw-story > tw-passage');
      const last = all[all.length - 1];
      return style === 'errors' ? plain(last) : marked(last);
    } catch (e) {
      return `⟦threw:${String(e.message || e).slice(0, style === 'errors' ? 120 : 60)}⟧`;
    }
  });
}

async function snippets(style) {
  const cases = readJson(casesFile);
  const browser = await launch();
  const open = async () => { const s = await openPage(browser, blank()); await settle(s.page); return s; };
  let session = await open();
  const out = [];
  const size = 200;
  const within = (p, ms) => Promise.race([p, new Promise((_, reject) => setTimeout(() => reject(new Error('timeout')), ms))]);
  for (let i = 0; i < cases.length; i += size) {
    const batch = cases.slice(i, i + size);
    try {
      out.push(...await within(session.page.evaluate(renderBatch, { sources: batch, style }), 20000));
    } catch {
      // One case can hang the page: start again and run the batch one case at a time.
      await session.context.close().catch(() => {});
      session = await open();
      for (const one of batch) {
        try {
          out.push(...await within(session.page.evaluate(renderBatch, { sources: [one], style }), 3000));
        } catch {
          out.push('⟦hung⟧');
          await session.context.close().catch(() => {});
          session = await open();
        }
      }
    }
  }
  await browser.close();
  return out;
}

// ----- step cases -----

async function clicks() {
  const cases = readJson(casesFile);
  const browser = await launch();
  const { page } = await openPage(browser, blank());
  await settle(page);
  const out = [];
  for (const c of cases) {
    out.push(await page.evaluate(async ({ source, clicks }) => {
      const { $, State, Engine, Passages } = window.__H;
      const put = (name, text) => Passages.set(name, Passages.create($(`<div name="${name}" tags="">`).text(text)));
      $('tw-story').empty();
      State.reset();
      Passages.clear();
      put('test', source);
      Engine.goToPassage('test');
      const frame = () => new Promise(r => requestAnimationFrame(() => setTimeout(r, 0)));
      const passage = () => { const all = document.querySelectorAll('tw-story > tw-passage'); return all[all.length - 1]; };
      const text = () => {
        const clone = passage().cloneNode(true);
        clone.querySelectorAll('tw-sidebar,script,style').forEach(e => e.remove());
        clone.querySelectorAll('tw-error').forEach(e => e.replaceWith('⟦error⟧'));
        return clone.textContent;
      };
      await frame();
      const log = [text()];
      for (const n of clicks ?? []) {
        const links = passage().querySelectorAll('tw-link, .enchantment-link');
        if (!links[n]) { log.push('(no such link)'); continue; }
        links[n].click();
        await frame();
        log.push(text());
      }
      return log;
    }, c));
  }
  await browser.close();
  return out;
}

async function nav() {
  const cases = readJson(casesFile);
  const browser = await launch();
  const { page } = await openPage(browser, blank());
  await settle(page);
  const out = [];
  for (const c of cases) {
    out.push(await page.evaluate(async ({ passages, go }) => {
      const { $, State, Engine, Passages } = window.__H;
      $('tw-story').empty();
      State.reset();
      Passages.clear();
      for (const [name, [tags, text]] of Object.entries(passages))
        Passages.set(name, Passages.create($(`<div name="${name}" tags="${tags}">`).text(text)));
      const frame = () => new Promise(r => requestAnimationFrame(() => setTimeout(r, 0)));
      const text = () => {
        const all = document.querySelectorAll('tw-story > tw-passage');
        const clone = all[all.length - 1].cloneNode(true);
        clone.querySelectorAll('tw-sidebar,script,style').forEach(e => e.remove());
        clone.querySelectorAll('tw-error').forEach(e => e.replaceWith('⟦error⟧'));
        return clone.textContent;
      };
      const log = [];
      for (const name of go) {
        if (name === '<undo>') Engine.goBack(); else Engine.goToPassage(name);
        await frame();
        await frame();
        log.push(text());
      }
      return log;
    }, c));
  }
  await browser.close();
  return out;
}

async function live(keepErrors) {
  const cases = readJson(casesFile);
  const browser = await launch();
  const { page } = await openPage(browser, blank());
  await settle(page);
  const out = [];
  let now = 1_000_000;
  for (const c of cases) {
    now += 100_000;
    await page.clock.pauseAt(now);
    await page.evaluate(({ source }) => {
      const { $, State, Engine, Passages } = window.__H;
      const put = (name, text) => Passages.set(name, Passages.create($(`<div name="${name}" tags="">`).text(text)));
      $('tw-story').empty();
      State.reset();
      Passages.clear();
      put('test', source);
      put('b', 'B(print: time)');
      put('c', '(live: 100ms)[c(stop:)]');
      Engine.goToPassage('test');
    }, c);
    const text = () => page.evaluate(keep => {
      const all = document.querySelectorAll('tw-story > tw-passage');
      const clone = all[all.length - 1].cloneNode(true);
      clone.querySelectorAll('tw-sidebar,script,style').forEach(e => e.remove());
      clone.querySelectorAll('tw-error').forEach(e => e.replaceWith(keep ? `⟦error: ${e.textContent.slice(0, 160)}⟧` : '⟦error⟧'));
      return clone.textContent;
    }, keepErrors);
    const log = [await text()];
    for (let i = 0; i < c.steps.length; i++) {
      const click = c.clicks && c.clicks[i];
      if (click !== undefined) {
        const clicked = await page.evaluate(n => {
          const all = document.querySelectorAll('tw-story > tw-passage');
          const links = all[all.length - 1].querySelectorAll('tw-link, .enchantment-link');
          if (!links[n]) return false;
          links[n].click();
          return true;
        }, click);
        if (!clicked) log.push('(no such link)');
      }
      // A held key is what (after:)'s second number reads.
      const hold = c.holds && c.holds[i];
      if (hold) await page.keyboard.down('a');
      await page.clock.runFor(c.steps[i]);
      if (hold) await page.keyboard.up('a');
      log.push(await text());
    }
    out.push(log);
  }
  await browser.close();
  return out;
}

// ----- walks -----

// Two hand-written pages: one of each kind of choice, and fifty seeded draws.
const SYNTHETIC = [
  {
    id: 'kinds', start: 'Start',
    passages: {
      Start: 'Before [[Go->Next]] mid (link: "Reveal")[shown] then |c>[click me](click: ?c)[clicked] and |h>[hover me](mouseover: ?h)[hovered] and [[Broken->Nowhere]] end.',
      Next: 'arrived',
    },
    steps: [
      { do: 'start' },
      { do: 'choose', label: 'Reveal', event: 'click', nth: 0 },
      { do: 'choose', label: 'click me', event: 'click', nth: 0 },
      { do: 'choose', label: 'hover me', event: 'mouseover', nth: 0 },
      { do: 'choose', label: 'Go', event: 'click', nth: 0 },
    ],
  },
  { id: 'draws', start: 'Start', passages: { Start: Array(50).fill('(print: (random: 1, 1000000))').join(' ') }, steps: [{ do: 'start' }] },
];

async function walks(only) {
  const dir = join(CACHE, 'walks');
  mkdirSync(dir, { recursive: true });
  const browser = await launch();
  console.log(`browser ${browser.version()}, Harlowe ${format.version}`);
  if (!only || only.includes('synthetic')) {
    const cases = [], real = {};
    for (const s of SYNTHETIC) {
      const run = await replay(browser, pageHtml(format, handWritten(format, s.passages, s.start), SEED), s.steps);
      cases.push({ id: s.id, passages: s.passages, start: s.start, seed: 42, steps: s.steps });
      real[s.id] = run.transcript;
    }
    writeFileSync(join(dir, 'synthetic.cases.json'), JSON.stringify({ cases }));
    writeFileSync(join(dir, 'synthetic.real.json'), JSON.stringify(real));
    console.log('synthetic: kinds and draws recorded');
  }
  for (const s of pins.stories) {
    if (!s.walks || (only ? !only.includes(s.id) : !s.default)) continue;
    const file = storyFile(s);
    let storyData, playFile = file;
    if (file.endsWith('.twee')) {
      // Built the way Twine would, tags kept, so both engines play the same HTML.
      const { passages, start } = tweePassages(readFileSync(file, 'utf8'));
      storyData = handWritten(format, passages, start, s.id, '22222222-2222-4222-8222-222222222222');
      playFile = join(dir, `${s.id}.story.html`);
      writeFileSync(playFile, `<!DOCTYPE html><html><body>${storyData}</body></html>`);
    } else storyData = storyDataOf(readFileSync(file, 'utf8'));
    const html = pageHtml(format, storyData, SEED);
    const cases = [], real = {};
    let identical = 0, steps = 0, pageErrors = 0;
    for (let w = 0; w < s.walks.count; w++) {
      const rec = await recordWalk(browser, html, s.walks.seed + w, s.walks.steps);
      // Played again from a clean page, a walk must come out the same, or the bench isn't deterministic.
      const again = await replay(browser, html, rec.steps);
      const strip = t => t.map(x => (x.missingChoice ? x : { passage: x.passage, turns: x.turns, errors: x.errors, segments: x.segments }));
      if (JSON.stringify(strip(rec.transcript)) === JSON.stringify(strip(again.transcript))) identical++;
      const id = `${s.id}-walk-${w + 1}`;
      cases.push({ id, file: relative(dir, playFile).split('\\').join('/'), seed: 42, steps: rec.steps });
      real[id] = rec.transcript;
      steps += rec.transcript.length;
      pageErrors += rec.pageErrors.length;
    }
    writeFileSync(join(dir, `${s.id}.cases.json`), JSON.stringify({ cases }));
    writeFileSync(join(dir, `${s.id}.real.json`), JSON.stringify(real));
    console.log(`${s.id}: ${s.walks.count} walks, ${steps} steps, ${identical} identical when played again, ${pageErrors} page errors`);
  }
  await browser.close();
}

const kinds = {
  markup: () => snippets('markup'), lists: () => snippets('lists'), errors: () => snippets('errors'),
  clicks, nav, live: () => live(false), 'live-errors': () => live(true),
};
if (kind === 'walks') await walks(casesFile ? casesFile.split(',') : null);
else if (kinds[kind] && casesFile && outFile) {
  const started = Date.now();
  const out = await kinds[kind]();
  writeFileSync(outFile, JSON.stringify(out) + '\n');
  console.log(`real Harlowe ${format.version}: ${out.length} cases in ${((Date.now() - started) / 1000).toFixed(1)} s`);
} else {
  console.error(`usage: node record.mjs <kind> <cases.json> <out.json>
       node record.mjs walks [story-id,…]

  markup, lists, errors   cases: ["snippet", …]
  clicks                  cases: [{ "source": "…", "clicks": [linkIndex, …] }, …]
  nav                     cases: [{ "passages": { "name": ["tags", "text"] }, "go": ["name" | "<undo>", …] }, …]
  live, live-errors       cases: [{ "source": "…", "steps": [ms, …], "clicks": { "step": linkIndex }, "holds": { "step": true } }, …]`);
  process.exit(2);
}
