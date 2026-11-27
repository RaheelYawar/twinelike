// Runs the extracted reference spec checks here under both profiles, and compares
// with real Harlowe 3.3.9's results and with the last run.
import { copyFileSync, existsSync, mkdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { CACHE } from './paths.mjs';
import { ratchet } from './ratchet.mjs';

const load = f => new Map(readFileSync(f, 'utf8').split('\u0003').filter(Boolean).map(r => {
  const [key, passed, actual, error] = r.split('\u0002');
  return [key, { passed: passed === '1', actual, error }];
}));

export async function verifySpecs(probe, counts, expected) {
  const dir = join(CACHE, 'specs');
  const specs = join(dir, 'specs.json');
  if (!existsSync(specs)) { console.log('no specs extracted: run "node specs.mjs extract" first'); return true; }
  const realFile = join(dir, 'real.json');
  const real = existsSync(realFile) ? new Map(JSON.parse(readFileSync(realFile, 'utf8')).map(r => [r.key, r.passed])) : null;
  const lastDir = join(CACHE, 'last', 'specs');
  mkdirSync(lastDir, { recursive: true });
  let ok = true;
  for (const version of ['3.3.9', '4.0.0']) {
    const r = probe('specs', specs, version, dir, version);
    console.log(r.stdout.split('\n')[0]);
    const file = join(dir, `results-${version}.dat`), last = join(lastDir, `results-${version}.dat`);
    const ours = load(file);
    let passed = 0;
    for (const v of ours.values()) if (v.passed) passed++;
    const verdict = ratchet(`specs:${version}`, passed, ours.size, counts, expected);
    if (verdict.failed) ok = false;
    if (verdict.note) console.log(`  ${verdict.note}`);
    if (existsSync(last)) {
      const before = load(last);
      let gained = 0, lost = 0;
      const shown = [];
      for (const [key, b] of before) {
        const a = ours.get(key);
        if (!a) continue;
        if (!b.passed && a.passed) gained++;
        if (b.passed && !a.passed) { lost++; if (shown.length < 5) shown.push(key.split('\u0001').slice(0, 4).join(' :: ').replace(/\n/g, '⏎').slice(0, 160)); }
      }
      console.log(`   since the last run: ${gained} newly passing, ${lost} newly failing${shown.length ? '\n     ' + shown.join('\n     ') : ''}`);
    }
    copyFileSync(file, last);
    if (real && version === '3.3.9') {
      const cell = { both: 0, onlyReal: 0, onlyOurs: 0, neither: 0 };
      for (const [key, realPassed] of real) {
        const o = ours.get(key);
        if (!o) continue;
        if (realPassed && o.passed) cell.both++;
        else if (realPassed) cell.onlyReal++;
        else if (o.passed) cell.onlyOurs++;
        else cell.neither++;
      }
      let realPass = 0;
      for (const v of real.values()) if (v) realPass++;
      console.log(`   real 3.3.9 passes ${realPass} of ${real.size}; both pass ${cell.both}, only real ${cell.onlyReal}, only here ${cell.onlyOurs}, neither ${cell.neither}`);
    }
  }
  return ok;
}
