// Compares Twinelike's output with real Harlowe's, case by case.
//
// A markup case's result is one string. A step case's result is the list of
// texts the page showed after each step, and it matches only if every step does.

/** Replaces each ⟦link:…⟧ with its label, brackets nested in the label included. */
export function unwrapLinks(s) {
  const open = '⟦link:';
  let out = '', i = 0;
  const stack = [];
  while (i < s.length) {
    if (s.startsWith(open, i)) { stack.push(true); i += open.length; continue; }
    if (s[i] === '⟦') { stack.push(false); out += s[i++]; continue; }
    if (s[i] === '⟧' && stack.length) { if (!stack.pop()) out += '⟧'; i++; continue; }
    out += s[i++];
  }
  return out;
}

const reduceErrors = s => s.replace(/⟦error: [^⟧]*⟧/g, '⟦error⟧');

/**
 * Returns { compared, matched, skipped, differ: [index] }.
 * options: unwrapLinks, reduceErrors (both sides), skipUncomparable, lastOnly
 * (real holds only each case's final page).
 *
 * skipUncomparable leaves out a case real Harlowe rendered as a list, which the
 * snippet format flattens, and a case our output prints with a literal `<n|`:
 * real Harlowe builds its page as HTML, so the browser reads `<n|` and what
 * follows as a tag and drops it (MACRO-DIVERGENCES.md #45), and there's no
 * text left to compare.
 */
export function compareSet(cases, real, ours, options = {}) {
  const norm = s => {
    let t = String(s);
    if (options.reduceErrors) t = reduceErrors(t);
    if (options.unwrapLinks) t = unwrapLinks(t);
    return t;
  };
  const same = (r, o) => {
    if (options.lastOnly) return norm(r) === norm(Array.isArray(o) ? o[o.length - 1] : o);
    if (Array.isArray(r) || Array.isArray(o)) {
      if (!Array.isArray(r) || !Array.isArray(o) || r.length !== o.length) return false;
      for (let i = 0; i < r.length; i++) if (norm(r[i]) !== norm(o[i])) return false;
      return true;
    }
    return norm(r) === norm(o);
  };
  const result = { compared: 0, matched: 0, skipped: 0, differ: [] };
  if (cases.length !== real.length || cases.length !== ours.length)
    throw new Error(`length mismatch: ${cases.length} cases, ${real.length} real, ${ours.length} ours`);
  for (let i = 0; i < cases.length; i++) {
    if (options.skipUncomparable && (String(real[i]).includes('⟦list:') || String(ours[i]).includes('<n|'))) { result.skipped++; continue; }
    result.compared++;
    if (same(real[i], ours[i])) result.matched++;
    else result.differ.push(i);
  }
  return result;
}

/** How many results differ between two runs of the same cases. */
export function changedBetween(before, after) {
  if (!before || before.length !== after.length) return null;
  let n = 0;
  for (let i = 0; i < after.length; i++) if (JSON.stringify(before[i]) !== JSON.stringify(after[i])) n++;
  return n;
}

export function describeCase(c) {
  if (typeof c === 'string') return c;
  if (c.source !== undefined) return c.source;
  return JSON.stringify(c);
}
