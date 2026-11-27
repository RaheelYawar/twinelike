// Where the bench keeps things, relative to this file, so it runs from any directory.
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const BENCH = resolve(dirname(fileURLToPath(import.meta.url)), '..');
export const TOOLS = resolve(BENCH, '..');
export const REPO = resolve(TOOLS, '..');
export const CASES = join(TOOLS, 'cases');
export const CACHE = join(TOOLS, '.cache');
export const PINS = join(TOOLS, 'pins.json');
export const PROBE_PROJECT = join(TOOLS, 'Twinelike.Probe', 'Twinelike.Probe.csproj');
export const PROBE_DLL = join(TOOLS, 'Twinelike.Probe', 'bin', 'Release', 'net8.0', 'Twinelike.Probe.dll');
export const FEATURE_COVERAGE = join(REPO, 'TestFiles', 'feature-coverage.twee');
