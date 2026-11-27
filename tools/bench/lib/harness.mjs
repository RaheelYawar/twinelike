// Drives real Harlowe 3.3.9 in headless Chromium. Stories run on a virtual clock
// with outside network access blocked, and nothing in format.js or a story's
// passages changes: one story script the harness adds exposes Harlowe's State and
// friends, and seeds the random number generator.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import vm from 'node:vm';
import { chromium } from 'playwright-core';

export const ORIGIN = 'http://bench.invalid/';

export function loadFormat(file) {
  let format;
  vm.runInNewContext(readFileSync(file, 'utf8'), { window: { storyFormat: f => { format = f; } } });
  return format;
}

const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');

/**
 * A <tw-storydata> for passages written here: { name: body }, or [[name, body], …] when
 * order matters (an object puts number-like names first). A body can be { body, tags }.
 */
export function handWritten(format, passages, start = 'Start', name = 'Bench', ifid = '11111111-1111-4111-8111-111111111111') {
  let pid = 1, startPid = 1;
  const entries = Array.isArray(passages) ? passages : Object.entries(passages);
  const data = entries.map(([passage, value]) => {
    const id = pid++;
    if (passage === start) startPid = id;
    const body = typeof value === 'string' ? value : value.body;
    const tags = typeof value === 'string' ? '' : value.tags ?? '';
    return `<tw-passagedata pid="${id}" name="${esc(passage)}" tags="${esc(tags)}" position="0,0" size="100,100">${esc(body)}</tw-passagedata>`;
  }).join('');
  return `<tw-storydata name="${esc(name)}" startnode="${startPid}" creator="bench" creator-version="1" ifid="${ifid}" zoom="1" format="Harlowe" format-version="${format.version}" options="" hidden>`
    + `<style role="stylesheet" id="twine-user-stylesheet" type="text/twine-css"></style><script role="script" id="twine-user-script" type="text/twine-javascript"></script>${data}</tw-storydata>`;
}

export function storyDataOf(exportHtml) {
  const start = exportHtml.indexOf('<tw-storydata');
  const end = exportHtml.indexOf('</tw-storydata>', start);
  if (start < 0 || end < 0) throw new Error('no tw-storydata in export');
  return exportHtml.slice(start, end + '</tw-storydata>'.length);
}

/** Twee 3 to passages, keeping tags and leaving out StoryTitle, StoryData and script or stylesheet passages. */
export function tweePassages(twee) {
  const passages = [];
  let storyData = {}, current = null;
  for (const line of twee.replace(/\r\n/g, '\n').split('\n')) {
    const header = /^::\s*(.*?)\s*(?:\[(.*?)\])?\s*(\{.*\})?\s*$/.exec(line);
    if (header && line.startsWith('::')) {
      current = { name: header[1], tags: (header[2] || '').trim(), body: [] };
      passages.push(current);
    } else if (current) current.body.push(line);
  }
  const kept = [];
  for (const p of passages) {
    const body = p.body.join('\n').replace(/\n+$/, '');
    if (p.name === 'StoryData') storyData = JSON.parse(body);
    else if (p.name !== 'StoryTitle' && !/\b(stylesheet|script)\b/.test(p.tags)) kept.push([p.name, { body, tags: p.tags }]);
  }
  return { passages: kept, start: storyData.start };
}

export function pageHtml(format, storyData, seed) {
  const harness = `window.__H={State:State,Engine:Engine,Passages:Passages,Section:Section,Utils:Utils,$:$,VarScope:VarScope};State.setSeed(${JSON.stringify(seed)});`;
  const data = storyData
    .replace(/(<tw-storydata\b[^>]*?)\boptions="[^"]*"/, (_, head) => `${head}options=""`)
    .replace(/<tw-storydata\b[^>]*>/, m => `${m}<script role="script" type="text/twine-javascript">${harness}</script>`);
  return format.source.replace(/\{\{STORY_NAME\}\}/g, 'Bench').replace('{{STORY_DATA}}', () => data);
}

// Runs in the page. Reads what the player sees, in document order.
function installHelpers() {
  const EVENTS = [
    ['enchantment-link', 'click'], ['enchantment-clickblock', 'click'],
    ['enchantment-mouseover', 'mouseover'], ['enchantment-mouseoverblock', 'mouseover'],
    ['enchantment-mouseout', 'mouseout'], ['enchantment-mouseoutblock', 'mouseout'],
  ];
  const CHOICE_SELECTOR = 'tw-link,' + EVENTS.map(e => '.' + e[0]).join(',');
  const SKIP = new Set(['SCRIPT', 'STYLE', 'TW-SIDEBAR', 'TW-DIALOG', 'TW-BACKDROP']);

  const eventOf = el => {
    for (const [cls, ev] of EVENTS) if (el.classList.contains(cls)) return ev;
    return el.tagName === 'TW-LINK' ? 'click' : null;
  };
  const labelOf = el => {
    const c = el.cloneNode(true);
    c.querySelectorAll('script,style,tw-error').forEach(x => x.remove());
    c.querySelectorAll('br,tw-consecutive-br').forEach(b => b.replaceWith('\n'));
    return c.textContent;
  };
  function collect() {
    const all = document.querySelectorAll('tw-story > tw-passage');
    const root = all[all.length - 1];
    const segments = [], elements = [];
    let errors = 0, nested = 0;
    const text = s => {
      if (!s) return;
      const last = segments[segments.length - 1];
      if (last && last[0] === 'text') last[1] += s; else segments.push(['text', s]);
    };
    const visit = node => {
      for (const child of node.childNodes) {
        if (child.nodeType === 3) { text(child.data); continue; }
        if (child.nodeType !== 1 || SKIP.has(child.tagName)) continue;
        if (child.tagName === 'TW-ERROR') { errors++; continue; }
        if (child.tagName === 'BR' || child.tagName === 'TW-CONSECUTIVE-BR') { text('\n'); continue; }
        if (child.tagName === 'TW-BROKEN-LINK') { segments.push(['broken-link', child.textContent]); continue; }
        const ev = eventOf(child);
        if (ev) {
          if (child.querySelector(CHOICE_SELECTOR)) nested++;
          segments.push(['choice', labelOf(child), ev]);
          elements.push(child);
          continue;
        }
        visit(child);
      }
    };
    if (root) visit(root);
    // A (click: ?page) wraps the passage in its enchantment: the whole passage is one choice then.
    // Harlowe only fires it for an event from inside the passage, so the passage is what gets clicked.
    for (let up = root && root.parentElement; up; up = up.parentElement) {
      const ev = eventOf(up);
      if (!ev) continue;
      const whole = segments.filter(s => s[0] === 'text' || s[0] === 'choice').map(s => s[1]).join('');
      return { segments: [['choice', whole, ev]], elements: [root], errors, nested: nested + elements.length };
    }
    return { segments, elements, errors, nested };
  }
  window.__benchSnapshot = () => {
    const H = window.__H, c = collect();
    return {
      passage: H ? H.State.passage : null, turns: H ? H.State.turns : null,
      past: H ? H.State.pastLength : null, future: H ? H.State.futureLength : null,
      errors: c.errors, nested: c.nested, segments: c.segments,
    };
  };
  window.__benchFire = (label, nth, event) => {
    const c = collect();
    let seen = 0, at = 0;
    for (const s of c.segments) {
      if (s[0] !== 'choice') continue;
      const el = c.elements[at++];
      if (s[1] !== label || s[2] !== event || seen++ !== nth) continue;
      if (event === 'click') el.click();
      else {
        const init = { bubbles: true, cancelable: true, relatedTarget: document.body };
        el.dispatchEvent(new MouseEvent('mouseover', init));
        if (event === 'mouseout') el.dispatchEvent(new MouseEvent('mouseout', init));
      }
      return true;
    }
    return false;
  };
}

/**
 * The browser: BENCH_BROWSER if set, else the one this playwright-core expects,
 * else the newest headless shell or Chromium Playwright has installed.
 */
function browserPath() {
  if (process.env.BENCH_BROWSER) return process.env.BENCH_BROWSER;
  const expected = chromium.executablePath();
  if (existsSync(expected)) return expected;
  const root = process.env.PLAYWRIGHT_BROWSERS_PATH
    || (process.platform === 'win32' ? join(process.env.LOCALAPPDATA ?? '', 'ms-playwright')
      : process.platform === 'darwin' ? join(homedir(), 'Library', 'Caches', 'ms-playwright')
        : join(homedir(), '.cache', 'ms-playwright'));
  const candidates = existsSync(root) ? readdirSync(root).filter(n => /^chromium(_headless_shell)?-\d+$/.test(n)) : [];
  candidates.sort((a, b) => Number(b.split('-').pop()) - Number(a.split('-').pop()) || (a.includes('headless') ? -1 : 1));
  const exe = {
    win32: ['chrome-headless-shell-win64/chrome-headless-shell.exe', 'chrome-win64/chrome.exe', 'chrome-win/chrome.exe'],
    darwin: ['chrome-headless-shell-mac-arm64/chrome-headless-shell', 'chrome-headless-shell-mac-x64/chrome-headless-shell', 'chrome-mac/Chromium.app/Contents/MacOS/Chromium'],
    linux: ['chrome-headless-shell-linux64/chrome-headless-shell', 'chrome-linux/chrome'],
  }[process.platform] ?? [];
  for (const dir of candidates)
    for (const e of exe) if (existsSync(join(root, dir, e))) return join(root, dir, e);
  throw new Error(`no browser: install one with "npx playwright-core install chromium-headless-shell", or set BENCH_BROWSER`);
}

export async function launch() {
  // The route in openPage blocks page requests. These also stop anything the route can't see.
  const args = ['--host-resolver-rules=MAP * ~NOTFOUND', '--proxy-server=http://127.0.0.1:9', '--proxy-bypass-list=<-loopback>'];
  return chromium.launch({ headless: true, executablePath: browserPath(), args });
}

export async function openPage(browser, html) {
  const context = await browser.newContext({ serviceWorkers: 'block', viewport: { width: 1000, height: 800 } });
  await context.route('**/*', route => (route.request().url() === ORIGIN
    ? route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: html })
    : route.abort()));
  const page = await context.newPage();
  const pageErrors = [];
  page.on('pageerror', e => pageErrors.push(String(e.message || e)));
  await page.clock.install({ time: 0 });
  await page.addInitScript(installHelpers);
  await page.goto(ORIGIN, { waitUntil: 'load' });
  return { context, page, pageErrors };
}

// Advances the virtual clock until the passage, turn count and page stop changing.
export async function settle(page, maxCycles = 30) {
  let last = '', same = 0, snap;
  for (let i = 0; i < maxCycles; i++) {
    await page.clock.runFor(48);
    snap = await page.evaluate(() => window.__benchSnapshot());
    const key = JSON.stringify(snap);
    same = key === last ? same + 1 : 0;
    last = key;
    if (same >= 3 && snap.passage) return { ...snap, settled: true };
  }
  return { ...snap, settled: false };
}

export function choicesOf(snapshot) {
  const seen = new Map(), out = [];
  for (const s of snapshot.segments) {
    if (s[0] !== 'choice') continue;
    const key = s[2] + '\u0001' + s[1];
    const nth = seen.get(key) ?? 0;
    seen.set(key, nth + 1);
    out.push({ label: s[1], event: s[2], nth });
  }
  return out;
}

async function perform(page, step) {
  if (step.do === 'choose') return page.evaluate(c => window.__benchFire(c.label, c.nth, c.event), step);
  if (step.do === 'undo') { await page.evaluate(() => window.__H.Engine.goBack()); return true; }
  if (step.do === 'redo') { await page.evaluate(() => window.__H.Engine.goForward()); return true; }
  return true;
}

export async function replay(browser, html, steps) {
  const { context, page, pageErrors } = await openPage(browser, html);
  const transcript = [];
  let snap = await settle(page);
  for (const step of steps) {
    if (step.do === 'wait') {
      for (let i = 0; i < step.cycles; i++) { await page.clock.runFor(1000); snap = await settle(page); }
    } else if (step.do !== 'start') {
      if (!(await perform(page, step))) { transcript.push({ missingChoice: step }); break; }
      snap = await settle(page);
    }
    transcript.push(snap);
  }
  await context.close();
  return { transcript, pageErrors };
}

function walkRng(seed) {
  let h = seed >>> 0;
  return () => {
    h = (h + 0x6D2B79F5) | 0;
    let t = Math.imul(h ^ (h >>> 15), 1 | h);
    t ^= t + Math.imul(t ^ (t >>> 7), 61 | t);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// Plays a seeded random walk on real Harlowe, so the scenario comes from the reference engine.
export async function recordWalk(browser, html, walkSeed, maxSteps) {
  const { context, page, pageErrors } = await openPage(browser, html);
  const rng = walkRng(walkSeed);
  const steps = [{ do: 'start' }], transcript = [];
  let snap = await settle(page);
  transcript.push(snap);
  for (let i = 0; i < maxSteps; i++) {
    let choices = choicesOf(snap);
    if (!choices.length) {
      // Timed macros can reveal a choice later, so let virtual time pass before ending the walk.
      let cycles = 0;
      while (!choices.length && cycles < 40) {
        await page.clock.runFor(1000);
        cycles++;
        snap = await settle(page);
        choices = choicesOf(snap);
      }
      if (!choices.length) break;
      steps.push({ do: 'wait', cycles });
      transcript.push(snap);
    }
    const c = choices[Math.floor(rng() * choices.length)];
    const step = { do: 'choose', ...c };
    if (!(await perform(page, step))) break;
    snap = await settle(page);
    steps.push(step);
    transcript.push(snap);
  }
  await context.close();
  return { steps, transcript, pageErrors };
}
