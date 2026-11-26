// Slices of a game record (contract K3 of TASK-2026-11-26-pro-collection, docs/analytics-records.md): rank buckets,
// Battlegrounds MMR ranges and the coin. A record falls into every bucket that contains it, so a view adds it once per
// bucket it serves; a record without the field is in the first bucket ("all", "any") only.

export const RANKS = Object.freeze(['all', 'bronze', 'silver', 'gold', 'platinum', 'diamond', 'd4-1', 'legend', 'd4-legend']);
export const MMR = Object.freeze(['all', '0-3999', '4000-5999', '6000-7999', '8000+']);
export const COIN = Object.freeze(['any', 'first', 'coin']);

const LEAGUES = new Set(['bronze', 'silver', 'gold', 'platinum', 'diamond', 'legend']);

/**
 * Rank buckets of a record. Leagues exist in Ranked only; "d4-1" is the band the server kept for diamond 4 to 1
 * (star level 47 to 50), and "d4-legend" joins it with legend: HSReplay's high bucket without a thinner sample.
 */
export function ranksOf(mode, body) {
  if (mode !== 'ranked' || !LEAGUES.has(body?.rank)) return ['all'];
  const out = ['all', body.rank];
  if (body.rank === 'diamond' && body.band === 'd4-1') out.push('d4-1', 'd4-legend');
  else if (body.rank === 'legend') out.push('d4-legend');
  return out;
}

/** MMR ranges of a Battlegrounds record; the rating a record holds is rounded down to 500 and at most 10,000. */
export function mmrOf(mode, body) {
  const mmr = body?.bg?.mmr;
  if ((mode !== 'bg' && mode !== 'duos') || !Number.isInteger(mmr) || mmr < 0) return ['all'];
  return ['all', mmr < 4000 ? '0-3999' : mmr < 6000 ? '4000-5999' : mmr < 8000 ? '6000-7999' : '8000+'];
}

/** Coin buckets: "first" went first, "coin" went second with the Coin; a record without `first` is in "any" only. */
export function coinOf(body) {
  if (body?.first === true) return ['any', 'first'];
  if (body?.first === false) return ['any', 'coin'];
  return ['any'];
}

/**
 * A query validator for a view (contract K2 `query`): the value when it is one of `values`, `fallback` when the
 * parameter is absent, undefined (answered 400 invalid_query) otherwise.
 */
export function oneOf(values, fallback) {
  const allowed = new Set(values);
  return value => (value === undefined ? fallback : allowed.has(value) ? value : undefined);
}

/** The validators every view asks the same way. */
export const param = Object.freeze({
  rank: oneOf(RANKS, 'all'),
  mmr: oneOf(MMR, 'all'),
  coin: oneOf(COIN, 'any'),
});
