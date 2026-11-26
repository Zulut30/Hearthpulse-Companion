// Balance patches and windows (contract K3, docs/analytics-records.md). A record's build places it in a balance patch
// of its line (server/pool/data/balance-patches.json, kept by hand: builds without card changes share a patch); the
// windows of a line are the newest patch (whole, last 7 days, last 30 days), the previous patch (whole) and the
// records with an unknown build (last 30 days). Only the last READ_DAYS days of records are read.
import { readFileSync } from 'node:fs';

export const READ_DAYS = 90;
export const UNKNOWN = 'unknown';
export const WINDOWS = Object.freeze(['patch', '7d', '30d']);
/** The game modes each line counts. */
export const LINES = Object.freeze({
  constructed: Object.freeze(['ranked']),
  battlegrounds: Object.freeze(['bg', 'duos']),
  arena: Object.freeze(['arena', 'arena_run']),
});
export const PATCH_ID = /^[A-Za-z0-9._-]{1,32}$/;
const MAX_BUILD = 99_999_999;
const LINE_OF = new Map(Object.entries(LINES).flatMap(([line, modes]) => modes.map(mode => [mode, line])));

const int = (x, min, max) => Number.isSafeInteger(x) && x >= min && x <= max;
const invalid = () => { throw new Error('invalid_balance_patches'); };

/** The line of a game mode, or null for a mode no view counts (casual, brawl). */
export const lineOf = mode => LINE_OF.get(mode) ?? null;

/** Reads and checks the patches file; throws 'invalid_balance_patches' for a file the pass could misread. */
export function readPatches(file = new URL('../../data/balance-patches.json', import.meta.url)) {
  let raw;
  try { raw = JSON.parse(readFileSync(file, 'utf8')); } catch { invalid(); }
  return checkPatches(raw);
}

/**
 * The patches as the pass uses them: each line's entries ordered by `fromBuild`. Every line is present (an empty list
 * when the file has none); an unknown line, a repeated id or build, an id "unknown", a build or day out of range, or a
 * later build that starts on an earlier day is refused.
 */
export function checkPatches(raw) {
  if (!raw || typeof raw !== 'object' || raw.v !== 1 || !raw.lines || typeof raw.lines !== 'object' || Array.isArray(raw.lines)) invalid();
  if (Object.keys(raw.lines).some(line => !Object.hasOwn(LINES, line))) invalid();
  const lines = {};
  for (const line of Object.keys(LINES)) {
    const list = raw.lines[line] ?? [];
    if (!Array.isArray(list)) invalid();
    const entries = list.map(e => {
      if (!e || typeof e.id !== 'string' || !PATCH_ID.test(e.id) || e.id === UNKNOWN
        || !int(e.fromBuild, 0, MAX_BUILD) || !int(e.fromDay, 0, 1_000_000)) invalid();
      return Object.freeze({ id: e.id, fromBuild: e.fromBuild, fromDay: e.fromDay });
    }).sort((a, b) => a.fromBuild - b.fromBuild);
    entries.forEach((e, i) => {
      const before = entries[i - 1];
      if (before && (before.fromBuild === e.fromBuild || before.fromDay > e.fromDay)) invalid();
      if (entries.findIndex(x => x.id === e.id) !== i) invalid();
    });
    lines[line] = Object.freeze(entries);
  }
  return Object.freeze({ v: 1, lines: Object.freeze(lines) });
}

/** The patch id of a build in a line: the entry with the highest fromBuild not above it; null for an unknown build. */
export function patchOf(patches, line, build) {
  if (!Number.isSafeInteger(build)) return null;
  let found = null;
  for (const entry of patches.lines[line] ?? []) {
    if (entry.fromBuild > build) break;
    found = entry.id;
  }
  return found;
}

/** The newest patch id of a line, or null when the file has none for it. */
export const latestPatch = (patches, line) => patches.lines[line]?.at(-1)?.id ?? null;

/**
 * The windows of a line on day `today`, each with the days it covers and `has(rec)`, whether a record belongs to it.
 * Membership is by build (the patch) and, for 7d and 30d, by day; the days of a whole patch are those of the patch
 * inside the read range: from its `fromDay` (or the first day read) to today, or for the previous patch to the day
 * before the newest one starts.
 */
export function windowsOf(patches, line, today) {
  const list = patches.lines[line] ?? [];
  const first = today - (READ_DAYS - 1);
  const newest = list.at(-1);
  const previous = list.at(-2);
  const out = [];
  if (newest) {
    const from = Math.min(today, Math.max(newest.fromDay, first));
    out.push({ patch: newest.id, window: 'patch', fromDay: from, toDay: today, has: r => r.patch === newest.id });
    out.push({ patch: newest.id, window: '7d', fromDay: Math.max(from, today - 6), toDay: today, has: r => r.patch === newest.id && r.day >= today - 6 });
    out.push({ patch: newest.id, window: '30d', fromDay: Math.max(from, today - 29), toDay: today, has: r => r.patch === newest.id && r.day >= today - 29 });
  }
  if (previous) {
    const from = Math.min(today, Math.max(previous.fromDay, first));
    const to = newest.fromDay > from ? Math.min(today, newest.fromDay - 1) : today;
    out.push({ patch: previous.id, window: 'patch', fromDay: from, toDay: to, has: r => r.patch === previous.id });
  }
  out.push({ patch: UNKNOWN, window: '30d', fromDay: today - 29, toDay: today, has: r => r.patch === null && r.day >= today - 29 });
  return out;
}
