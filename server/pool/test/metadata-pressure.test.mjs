// What each intake budget counts. The archive budget: source chunks (uncompressed + compressed), derived results,
// a bounded reservation per running job and the revocation ledger. The metadata budget: live SQLite pages without
// those derived results, plus the whole WAL and SHM. Real SQLite, files and worker processes in a temp directory.
import assert from 'node:assert/strict';
import * as fs from 'node:fs';
import path from 'node:path';
import { gzipSync } from 'node:zlib';
import test from 'node:test';
import { ID, start, end, choice, jsonl, source, chunk, manifest, setup, workerConfig } from './capture-fixture.mjs';
import { identity, MiB } from '../node/validation.mjs';

const PAGE = 4096;
const bounded = { timeout: 60_000 }; // Bounds a hang only; no result depends on it.
const big = (choices = 3000) => jsonl([start(), ...Array.from({ length: choices }, (_, i) => choice(i + 1)), end(choices + 1)]);
const capture = n => ({ ...ID, captureId: n.toString(16).padStart(32, '0') });
const generationId = { v: 1, iid: ID.iid, generation: ID.generation };
const stored = bytes => bytes.length + gzipSync(bytes).length;
const row = (f, id) => f.pool.archive.row(identity(id).key);
const status = async (f, id) => (await (await f.post('status', id)).json()).status;

/** The same quantities read independently of the service: PRAGMAs, file sizes and the stored result bytes. */
function measure(f) {
  const db = f.pool.store.sqlite;
  const pragma = name => Number(Object.values(db.prepare(`PRAGMA ${name}`).get())[0]);
  const file = path.join(f.dataDir, 'pool.sqlite');
  const size = suffix => (fs.existsSync(file + suffix) ? fs.statSync(file + suffix).size : 0);
  return {
    main: size(''), wal: size('-wal'), shm: size('-shm'),
    live: (pragma('page_count') - pragma('freelist_count')) * pragma('page_size'),
    free: pragma('freelist_count') * pragma('page_size'),
    derived: db.prepare('SELECT COALESCE(SUM(LENGTH(CAST(derived AS BLOB))),0) AS n FROM source_captures').get().n,
  };
}
async function parse(f, bytes, id) {
  assert.equal((await f.complete(bytes, {}, id)).status, 202);
  assert.equal(await f.pool.archive.tick(), true);
  assert.equal(row(f, id).status, 'parsed');
  return row(f, id).derived_bytes;
}
function appears(file) {
  return new Promise(resolve => {
    const check = () => { if (fs.existsSync(file)) { watcher.close(); resolve(); } };
    const watcher = fs.watch(path.dirname(file), check);
    check();
  });
}
/** Starts the gated fixture worker and returns once its process runs; release() lets it write the result. */
async function launch(f, id) {
  const task = f.pool.archive.tick();
  await f.pool.archive.queue.run(() => {}); // The launch step is queued synchronously by tick().
  const job = f.pool.archive.active.get(identity(id).key);
  assert.ok(job, 'job started');
  await appears(`${job.output}.started`);
  return { task, job, release: () => fs.writeFileSync(`${job.output}.go`, '') };
}

test('a derived result is counted once, in the archive budget, and is not metadata', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('v1') } });
  const bytes = big();
  const empty = f.pool.archive.metadata().bytes;
  let derived = 0;
  for (let i = 0; i < 3; i++) derived += await parse(f, bytes, capture(16 + i));
  assert.ok(derived > 3 * 150_000, 'the fixture result is large enough to tell the budgets apart');
  assert.equal(f.pool.archive.usage(), 3 * stored(bytes) + derived);
  const m = measure(f);
  assert.equal(m.derived, derived, 'derived_bytes is what SQLite stores');
  assert.ok(m.main >= derived, 'the results are physically inside pool.sqlite');
  const metadata = f.pool.archive.metadata();
  assert.equal(metadata.bytes, m.live - m.derived + m.wal + m.shm);
  assert.equal(metadata.fileBytes, m.main + m.wal + m.shm);
  assert.ok(metadata.bytes - empty <= 3 * 2 * PAGE, `three captures cost ${metadata.bytes - empty} metadata bytes`);
  // The file-size measure this replaces put every result into both budgets.
  assert.ok(metadata.fileBytes - empty >= derived);
  f.pool.archive.maxMetadataBytes = empty + 3 * 2 * PAGE;
  assert.ok(metadata.fileBytes > f.pool.archive.maxMetadataBytes);
  assert.equal((await f.post('chunks', chunk(source(), 0, capture(99)))).status, 201);
});

test('healthz shows metadata pressure apart from the archive, as numbers only', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('v1') } });
  const derived = await parse(f, big(), capture(16));
  const text = await (await fetch(`${f.pool.url}/healthz`)).text();
  const health = JSON.parse(text);
  const m = measure(f);
  assert.equal(health.archiveBytes, stored(big()) + derived);
  assert.equal(health.archiveCapBytes, 256 * MiB);
  assert.equal(health.metadataBytes, m.live - m.derived + m.wal + m.shm);
  assert.equal(health.metadataCapBytes, 64 * MiB);
  assert.equal(health.sqliteFileBytes, m.main + m.wal + m.shm);
  assert.equal(health.sqliteFreeBytes, m.free);
  assert.equal(health.sqliteWalBytes, m.wal);
  assert.equal(health.sqliteShmBytes, m.shm);
  assert.ok(health.metadataBytes < derived && health.sqliteFileBytes > derived);
  for (const secret of [ID.iid, ID.generation, capture(16).captureId, identity(capture(16)).key, identity(ID).generation]) assert.equal(text.includes(secret), false);
});

test('WAL growth alone raises metadata pressure, refuses intake and job starts, and a checkpoint returns it', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('v1') } });
  assert.equal((await f.complete(source(), {}, capture(1))).status, 202);
  await f.pool.archive.maintain();
  const before = measure(f);
  assert.equal(before.wal, 0);
  const cap = f.pool.archive.maxMetadataBytes = f.pool.archive.metadata().bytes + 16 * PAGE;
  let accepted = 0;
  let refused;
  for (let i = 2; i < 200 && !refused; i++) {
    const response = await f.post('chunks', chunk(source(), 0, capture(i)));
    if (response.status === 201) accepted++; else refused = response;
  }
  assert.ok(refused, 'intake stops');
  assert.ok(accepted > 0);
  assert.equal(refused.status, 503);
  assert.equal(refused.headers.get('retry-after'), '30');
  assert.deepEqual(await refused.json(), { ok: false, error: 'metadata_pressure' });
  assert.equal(f.pool.store.sqlite.prepare('SELECT COUNT(*) AS n FROM source_captures').get().n, accepted + 1, 'no receipt for the refused chunk');
  const full = measure(f);
  assert.equal(full.main, before.main, 'nothing reached the main file: the growth is the WAL');
  assert.ok(full.wal > 16 * PAGE);
  assert.ok(f.pool.archive.metadata().bytes > cap);
  assert.ok(f.pool.archive.usage() < MiB, 'the archive budget is nowhere near its cap');
  // Other writers share the SQLite file and stop too; deletion and reads stay available.
  const shared = await fetch(`${f.pool.url}/v1/games`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: '{}' });
  assert.equal(shared.status, 503);
  assert.equal((await shared.json()).error, 'metadata_pressure');
  const unknown = await f.post('forget', { v: 1, iid: ID.iid, generation: 'c'.repeat(32) });
  assert.equal((await unknown.json()).error, 'metadata_pressure', 'a tombstone for an unknown generation is new metadata');
  assert.equal(await f.pool.archive.tick(), false, 'no job starts under pressure');
  assert.equal(await status(f, capture(1)), 'stored', 'and the waiting capture is not failed');
  await f.pool.archive.maintain();
  const after = measure(f);
  assert.equal(after.wal, 0);
  assert.ok(after.live - before.live < full.wal, 'the receipts take fewer pages than their WAL frames did');
  assert.ok(f.pool.archive.metadata().bytes <= cap);
  assert.equal((await f.post('chunks', chunk(source(), 0, capture(999)))).status, 201);
  assert.equal(await f.pool.archive.tick(), true);
  assert.equal(await status(f, capture(1)), 'parsed');
});

test('forget stays available under metadata pressure and returns both budgets to what is kept', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('v1') } });
  const baseline = f.pool.archive.metadata().bytes;
  let derived = 0;
  for (let i = 0; i < 4; i++) derived += await parse(f, big(), capture(16 + i));
  await f.upload(source(), capture(99)); // An unfinished upload of the same generation.
  f.pool.archive.maxMetadataBytes = 0;
  assert.equal((await f.post('chunks', chunk(source(), 0, capture(100)))).status, 503);
  assert.equal((await f.post('forget', generationId)).status, 200);
  assert.equal(f.pool.archive.usage(), fs.statSync(f.pool.archive.ledger).size, 'only the revocation ledger is left in the archive budget');
  assert.deepEqual(fs.readdirSync(f.pool.archive.captureRoot), []);
  const metadata = f.pool.archive.metadata();
  const m = measure(f);
  assert.ok(m.free >= derived, 'SQLite keeps the freed pages in the file');
  assert.equal(metadata.freeBytes, m.free);
  assert.ok(metadata.fileBytes > baseline + derived);
  assert.ok(metadata.bytes - baseline <= 2 * PAGE, `after forget metadata is ${metadata.bytes - baseline} above the empty service`);
  // A cap the physical file exceeds no longer latches intake shut after the data is gone.
  f.pool.archive.maxMetadataBytes = baseline + 2 * PAGE;
  const next = { ...capture(1), generation: 'a'.repeat(32) };
  assert.equal((await f.post('chunks', chunk(source(), 0, next))).status, 201);
  assert.equal(f.pool.archive.metadata().fileBytes > f.pool.archive.maxMetadataBytes, true);
});

test('retention expiry returns the archive budget and leaves only tombstones as metadata', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('v1'), retentionDays: 1 } });
  const baseline = f.pool.archive.metadata().bytes;
  const derived = await parse(f, big(), capture(16)) + await parse(f, big(), capture(17));
  assert.equal(f.pool.archive.usage(), 2 * stored(big()) + derived);
  await f.pool.archive.maintain(Date.now() + 2 * 86_400_000);
  assert.equal(f.pool.archive.usage(), 0);
  assert.equal(await status(f, capture(16)), 'deleted');
  assert.equal(fs.existsSync(f.pool.archive.dir(identity(capture(16)))), false);
  const m = measure(f);
  assert.equal(m.derived, 0);
  assert.equal(m.wal, 0);
  assert.ok(m.free >= derived);
  assert.ok(f.pool.archive.metadata().bytes - baseline <= 2 * PAGE);
});

test('chunks of failed and unfinished uploads stay in the archive budget until they are really removed', bounded, async t => {
  const bytes = big(200);
  const one = stored(bytes);
  const f = await setup(t, { archive: { worker: workerConfig('crash'), maxWorkerOutputBytes: 1024, maxArchiveBytes: 2 * one + 1024, unfinishedHours: 1 } });
  const failed = capture(16), unfinished = capture(17), third = capture(18);
  assert.equal((await f.complete(bytes, {}, failed)).status, 202);
  await f.upload(bytes, unfinished);
  assert.equal(f.pool.archive.usage(), 2 * one);
  assert.equal(await f.pool.archive.tick(), true);
  assert.equal(row(f, failed).status, 'failed');
  assert.equal(row(f, failed).derived_bytes, 0);
  assert.equal(f.pool.archive.usage(), 2 * one, 'a failed job leaves its chunks counted and no reservation behind');
  for (const id of [failed, unfinished]) {
    assert.deepEqual(fs.readdirSync(f.pool.archive.dir(identity(id))), ['0.jsonl.gz'], 'no work file is left');
    assert.equal(fs.statSync(f.pool.archive.chunkPath(identity(id), 0)).size, gzipSync(bytes).length);
  }
  const refused = await f.post('chunks', chunk(bytes, 0, third));
  assert.equal(refused.status, 503);
  assert.equal((await refused.json()).error, 'archive_pressure');
  await f.pool.archive.maintain(Date.now() + 2 * 3_600_000);
  assert.equal(await status(f, unfinished), 'deleted');
  assert.equal(await status(f, failed), 'failed');
  assert.equal(f.pool.archive.usage(), one);
  assert.equal((await f.post('chunks', chunk(bytes, 0, third))).status, 201);
  assert.equal(f.pool.archive.usage(), 2 * one);
});

test('a running job holds its reservation against intake and its commit leaves exactly the stored bytes', bounded, async t => {
  const bytes = big();
  const small = source();
  const reserve = 256 * 1024;
  const cap = stored(bytes) + reserve + stored(small) - 1;
  const f = await setup(t, { archive: { worker: workerConfig('gated'), maxWorkerOutputBytes: reserve, maxArchiveBytes: cap } });
  const id = capture(16), other = capture(17);
  assert.equal((await f.complete(bytes, {}, id)).status, 202);
  const run = await launch(f, id);
  assert.equal(f.pool.archive.usage(), stored(bytes) + reserve);
  assert.equal(fs.statSync(run.job.input).size, bytes.length, 'the uncompressed half of the chunk reservation is the work file');
  const during = await f.post('chunks', chunk(small, 0, other));
  assert.equal(during.status, 503);
  assert.equal((await during.json()).error, 'archive_pressure');
  // Intake racing the commit: whichever the queue takes first, the budget holds and nothing is counted twice.
  run.release();
  const [raced] = await Promise.all([f.post('chunks', chunk(small, 0, other)), run.task]);
  assert.ok([201, 503].includes(raced.status));
  const derived = row(f, id).derived_bytes;
  assert.equal(row(f, id).status, 'parsed');
  assert.ok(derived > 0 && derived < reserve);
  assert.equal(f.pool.archive.usage(), stored(bytes) + derived + (raced.status === 201 ? stored(small) : 0));
  assert.ok(f.pool.archive.usage() <= cap);
  assert.equal((await f.post('chunks', chunk(small, 0, other))).status, raced.status === 201 ? 200 : 201, 'the released reservation admits it');
  assert.equal(f.pool.archive.usage(), stored(bytes) + derived + stored(small));
  assert.deepEqual(fs.readdirSync(f.pool.archive.dir(identity(id))), ['0.jsonl.gz']);
  assert.ok(measure(f).wal < derived, 'the committed result is not left in the WAL; only the later receipt is');
});

test('intake that crosses the metadata cap during a job is refused next time while the job still commits', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('gated') } });
  const id = capture(16), waiting = capture(17);
  assert.equal((await f.complete(big(), {}, id)).status, 202);
  const run = await launch(f, id);
  const cap = f.pool.archive.maxMetadataBytes = f.pool.archive.metadata().bytes;
  assert.equal((await f.complete(source(), {}, waiting)).status, 202, 'at the cap, not over it');
  assert.ok(f.pool.archive.metadata().bytes > cap);
  const refused = await f.post('chunks', chunk(source(), 0, capture(18)));
  assert.equal(refused.status, 503);
  assert.equal((await refused.json()).error, 'metadata_pressure');
  run.release();
  assert.equal(await run.task, true);
  assert.equal(row(f, id).status, 'parsed', 'the admitted job is not thrown away');
  const m = measure(f);
  assert.equal(m.derived, row(f, id).derived_bytes);
  assert.equal(f.pool.archive.metadata().bytes, m.live - m.derived + m.wal + m.shm);
  assert.equal(f.pool.archive.usage(), stored(big()) + stored(source()) + m.derived);
  f.pool.archive.maxMetadataBytes = f.pool.archive.metadata().bytes - 1;
  assert.equal(await f.pool.archive.tick(), false, 'the next job waits for the pressure to clear');
  assert.equal(row(f, waiting).status, 'stored');
});

test('forget during processing drops the reservation, the work files and the chunks from the accounting', bounded, async t => {
  const reserve = 256 * 1024;
  const f = await setup(t, { archive: { worker: workerConfig('gated'), maxWorkerOutputBytes: reserve } });
  const baseline = f.pool.archive.metadata().bytes;
  const id = capture(16);
  assert.equal((await f.complete(big(), {}, id)).status, 202);
  const run = await launch(f, id);
  assert.equal(f.pool.archive.usage(), stored(big()) + reserve);
  assert.equal((await f.post('forget', generationId)).status, 200);
  assert.equal(await run.task, true);
  assert.equal(f.pool.archive.active.size, 0);
  assert.equal(f.pool.archive.usage(), fs.statSync(f.pool.archive.ledger).size);
  assert.deepEqual(fs.readdirSync(f.pool.archive.captureRoot), []);
  assert.equal(measure(f).derived, 0);
  assert.ok(f.pool.archive.metadata().bytes - baseline <= 2 * PAGE);
});

test('metadata is never understated by a wrong byte column or driven below the journals', bounded, async t => {
  const f = await setup(t, { archive: { worker: workerConfig('v1') } });
  const id = capture(16);
  const derived = await parse(f, big(), id);
  const db = f.pool.store.sqlite;
  const set = bytes => db.prepare('UPDATE source_captures SET derived_bytes=? WHERE key=?').run(bytes, identity(id).key);
  set(derived * 1000); // The archive budget overstates; metadata takes out only what is stored.
  let m = measure(f);
  assert.equal(f.pool.archive.metadata().bytes, m.live - derived + m.wal + m.shm);
  set(0); // Bytes the archive budget does not count stay metadata.
  m = measure(f);
  assert.equal(f.pool.archive.metadata().bytes, m.live + m.wal + m.shm);
  set(derived);
  m = measure(f);
  assert.ok(m.wal > 0);
  assert.ok(f.pool.archive.metadata().bytes >= m.wal + m.shm);
  assert.equal(f.pool.archive.metadata().bytes, m.live - derived + m.wal + m.shm);
});
