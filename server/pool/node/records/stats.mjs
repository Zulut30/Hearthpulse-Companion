// Numbers of the record views (contract K3/K4, docs/analytics-records.md): shares with a Wilson interval, means with
// ±1.96·s/√n, placements with their distribution, the publication thresholds and the ranking rule. Every number needs
// minInstalls distinct installations among its own observations, else it is null; above that, a denominator under
// minGames keeps its counts and hides the value. A number is `rankable` when its interval is narrow enough to order
// rows by it.
import { wilson } from '../analytics.mjs';

/** Largest interval half-width a rankable number may have: 5 points of a share, 0.3 of a place, 0.3 of a win. */
export const LIMITS = Object.freeze({ rate: 0.05, place: 0.3, wins: 0.3 });
const Z = 1.96;
// Floating-point slack so that a half-width of exactly the limit counts as within it.
const EPSILON = 1e-9;

const round = x => (x === null || !Number.isFinite(x) ? null : Math.round(x * 1000) / 1000);
const size = installs => (installs instanceof Set ? installs.size : Number.isSafeInteger(installs) ? installs : 0);

/** Running count, mean and sum of squared deviations (Welford): a mean without keeping every value. */
export class Moments {
  n = 0;
  mean = 0;
  m2 = 0;
  add(x) {
    this.n++;
    const d = x - this.mean;
    this.mean += d / this.n;
    this.m2 += d * (x - this.mean);
    return this;
  }
}

/** The numbers of one pass, bound to its thresholds (ctx.stats of a view). */
export function createStats({ minInstalls = 5, minGames = 10 } = {}) {
  const enough = installs => size(installs) >= minInstalls;

  /** RATE {num, den, value, low, high, rankable}: num of den observations, made by `installs` (a Set or a count). */
  function rate(num, den, installs) {
    if (!enough(installs)) return null;
    if (!(den >= minGames) || den <= 0) return { num, den, value: null, low: null, high: null, rankable: false };
    const w = wilson(num, den);
    return { num, den, value: round(num / den), low: round(w.low), high: round(w.high), rankable: (w.high - w.low) / 2 <= LIMITS.rate + EPSILON };
  }

  /**
   * MEAN {n, mean, low, high, rankable} of an array of numbers or a Moments. `kind` names the ranking limit ('place',
   * 'wins'); a mean without one (turns, seconds) is never rankable.
   */
  function mean(values, installs, kind = null) {
    if (kind !== null && (kind === 'rate' || !Object.hasOwn(LIMITS, kind))) throw new Error('unknown_rank_limit');
    const limit = kind === null ? null : LIMITS[kind];
    if (!enough(installs)) return null;
    const m = values instanceof Moments ? values : values.reduce((acc, x) => acc.add(x), new Moments());
    if (m.n < minGames || m.n === 0) return { n: m.n, mean: null, low: null, high: null, rankable: false };
    if (m.n < 2) return { n: m.n, mean: round(m.mean), low: null, high: null, rankable: false };
    const half = Z * Math.sqrt(Math.max(0, m.m2 / (m.n - 1))) / Math.sqrt(m.n);
    return { n: m.n, mean: round(m.mean), low: round(m.mean - half), high: round(m.mean + half), rankable: limit !== null && half <= limit + EPSILON };
  }

  /**
   * PLACEMENT {n, mean, low, high, dist, rankable} of final places 1..max (8 solo, 4 duos). A place outside that range
   * is not counted. Under minGames the distribution is hidden with the mean: a handful of places is as revealing as
   * the places themselves.
   */
  function placement(places, installs, max = 8) {
    if (!enough(installs)) return null;
    const valid = places.filter(p => Number.isSafeInteger(p) && p >= 1 && p <= max);
    if (valid.length < minGames || valid.length === 0) return { n: valid.length, mean: null, low: null, high: null, dist: null, rankable: false };
    const dist = Array.from({ length: max }, () => 0);
    for (const p of valid) dist[p - 1]++;
    const m = mean(valid, installs, 'place');
    return { n: m.n, mean: m.mean, low: m.low, high: m.high, dist, rankable: m.rankable };
  }

  /** Whether a number (RATE, MEAN or PLACEMENT, or null) may order rows. */
  const rankable = x => x?.rankable === true;

  /** 'ok' when every number is published with a value, else 'small_sample'. */
  const quality = (...numbers) => (numbers.every(x => x && (x.value ?? x.mean) !== null && (x.value ?? x.mean) !== undefined) ? 'ok' : 'small_sample');

  /**
   * Rows of the cells with minInstalls installations or more (`cell.installs`, a Set or a count), each made by
   * `makeRow(cell)` (null leaves it out); the cells under the threshold are only counted in `suppressed`.
   */
  function rows(cells, makeRow) {
    let suppressed = 0;
    const out = [];
    for (const cell of cells) {
      if (!enough(cell.installs)) { suppressed++; continue; }
      const row = makeRow(cell);
      if (row) out.push(row);
    }
    return { rows: out, suppressed };
  }

  /** Rankable rows first, each group in `compare` order (stable): an unrankable row never sorts above a rankable one. */
  function rankFirst(list, isRankable, compare) {
    return [...list].sort((a, b) => (isRankable(b) ? 1 : 0) - (isRankable(a) ? 1 : 0) || compare(a, b));
  }

  return Object.freeze({ minInstalls, minGames, enough, rate, mean, placement, rankable, quality, rows, rankFirst, Moments });
}
