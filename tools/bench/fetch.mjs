// Fetches the pinned third-party inputs into tools/.cache and checks each by SHA-256.
//
// usage: node fetch.mjs [--story id,…] [--all-stories] [--from <dir>]
//
// Harlowe's format.js and the default stories are fetched by default; a story
// marked "default": false only when named. --from looks in a local directory
// first, for a file with the pinned hash, so a machine that already has the
// files needs no network. The reference archive is never downloaded (its host
// blocks scripts); fetch only checks that the unpacked copy is the pinned node.
import { createHash } from 'node:crypto';
import { copyFileSync, existsSync, mkdirSync, readFileSync, readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { REPO } from './lib/paths.mjs';
import { formatFile, pins, storyFile } from './lib/pins.mjs';

const args = process.argv.slice(2);
const valueOf = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : null; };
const named = valueOf('--story')?.split(',') ?? [];
const from = valueOf('--from');

const sha256 = buf => createHash('sha256').update(buf).digest('hex');

let localIndex = null;
function findLocal(hash) {
  if (!from) return null;
  if (!localIndex) {
    localIndex = new Map();
    const walk = d => {
      for (const n of readdirSync(d)) {
        const p = join(d, n);
        const st = statSync(p);
        if (st.isDirectory()) { if (n !== 'node_modules' && n !== '.git') walk(p); }
        else if (st.size < 64 << 20) localIndex.set(sha256(readFileSync(p)), p);
      }
    };
    walk(from);
  }
  return localIndex.get(hash) ?? null;
}

async function get(target, url, hash) {
  if (existsSync(target) && sha256(readFileSync(target)) === hash) return 'cached';
  mkdirSync(dirname(target), { recursive: true });
  const local = findLocal(hash);
  if (local) { copyFileSync(local, target); return 'copied'; }
  const res = await fetch(url);
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`);
  const buf = Buffer.from(await res.arrayBuffer());
  if (sha256(buf) !== hash) throw new Error(`${url}: SHA-256 is ${sha256(buf)}, pinned ${hash}`);
  writeFileSync(target, buf);
  return 'downloaded';
}

const h = pins.harlowe;
console.log(`harlowe ${h.file}: ${await get(formatFile(), h.url, h.sha256)}`);

for (const s of pins.stories) {
  if (!(s.default || named.includes(s.id) || args.includes('--all-stories'))) continue;
  const url = `https://raw.githubusercontent.com/${s.repo}/${s.commit}/${s.path.split('/').map(encodeURIComponent).join('/')}`;
  console.log(`story ${s.id}: ${await get(storyFile(s), url, s.sha256)}`);
}

const ref = join(REPO, pins.reference.dir);
const archival = join(ref, '.hg_archival.txt');
if (!existsSync(archival)) console.log(`reference: not unpacked at ${pins.reference.dir} (only the specs suite needs it)`);
else {
  const node = /^node: (\w+)/m.exec(readFileSync(archival, 'utf8'))?.[1];
  console.log(`reference: ${node === pins.reference.node ? 'pinned node' : `node ${node}, pinned ${pins.reference.node}`}`);
}
