// Replays the walks record.mjs recorded on real Harlowe and compares them step by step.
import { existsSync, readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';
import { CACHE } from './paths.mjs';
import { ratchet } from './ratchet.mjs';

// Harlowe draws a broken link with no error. We print its label plus an error.
function normal(s) {
  let errors = s.errors;
  const segments = [];
  const add = x => {
    const last = segments[segments.length - 1];
    if (x[0] === 'text' && last?.[0] === 'text') last[1] += x[1]; else segments.push([...x]);
  };
  for (const x of s.segments) {
    if (x[0] === 'broken-link') { errors++; add(['text', x[1]]); } else add(x);
  }
  return { passage: s.passage, errors, segments };
}
const squash = segs => JSON.stringify(segs.map(x => (x[0] === 'text' ? ['text', x[1].replace(/\s+/g, ' ').trim()] : [x[0], x[1].replace(/\s+/g, ' ').trim(), x[2]])).filter(x => x[0] !== 'text' || x[1]));

function classify(h, t) {
  if (t.missingChoice) return 'choice missing';
  if (t.exception) return 'exception';
  if (h.passage !== t.passage) return 'wrong passage';
  if (JSON.stringify(h.segments) === JSON.stringify(t.segments)) return h.errors === t.errors ? 'match' : 'error count';
  if (squash(h.segments) === squash(t.segments)) return h.errors === t.errors ? 'whitespace only' : 'error count';
  if (t.errors > h.errors) return 'our error';
  return 'different content';
}

function firstDiff(a, b) {
  const x = JSON.stringify(a), y = JSON.stringify(b);
  let i = 0;
  while (i < x.length && x[i] === y[i]) i++;
  return { real: x.slice(Math.max(0, i - 25), i + 55), ours: y.slice(Math.max(0, i - 25), i + 55) };
}

/**
 * Per walk set: steps compared, by kind of difference, and which passages always
 * match. A difference in prose leaves a walk valid; only a lost choice ends it.
 */
export async function verifyWalks(probe, counts, expected) {
  const dir = join(CACHE, 'walks');
  const sets = existsSync(dir) ? readdirSync(dir).filter(f => f.endsWith('.cases.json')).map(f => f.slice(0, -'.cases.json'.length)) : [];
  if (!sets.length) { console.log('no walks recorded: run "node record.mjs walks" first'); return true; }
  let ok = true;
  for (const set of sets) {
    const out = join(CACHE, 'ours', 'walks', `${set}.json`);
    probe('walks', join(dir, `${set}.cases.json`), out);
    const H = JSON.parse(readFileSync(join(dir, `${set}.real.json`), 'utf8'));
    const T = JSON.parse(readFileSync(out, 'utf8'));
    const kinds = {}, matched = new Set(), differed = new Set(), shown = new Set();
    let steps = 0, matchedSteps = 0;
    const firsts = [];
    let missing = 0;
    for (const id of Object.keys(H)) if (!T[id]) missing++;
    for (const id of Object.keys(T)) {
      const hs = H[id], ts = T[id];
      if (!hs) { missing++; continue; }
      for (let i = 0; i < hs.length && i < ts.length; i++) {
        const h = normal(hs[i]);
        const t = ts[i].segments ? normal(ts[i]) : ts[i];
        const kind = classify(h, t);
        steps++;
        kinds[kind] = (kinds[kind] || 0) + 1;
        if (kind === 'match') { matchedSteps++; matched.add(h.passage); continue; }
        differed.add(h.passage);
        if (!shown.has(h.passage) && firsts.length < 3) {
          shown.add(h.passage);
          firsts.push(`   ${id} step ${i}, passage "${h.passage}": ${kind}${t.segments ? `\n      real …${firstDiff(h.segments, t.segments).real}…\n      ours …${firstDiff(h.segments, t.segments).ours}…` : ''}`);
        }
        if (!t.segments) break;
      }
    }
    const always = [...matched].filter(p => !differed.has(p)).length;
    const verdict = ratchet(`walks:${set}`, matchedSteps, steps, counts, expected);
    if (verdict.failed || missing) ok = false;
    console.log(`${set.padEnd(20)} ${matchedSteps} of ${steps} steps match; passages always matching ${always}, with a difference ${differed.size}${verdict.note}${missing ? `  ${missing} walks recorded on one side only: record them again` : ''}`);
    const other = Object.entries(kinds).filter(([k]) => k !== 'match');
    if (other.length) console.log(`   ${other.map(([k, v]) => `${k} ${v}`).join(', ')}`);
    // Story text stays out of the log for stories that ask for it.
    if (!/^egg-exe/.test(set)) for (const f of firsts) console.log(f);
  }
  return ok;
}
