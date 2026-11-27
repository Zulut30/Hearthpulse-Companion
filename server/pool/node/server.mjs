import http from 'node:http';
import * as fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { timingSafeEqual } from 'node:crypto';
import { DatabaseSync } from 'node:sqlite';
import legacy, { prune } from '../src/index.js';
import { pruneEvents } from '../src/events.js';
import { CaptureArchive } from './archive.mjs';
import { bgHeroesResponse, thresholds } from './analytics.mjs';
import * as records from './records/index.mjs';
import { loadModules } from './records/loader.mjs';
import { readPatches } from './records/periods.mjs';
import { openDatabase, ResponseCache, SerialQueue } from './sqlite.mjs';
import { CaptureError, fail, MiB, int, parseJson } from './validation.mjs';

// Views of the record pass (docs/analytics-records.md): GET only, served from the published snapshot.
const RECORDS_ROUTE = '/v2/analytics/records/';

const json = (body, code = 200) => new Response(JSON.stringify(body), { status: code, headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
const loopback = address => ['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(address);
// Loopback plus the operator token; the public proxy also refuses them (deploy/nginx-source-capture.conf).
const OPERATOR_ROUTES = ['/v1/captures/reprocess', '/v1/captures/combats/export'];
function operator(request, token) {
  if (!token || !loopback(request.socket.remoteAddress)) return false;
  const got = Buffer.from(request.headers.authorization ?? '');
  const expected = Buffer.from(`Bearer ${token}`);
  return got.length === expected.length && timingSafeEqual(got, expected);
}
async function bodyBytes(req, max) {
  if (req.headers['content-encoding'] && req.headers['content-encoding'] !== 'identity') fail(400, 'http_encoding_unsupported');
  if (Number(req.headers['content-length'] ?? 0) > max) fail(413, 'body_limit');
  const parts = [];
  let bytes = 0;
  for await (const data of req) {
    bytes += data.length;
    if (bytes > max) fail(413, 'body_limit');
    parts.push(data);
  }
  return Buffer.concat(parts, bytes);
}
function takeLock(root) {
  // A separate SQLite OS lock is held for the service lifetime. PID-file stale cleanup has a race in which
  // simultaneous restarts can both become writers. OS locks release on process death without stale-file removal.
  const guard = new DatabaseSync(path.join(root, 'instance.lock.sqlite'));
  try {
    guard.exec('PRAGMA busy_timeout=50; CREATE TABLE IF NOT EXISTS service(pid INTEGER); BEGIN EXCLUSIVE;');
    guard.prepare('INSERT INTO service(pid) VALUES(?)').run(process.pid);
  } catch (e) { guard.close(); throw e; }
  return () => { guard.exec('ROLLBACK'); guard.close(); };
}

/**
 * Starts a single-writer Linux/Windows Node service. Port 0 is supported for isolated loopback tests. `records` may
 * give tests their own record views and patches: {modules: {views, derivers}, patches, patchesFile, dir, log}.
 */
export async function startPool({ dataDir, host = '127.0.0.1', port = 3181, operatorToken = '',
  maxRequests = 4, archive: archiveOptions = {}, legacyEnv = {}, schedule = true, records: recordsOptions = {} } = {}) {
  if (!dataDir || !path.isAbsolute(dataDir) || !int(port, 0, 65535) || !int(maxRequests, 1, 32)) throw new Error('invalid_service_config');
  thresholds(); // An invalid ANALYTICS_MIN_* stops the start instead of failing every later rebuild silently.
  // A malformed balance-patches.json switches the record views off (records_patches_invalid), and only them: windows
  // read from a misread file would publish numbers under the wrong patch. A broken view or deriver leaves itself out.
  const recordLog = recordsOptions.log ?? console.error;
  let patches = recordsOptions.patches ?? null;
  if (patches === null) {
    try { patches = readPatches(recordsOptions.patchesFile); } catch (e) { reportFailure('records_patches_invalid', recordLog)(e); }
  }
  const modules = patches === null ? { views: [], derivers: [], rejected: [] }
    : recordsOptions.modules ?? await loadModules({ dir: recordsOptions.dir, log: recordLog });
  fs.mkdirSync(dataDir, { recursive: true, mode: 0o700 });
  const unlock = takeLock(dataDir);
  const queue = new SerialQueue();
  let store;
  const cache = new ResponseCache();
  let archive;
  try {
    store = openDatabase(path.join(dataDir, 'pool.sqlite'));
    archive = new CaptureArchive({ ...archiveOptions, sqlite: store.sqlite, root: path.join(dataDir, 'private'), queue });
    await archive.recover(); await archive.maintain();
  } catch (e) { store?.close(); unlock(); throw e; }
  const env = { ...legacyEnv, DB: store.DB, CACHE: cache };
  let inflight = 0;
  let stopping = false;
  const server = http.createServer(async (req, res) => {
    let counted = false;
    let response;
    try {
      if (stopping || inflight >= maxRequests) fail(503, 'intake_pressure');
      inflight++; counted = true;
      // Fixed internal origin; Host and proxy forwarding headers never grant operator access or affect cache keys.
      const url = new URL(req.url, 'http://pool.internal');
      const route = url.pathname;
      if (route === '/healthz' && req.method === 'GET') {
        const metadata = archive.metadata();
        response = json({ ok: true, workerConfigured: !!archive.worker, archiveBytes: archive.usage(), archiveCapBytes: archive.maxArchiveBytes,
          metadataBytes: metadata.bytes, metadataCapBytes: archive.maxMetadataBytes, sqliteFileBytes: metadata.fileBytes, sqliteFreeBytes: metadata.freeBytes,
          sqliteWalBytes: metadata.walBytes, sqliteShmBytes: metadata.shmBytes,
          statuses: store.sqlite.prepare('SELECT status,COUNT(*) AS n FROM source_captures GROUP BY status').all(),
          bgCombats: store.sqlite.prepare('SELECT COUNT(*) AS n FROM bg_combats').get().n,
          ...flow(store.sqlite) });
      } else if (route === '/v2/analytics/bg/heroes') {
        // Reads the published row only; the numbers are built in the archive queue, never in a request.
        if (req.method !== 'GET') fail(405, 'method_not_allowed');
        const answer = bgHeroesResponse(store.sqlite, url.searchParams);
        response = json(answer.body, answer.status);
      } else if (route.startsWith(RECORDS_ROUTE)) {
        // Reads the published rows only; the pass runs on its timer in the queue, never in a request.
        if (req.method !== 'GET') fail(405, 'method_not_allowed');
        if (patches === null) {
          // The views are off until a deploy fixes balance-patches.json: a passing outage, not a view the pool lacks, so
          // the app shows "offline" and asks again, instead of "not counted for this mode" for an hour (A5 review m5).
          response = json({ ok: false, error: 'records_off' }, 503);
          response.headers.set('retry-after', '60');
        } else {
          const answer = records.answer(store.sqlite, route.slice(RECORDS_ROUTE.length), url.searchParams, { modules, log: recordLog });
          response = json(answer.body, answer.status);
        }
      } else if (route.startsWith('/v2/')) {
        // nginx passes the whole /v2/analytics/ prefix: anything else under /v2 is not found, without the write queue.
        fail(404, 'not_found');
      } else if (route.startsWith('/v1/captures/')) {
        if (req.method !== 'POST') fail(405, 'method_not_allowed');
        if (OPERATOR_ROUTES.includes(route) && !operator(req, operatorToken)) fail(403, 'operator_only');
        const max = route === '/v1/captures/chunks' ? 6 * MiB : 64 * 1024;
        const bytes = await bodyBytes(req, max);
        let body;
        try { body = parseJson(new TextDecoder('utf-8', { fatal: true }).decode(bytes)); }
        catch (e) { if (e instanceof CaptureError) throw e; fail(400, 'invalid_json'); }
        const method = { '/v1/captures/chunks': 'upload', '/v1/captures/complete': 'complete', '/v1/captures/status': 'status',
          '/v1/captures/reprocess': 'reprocess', '/v1/captures/forget': 'forget', '/v1/captures/combats/export': 'exportCombats' }[route];
        if (!method) fail(404, 'not_found');
        const result = await archive[method](body);
        response = result.statusCode ? json(result.body, result.statusCode) : json(result);
      } else {
        const bytes = ['GET', 'HEAD'].includes(req.method) ? undefined : await bodyBytes(req, 512 * 1024);
        const headers = new Headers();
        if (req.headers['content-type']) headers.set('content-type', req.headers['content-type']);
        const request = new Request(`http://pool.internal${url.pathname}${url.search}`, { method: req.method, headers, body: bytes });
        response = await queue.run(async () => {
          if (req.method === 'POST' && url.pathname !== '/v1/events/forget') archive.pressure(512 * 1024);
          const pending = [];
          // Quota reservation and inserts commit together; readers and capture operations share this queue.
          store.sqlite.exec('BEGIN IMMEDIATE');
          try {
            const answer = await legacy.fetch(request, env, { waitUntil: p => pending.push(p) });
            await Promise.all(pending);
            if (answer.status >= 400) store.sqlite.exec('ROLLBACK'); else store.sqlite.exec('COMMIT');
            if (req.method === 'POST' && answer.status < 400) cache.clear();
            return answer;
          } catch (e) { store.sqlite.exec('ROLLBACK'); cache.clear(); throw e; }
        });
      }
    } catch (e) {
      const code = e instanceof CaptureError ? e.status : 503;
      response = json({ ok: false, error: e instanceof CaptureError ? e.code : 'temporarily_unavailable', ...(e instanceof CaptureError ? e.details : {}) }, code);
      if (code === 503) response.headers.set('retry-after', '30');
    } finally { if (counted) inflight--; }
    try {
      if (response.status === 503 && !response.headers.has('retry-after')) response.headers.set('retry-after', '30');
      res.writeHead(response.status, Object.fromEntries(response.headers));
      res.end(Buffer.from(await response.arrayBuffer()));
    } catch { res.destroy(); }
  });
  server.requestTimeout = 30_000;
  server.headersTimeout = 15_000;
  server.maxHeadersCount = 32;
  server.maxRequestsPerSocket = 100;
  server.on('clientError', (_error, socket) => socket.end('HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n'));
  try {
    await new Promise((resolve, reject) => { server.once('error', reject); server.listen(port, host, resolve); });
  } catch (e) { store.close(); unlock(); throw e; }
  // The minute's upkeep; on the descriptor too, so a test runs the very pass the timer runs.
  const maintenance = log => maintenanceTick({ archive, queue, env, cache }, log);
  // The record pass, in the queue (one transaction, nothing else writes meanwhile); it builds only when due.
  const refreshRecords = (options = {}) => queue.run(() => records.refresh(store.sqlite, { modules, patches, log: recordLog, ...options }));
  const timers = [];
  if (schedule) {
    timers.push(setInterval(() => { archive.tick().catch(() => {}); }, 1000));
    // At most once a minute, and only when nothing is published, the snapshot is stale/withdrawn or the day moved.
    timers.push(setInterval(() => { archive.refreshAnalytics().catch(reportFailure('analytics_refresh_failed')); }, 60_000));
    // Each step reports its own failure; this is the last guard, as an unhandled rejection stops Node 24.
    timers.push(setInterval(() => { maintenance().catch(reportFailure('maintenance_failed')); }, 60_000));
    timers.push(setInterval(() => { refreshRecords().catch(reportFailure('records_refresh_failed', recordLog)); }, 60_000));
    for (const t of timers) t.unref();
  }
  let closePromise;
  return {
    server, archive, store, maintenance, refreshRecords, recordModules: modules,
    url: `http://${host === '::1' ? '[::1]' : host}:${server.address().port}`,
    close() {
      return closePromise ??= (async () => {
        stopping = true;
        timers.forEach(clearInterval);
        const drained = new Promise(resolve => server.close(resolve));
        await archive.close();
        await drained;
        await queue.run(() => { store.sqlite.exec('PRAGMA wal_checkpoint(TRUNCATE)'); store.close(); unlock(); });
      })();
    },
  };
}

/** Logs a background failure as one technical line: a code and the error name/message, never source content. */
export function reportFailure(code, log = console.error) {
  return error => log(JSON.stringify({ ok: false, error: code, cause: String(error?.message ?? error).slice(0, 200) }));
}

/**
 * One pass of the minute's upkeep. The retention deletes run first, each on its own: shared games and picks after 60
 * days, game records after 180. A failed step is one reportFailure line with its code and stops neither the other
 * delete nor the archive. The archive's maintenance runs last: its WAL checkpoint frees the pages the deletes released.
 */
export async function maintenanceTick({ archive, queue, env, cache }, log = console.error) {
  await queue.run(async () => {
    await prune(env).catch(reportFailure('shared_prune_failed', log));
    await pruneEvents(env).catch(reportFailure('records_prune_failed', log));
    cache.clear();
  });
  await archive.maintain().catch(reportFailure('archive_maintain_failed', log));
}

function envInt(name, fallback, min, max) {
  const value = process.env[name] === undefined ? fallback : Number(process.env[name]);
  if (!int(value, min, max)) throw new Error('invalid_environment');
  return value;
}
export function configFromEnvironment() {
  const executable = process.env.POOL_WORKER_EXECUTABLE;
  const args = process.env.POOL_WORKER_ARGS;
  if (!!executable !== !!args) throw new Error('worker_configuration_incomplete');
  let worker = null;
  if (executable) {
    worker = { executable, args: JSON.parse(args) };
    if (!path.isAbsolute(executable) || !Array.isArray(worker.args) || !worker.args.every(s => typeof s === 'string')) throw new Error('invalid_worker_configuration');
  }
  return {
    dataDir: process.env.POOL_DATA_DIR,
    host: process.env.POOL_HOST ?? '127.0.0.1',
    port: envInt('POOL_PORT', 3181, 0, 65535),
    operatorToken: process.env.POOL_OPERATOR_TOKEN ?? '',
    maxRequests: envInt('POOL_MAX_REQUESTS', 4, 1, 32),
    legacyEnv: { DAILY_WRITES: envInt('POOL_LEGACY_DAILY', 15000, 1, 1000000), EVENTS_DAILY: envInt('POOL_EVENTS_DAILY', 10000, 1, 1000000) },
    archive: {
      worker,
      maxArchiveBytes: envInt('POOL_ARCHIVE_BYTES', 256 * MiB, MiB, 1024 ** 4),
      dailyBytes: envInt('POOL_DAILY_BYTES', 128 * MiB, 1, 1024 ** 4),
      dailyCaptures: envInt('POOL_DAILY_CAPTURES', 1000, 1, 1000000),
      minFreeBytes: envInt('POOL_MIN_FREE_BYTES', 64 * MiB, 0, 1024 ** 4),
      retentionDays: envInt('POOL_RETENTION_DAYS', 180, 1, 180),
      unfinishedHours: envInt('POOL_UNFINISHED_HOURS', 24, 1, 168),
      workerTimeoutMs: envInt('POOL_WORKER_TIMEOUT_MS', 120000, 100, 600000),
    },
  };
}

/**
 * "Alive" is not "producing": what arrived today (the write counters the quotas keep), what is stored, and how old the
 * analytics snapshot is. Counts and times only; no id, generation or body. `records` is the record pass: its last
 * build, and how many views are published, withdrawn or failed in it.
 */
export function flow(db) {
  const day = Math.floor(Date.now() / 86_400_000);
  const one = (sql, ...args) => db.prepare(sql).get(...args)?.n ?? 0;
  const snap = db.prepare("SELECT built,stale,body<>'' AS published FROM analytics_snap WHERE key='bg-heroes-solo'").get();
  return {
    intake: { day, shared: one('SELECT n FROM quota WHERE day=?', day), records: one('SELECT n FROM equota WHERE day=?', day) },
    stored: { shared: one('SELECT COUNT(*) AS n FROM g'), picks: one('SELECT COUNT(*) AS n FROM c'), records: one('SELECT COUNT(*) AS n FROM e') },
    analytics: snap ? { built: snap.built, stale: !!snap.stale, published: !!snap.published } : { built: null, stale: false, published: false },
    records: records.status(db),
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  process.umask(0o077);
  try {
    const pool = await startPool(configFromEnvironment());
    console.log(JSON.stringify({ ok: true, status: 'listening', port: pool.server.address().port }));
    let stopping = false;
    for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => {
      if (stopping) return;
      stopping = true;
      pool.close().then(() => { process.exitCode = 0; }).catch(() => { process.exitCode = 1; });
    });
  } catch { console.error(JSON.stringify({ ok: false, error: 'startup_failed' })); process.exitCode = 1; }
}
