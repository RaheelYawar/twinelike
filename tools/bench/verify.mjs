// Runs Twinelike on every committed case set and compares it with what real Harlowe 3.3.9 showed.
//
// usage: node verify.mjs [--only suite,…] [--show N] [--fuzz] [--stack] [--walks] [--specs] [--all]
//                        [--update] [--no-build]
//
// Each suite prints how many cases match. A suite that matches or compares
// fewer than tools/cases/expected.json records fails the run; --update records
// the new counts instead. Each run's output is kept in .cache/last, so the next
// run also says how many results changed, matching or not.
import { spawnSync } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { changedBetween, compareSet, describeCase } from './lib/compare.mjs';
import { ratchet } from './lib/ratchet.mjs';
import { CACHE, CASES, FEATURE_COVERAGE, PROBE_DLL, PROBE_PROJECT } from './lib/paths.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(name) || (args.includes('--all') && ['--fuzz', '--stack', '--walks', '--specs'].includes(name));
const valueOf = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const only = valueOf('--only', null)?.split(',');
const show = Number(valueOf('--show', 3));

const readJson = f => JSON.parse(readFileSync(f, 'utf8').replace(/^﻿/, ''));
const section = title => console.log(`\n=== ${title} ===`);

function probe(...probeArgs) {
  const r = spawnSync('dotnet', [PROBE_DLL, ...probeArgs], { encoding: 'utf8', maxBuffer: 1 << 28 });
  if (r.status !== 0 && r.status !== 1) throw new Error(`probe ${probeArgs[0]} failed (${r.status}): ${r.stderr || r.stdout}`);
  return r;
}

if (!args.includes('--no-build')) {
  section('build');
  const r = spawnSync('dotnet', ['build', PROBE_PROJECT, '-c', 'Release', '-v', 'q', '-nologo'], { encoding: 'utf8' });
  if (r.status !== 0) { console.log(r.stdout, r.stderr); process.exit(2); }
  console.log('probe built');
}

const expectedFile = join(CASES, 'expected.json');
const expected = existsSync(expectedFile) ? readJson(expectedFile) : {};
const counts = {};
let failed = false;

section('case suites vs real Harlowe 3.3.9');
for (const suite of readJson(join(CASES, 'suites.json'))) {
  if (only && !only.includes(suite.suite)) continue;
  let compared = 0, matched = 0, skipped = 0, changed = 0;
  const lines = [];
  for (const set of suite.sets) {
    const casesFile = join(CASES, suite.suite, `${set}.cases.json`);
    const lastOnly = existsSync(join(CASES, suite.suite, `${set}.real-last.json`));
    const realFile = join(CASES, suite.suite, `${set}.real${lastOnly ? '-last' : ''}.json`);
    const outDir = join(CACHE, 'ours', suite.suite), lastDir = join(CACHE, 'last', suite.suite);
    mkdirSync(outDir, { recursive: true });
    mkdirSync(lastDir, { recursive: true });
    const outFile = join(outDir, `${set}.json`), lastFile = join(lastDir, `${set}.json`);
    probe(suite.probe, casesFile, outFile, ...(suite.style ? [suite.style] : []));
    const cases = readJson(casesFile), real = readJson(realFile), ours = readJson(outFile);
    const r = compareSet(cases, real, ours, { ...suite, lastOnly });
    compared += r.compared; matched += r.matched; skipped += r.skipped;
    const delta = existsSync(lastFile) ? changedBetween(readJson(lastFile), ours) : null;
    if (delta) changed += delta;
    copyFileSync(outFile, lastFile);
    for (const i of r.differ) {
      if (lines.length >= show) break;
      lines.push(`   ${set} #${i} ${JSON.stringify(describeCase(cases[i])).slice(0, 160)}\n      real ${JSON.stringify(real[i]).slice(0, 200)}\n      ours ${JSON.stringify(ours[i]).slice(0, 200)}`);
    }
  }
  const verdict = ratchet(suite.suite, matched, compared, counts, expected);
  if (verdict.failed) failed = true;
  const pct = compared ? ` (${(100 * matched / compared).toFixed(2)}%)` : '';
  console.log(`${suite.suite.padEnd(14)} ${matched} of ${compared} match${pct}${skipped ? `, ${skipped} not comparable` : ''}${changed ? `, ${changed} changed since the last run` : ''}${verdict.note}`);
  for (const l of lines) console.log(l);
}

if (flag('--fuzz')) {
  section('fuzz');
  const extra = [];
  for (const suite of readdirSync(CASES, { withFileTypes: true })) {
    if (!suite.isDirectory()) continue;
    for (const f of readdirSync(join(CASES, suite.name)))
      if ((f.endsWith('.cases.json') && !['snippets', 'nav'].includes(suite.name)) || (suite.name === 'fuzz' && f.endsWith('.json'))) extra.push(join(CASES, suite.name, f));
  }
  mkdirSync(join(CACHE, 'fuzz'), { recursive: true });
  for (const seed of [41, 20261124, 777, 90210]) {
    const report = join(CACHE, 'fuzz', `seed-${seed}.txt`);
    const r = probe('fuzz', FEATURE_COVERAGE, report, '200000', String(seed), ...extra);
    if (r.status !== 0) failed = true;
    console.log(`seed ${seed}: ${readFileSync(report, 'utf8').split('\n').slice(0, r.status === 0 ? 3 : 14).join('\n   ')}`);
  }
  section('the case that used to hang');
  const r = probe('timecase', join(CASES, 'fuzz', 'hang-case.txt'));
  if (r.status !== 0) failed = true;
  console.log(r.stdout.trim());
}

if (flag('--stack')) {
  section('deep recursion on small stacks');
  const shapes = probe('stack').stdout.split(/\r?\n/).filter(Boolean);
  for (const kb of [512, 256, 160]) {
    let ok = 0;
    const bad = [];
    for (const shape of shapes) {
      const r = spawnSync('dotnet', [PROBE_DLL, 'stack', shape, String(kb)], { encoding: 'utf8', timeout: 30000 });
      const out = (r.stdout || '').trim().split('\n').pop() ?? '';
      if (r.status === 0 && out.startsWith('ok')) ok++;
      else bad.push(`${shape}: ${r.error ? 'hung over 30 s' : `exit ${r.status}`} ${out.slice(0, 80)}`);
    }
    if (bad.length) failed = true;
    console.log(`${kb} KB: ${ok} of ${shapes.length} shapes finish with an in-prose error or none${bad.length ? '\n   ' + bad.join('\n   ') : ''}`);
  }
}

if (flag('--walks')) {
  section('story walks vs real Harlowe 3.3.9');
  const { verifyWalks } = await import('./lib/walks.mjs');
  if (!(await verifyWalks(probe, counts, expected))) failed = true;
}

if (flag('--specs')) {
  section('reference specs');
  const { verifySpecs } = await import('./lib/specs.mjs');
  if (!(await verifySpecs(probe, counts, expected))) failed = true;
}

if (args.includes('--update')) {
  const merged = { ...expected, ...counts };
  writeFileSync(expectedFile, JSON.stringify(merged, null, 2) + '\n');
  console.log(`\nrecorded the counts in ${expectedFile}`);
} else if (failed) {
  console.log('\nFAILED: a suite matches or compares fewer cases than recorded, or a fuzz, hang or stack check failed.');
}
process.exit(failed && !args.includes('--update') ? 1 : 0);
