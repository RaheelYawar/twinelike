# Test bench

The evidence that Twinelike plays stories the way Harlowe 3.3.9 does. Everything here compares what Twinelike shows with what real Harlowe showed for the same input, recorded in headless Chromium.

```
pins.json             third-party inputs: format.js and stories by URL, commit and SHA-256, the reference by node
cases/                our snippet cases, what real 3.3.9 showed for them, and the expected match counts
Twinelike.Probe/      Twinelike's side: plays cases, walks and specs through StorySession
bench/                Node: verify, fetch, record, specs
.cache/               gitignored: fetched inputs, recordings, and each run's output
```

You need the .NET 8 SDK and Node 20 or newer.

## Check a change

```sh
node tools/bench/verify.mjs
```

This builds the probe, runs every case set in `cases/suites.json` and prints how many cases match real Harlowe, with the first few that don't. It needs no browser and no network. A suite that matches or compares fewer cases than `cases/expected.json` records fails the run. When a change earns more matches, or a deliberate one costs some, record the new counts with `--update`, and record a deliberate loss in `MACRO-DIVERGENCES.md`. Each run's output is kept in `.cache/last`, so the next run also says how many results changed, matching or not.

| Flag | Adds |
|---|---|
| `--fuzz` | 4 seeds × 150 s of mutated stories through the whole host surface, and the case that once hung. Nothing may throw or hang |
| `--stack` | Every kind of deep recursion on 512, 256 and 160 KB stacks. Each must end in an in-prose error, not a crash |
| `--walks` | Seeded random walks of the pinned stories, recorded on real Harlowe |
| `--specs` | Reference Harlowe's own specs, under both profiles, beside real 3.3.9's results |
| `--all` | All of the above |

`--only snippets,lists` runs some suites, and `--show N` prints more differences.

## Walks and specs

These use third-party content, which is fetched into `.cache` and never committed.

```sh
cd tools/bench
npm install                                     # playwright-core, for recording only
npx playwright-core install chromium-headless-shell
node fetch.mjs                                  # format.js and the default stories, checked by SHA-256
node record.mjs walks                           # records walks on real Harlowe
node specs.mjs extract                          # needs the reference, see below
node specs.mjs real
node verify.mjs --walks --specs
```

- `fetch.mjs --from <dir>` copies a file with the pinned hash from a local folder instead of downloading it.
- The browser runs with outside network blocked. If the browser this `playwright-core` expects isn't installed, the highest-numbered Chromium or headless shell Playwright has installed is used, and `BENCH_BROWSER` names one outright.
- The reference specs come from reference Harlowe's source, downloaded by hand from [Heptapod](https://foss.heptapod.net/games/harlowe) (branch `default`, at the node `pins.json` names) because the host blocks scripts. Unpack it at the `pins.json` reference path. Extraction stops unless `.hg_archival.txt` there names the pinned node.
- egg.exe and Sand-dancer are fetched only when named (`fetch.mjs --story egg-exe`). egg.exe deals with mental health and self-harm, so its text stays out of logs and reports. Sand-dancer ports a game by other authors and is never redistributed.

## Add cases

From the repository root:

1. Write the cases as `tools/cases/<suite>/<set>.cases.json`, in the shape the suite's probe command reads. `node tools/bench/record.mjs` with no arguments lists the shapes.
2. Record real Harlowe: `node tools/bench/record.mjs <kind> tools/cases/<suite>/<set>.cases.json tools/cases/<suite>/<set>.real.json`. The kind is the suite's: `markup` for snippets, macros, custom and unset, `lists`, `errors` for datamap names, `clicks`, `nav`, `live` for live, collapse and revisions, and `live-errors` for `(after:)`.
3. Add the set to `tools/cases/suites.json`, then run `node tools/bench/verify.mjs --update`.

Cases are our own snippets. Don't paste in story text, which belongs in `pins.json`.
