// The pinned third-party inputs and where fetch.mjs puts them.
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { CACHE, PINS } from './paths.mjs';

export const pins = JSON.parse(readFileSync(PINS, 'utf8'));
export const formatFile = () => join(CACHE, pins.harlowe.file);
export const storyFile = s => join(CACHE, 'stories', s.id, s.path.split('/').pop());
export const story = id => pins.stories.find(s => s.id === id);
