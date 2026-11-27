// Finds the derivers (derive/*.mjs) and views (views/*.mjs) of the record pass and checks each against its contract
// (K2, docs/analytics-records.md) and, when this checkout has it, against the metric catalogue. A module that fails to
// load or breaks the contract is left out with one log line (records_module_rejected); the others load. There is no
// list to edit: a package adds its file, as Core's EffectRegistry finds effects.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { LINES } from './periods.mjs';

/** The folder holding derive/ and views/. */
export const MODULE_DIR = fileURLToPath(new URL('./', import.meta.url));
/** The catalogue in a full checkout; a deployed pool is server/pool alone and has none (CI checks it instead). */
export const CATALOG_FILE = fileURLToPath(new URL('../../../../docs/analytics-metrics.json', import.meta.url));
/** How a catalogue metric names the route of its view: `api` holds /v2/analytics/records/<view key>. */
export const RECORDS_ROUTE = /\/v2\/analytics\/records\/([a-z][a-z0-9-]*)/;

const KEY = /^[a-z][a-z0-9-]{0,47}$/;
const NAME = /^[a-z][a-z0-9-]{0,31}$/;
const VERSION = /^[A-Za-z0-9._-]{1,48}$/;
const PARAM = /^[a-z][A-Za-z0-9]{0,23}$/;
const RESERVED = new Set(['window', 'patch']);
const fn = x => typeof x === 'function';

/** The catalogue's metrics by id, or null when the file is not there. A file that is there but unreadable throws. */
export function readCatalog(file = CATALOG_FILE) {
  if (!existsSync(file)) return null;
  const raw = JSON.parse(readFileSync(file, 'utf8'));
  if (!Array.isArray(raw?.metrics)) throw new Error('invalid_catalog');
  return new Map(raw.metrics.map(m => [m.id, m]));
}

/**
 * A view module as the pass uses it, or {reason} when it breaks the contract: key (equal to the file name when there
 * is one), metric, rules, line, modes of that line, query validators (not `window` or `patch`), start/add/finish/slice
 * and an optional unsupported(params). With a catalogue, the metric must be `implemented` with the same rulesVersion.
 */
export function checkView(m, { file = null, catalog = null } = {}) {
  if (typeof m?.key !== 'string' || !KEY.test(m.key) || (file !== null && path.basename(file, '.mjs') !== m.key)) return { reason: 'key' };
  if (typeof m.metric !== 'string' || m.metric.length === 0 || m.metric.length > 64) return { reason: 'metric' };
  if (typeof m.rules !== 'string' || !VERSION.test(m.rules)) return { reason: 'rules' };
  if (!Object.hasOwn(LINES, m.line)) return { reason: 'line' };
  if (!Array.isArray(m.modes) || m.modes.length === 0 || new Set(m.modes).size !== m.modes.length
    || !m.modes.every(mode => LINES[m.line].includes(mode))) return { reason: 'modes' };
  if (!m.query || typeof m.query !== 'object' || Array.isArray(m.query)
    || !Object.entries(m.query).every(([name, check]) => PARAM.test(name) && !RESERVED.has(name) && fn(check))) return { reason: 'query' };
  if (![m.start, m.add, m.finish, m.slice].every(fn) || (m.unsupported !== undefined && !fn(m.unsupported))) return { reason: 'functions' };
  if (catalog) {
    const metric = catalog.get(m.metric);
    if (metric?.status !== 'implemented' || metric.rulesVersion !== m.rules) return { reason: 'catalog' };
  }
  return {
    view: Object.freeze({
      key: m.key, metric: m.metric, rules: m.rules, line: m.line, modes: Object.freeze([...m.modes]),
      query: Object.freeze({ ...m.query }), start: m.start, add: m.add, finish: m.finish, slice: m.slice,
      unsupported: m.unsupported ?? null,
    }),
  };
}

/** A deriver as the pass uses it, or {reason}: name, numeric order, version and derive(rec, ctx). */
export function checkDeriver(m) {
  if (typeof m?.name !== 'string' || !NAME.test(m.name)) return { reason: 'name' };
  if (!Number.isFinite(m.order)) return { reason: 'order' };
  if (typeof m.version !== 'string' || !VERSION.test(m.version)) return { reason: 'version' };
  if (!fn(m.derive)) return { reason: 'functions' };
  return { deriver: Object.freeze({ name: m.name, order: m.order, version: m.version, derive: m.derive }) };
}

/**
 * Where the catalogue and the loaded views disagree (AC8): a metric naming a records route that no loaded view of that
 * key serves for it. (A view whose metric is not implemented with its rules never loads.) Empty when they agree.
 */
export function catalogProblems(catalog, views) {
  const problems = [];
  for (const metric of catalog.values()) {
    const key = RECORDS_ROUTE.exec(String(metric.api ?? ''))?.[1];
    if (!key) continue;
    const view = views.find(v => v.key === key);
    if (!view) problems.push({ metric: metric.id, problem: 'no_view', view: key });
    else if (view.metric !== metric.id) problems.push({ metric: metric.id, problem: 'other_metric', view: key });
  }
  return problems;
}

const files = (dir, folder) => {
  const where = path.join(dir, folder);
  if (!existsSync(where)) return [];
  return readdirSync(where, { withFileTypes: true }).filter(e => e.isFile() && e.name.endsWith('.mjs')).map(e => e.name).sort();
};

/**
 * Loads every module under `dir` (default: this folder): derivers ordered by `order`, then name; views by key.
 * `catalog` is the catalogue map, or null for none; by default the checkout's file when present. An unreadable
 * catalogue is logged (records_catalog_unreadable) and the views load without that check: a docs file never stops
 * the service, and CI reads the same file strictly. Returns {derivers, views, rejected}.
 */
export async function loadModules({ dir = MODULE_DIR, catalog, catalogFile = CATALOG_FILE, log = console.error } = {}) {
  if (catalog === undefined) {
    try { catalog = readCatalog(catalogFile); } catch {
      catalog = null;
      log(JSON.stringify({ ok: false, error: 'records_catalog_unreadable' }));
    }
  }
  const rejected = [];
  const reject = (module, reason) => {
    rejected.push({ module, reason });
    log(JSON.stringify({ ok: false, error: 'records_module_rejected', module, reason }));
  };
  const load = async (folder, name) => {
    try { return await import(pathToFileURL(path.join(dir, folder, name)).href); } catch { return null; }
  };

  const derivers = [];
  for (const name of files(dir, 'derive')) {
    const module = `derive/${name}`;
    const m = await load('derive', name);
    if (!m) { reject(module, 'import'); continue; }
    const { deriver, reason } = checkDeriver(m);
    if (reason) reject(module, reason);
    else if (derivers.some(d => d.name === deriver.name)) reject(module, 'duplicate');
    else derivers.push(deriver);
  }
  const views = [];
  for (const name of files(dir, 'views')) {
    const module = `views/${name}`;
    const m = await load('views', name);
    if (!m) { reject(module, 'import'); continue; }
    const { view, reason } = checkView(m, { file: name, catalog });
    if (reason) reject(module, reason);
    else views.push(view);
  }
  derivers.sort((a, b) => a.order - b.order || (a.name < b.name ? -1 : a.name > b.name ? 1 : 0));
  views.sort((a, b) => (a.key < b.key ? -1 : a.key > b.key ? 1 : 0));
  return { derivers, views, rejected };
}
