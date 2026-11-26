// One pass over the game records (contract K0/K2, docs/analytics-records.md): the records of the last READ_DAYS days,
// up to today, of the modes the loaded views count, each read once and turned into a Rec with its build, line, patch
// and the buckets it falls into. Rows stream from SQLite in (day, mode, id) order, the index order, so a pass is
// repeatable.
import { coinOf, mmrOf, ranksOf } from './buckets.mjs';
import { READ_DAYS, lineOf, patchOf } from './periods.mjs';

/**
 * The Recs of day `today` in `modes`: {iid, day, mode, fmt, build, line, patch, ranks, mmr, coin, body, d}. `iid` is
 * there to count installations and is never published; `body` is the record as validateEvent stored it; `d` is empty
 * for the derivers to fill. A mode outside every line is never read. A body that does not parse is skipped and
 * counted in `counts.skipped`.
 */
export function* scan(db, { today, patches, modes, counts = { read: 0, skipped: 0 } }) {
  const wanted = modes.filter(mode => lineOf(mode) !== null);
  if (wanted.length === 0) return;
  const read = db.prepare(`SELECT iid, day, mode, fmt, body FROM e
    WHERE day BETWEEN ? AND ? AND mode IN (${wanted.map(() => '?').join(', ')})
    ORDER BY day, mode, id`);
  for (const row of read.iterate(today - (READ_DAYS - 1), today, ...wanted)) {
    let body;
    try { body = JSON.parse(row.body); } catch { body = null; }
    if (!body || typeof body !== 'object' || Array.isArray(body)) { counts.skipped++; continue; }
    counts.read++;
    const line = lineOf(row.mode);
    const build = Number.isSafeInteger(body.build) ? body.build : null;
    yield {
      iid: row.iid, day: row.day, mode: row.mode, fmt: row.fmt ?? null, build, line, patch: patchOf(patches, line, build),
      ranks: ranksOf(row.mode, body), mmr: mmrOf(row.mode, body), coin: coinOf(body), body, d: {},
    };
  }
}
