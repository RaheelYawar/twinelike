// The counts a run may not fall below, kept in tools/cases/expected.json as
// { "<suite>": { "matched": n, "compared": n } }. Fewer matches fails, and so
// does comparing fewer cases, since more cases left out can hide a loss.

/** Records this run's counts and says how they stand against the recorded ones. */
export function ratchet(key, matched, compared, counts, expected) {
  counts[key] = { matched, compared };
  const want = expected[key];
  if (!want) return { failed: false, note: '' };
  const notes = [];
  let failed = false;
  if (matched < want.matched) { failed = true; notes.push(`FEWER matches than the ${want.matched} recorded`); }
  else if (matched > want.matched) notes.push(`more matches than the ${want.matched} recorded`);
  if (compared < want.compared) { failed = true; notes.push(`FEWER compared than the ${want.compared} recorded`); }
  else if (compared > want.compared) notes.push(`more compared than the ${want.compared} recorded`);
  return { failed, note: notes.length ? `  ${notes.join(', ')}` : '' };
}
