// Owner-only export of the stored Battlegrounds fights (bg_combat, migration 0010) as JSONL for
// `Hearthpulse.Bench simcover --pool <file>`. Runs on the pool host against the loopback service with the operator
// token, the same gate as reprocess; the token is read from POOL_OPERATOR_TOKEN, never from the command line.
//
//   POOL_OPERATOR_TOKEN=... node server/pool/node/export-combats.mjs --out fights.jsonl [--url http://127.0.0.1:3181]
//
// Each line: {"v":1,"game":"<16 hex>","day":<days since 1970>,"build":<client build|null>,"mode":"bg|duos","fight":{...}}
// with the fight exactly as validated on upload (docs/source-capture.md). Prints counts only.
import * as fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const LOOPBACK = ['127.0.0.1', '[::1]', 'localhost'];

export function parseArgs(argv) {
  const args = { url: 'http://127.0.0.1:3181', out: '', page: 200 };
  for (let i = 0; i < argv.length; i += 2) {
    const [name, value] = [argv[i], argv[i + 1]];
    if (value === undefined) throw new Error('usage');
    if (name === '--url') args.url = value;
    else if (name === '--out') args.out = value;
    else if (name === '--page') args.page = Number(value);
    else throw new Error('usage');
  }
  const url = new URL(args.url);
  // The operator token never leaves the host: the service answers the export on loopback only anyway.
  if (url.protocol !== 'http:' || !LOOPBACK.includes(url.hostname) || !args.out || !Number.isSafeInteger(args.page) || args.page < 1 || args.page > 200)
    throw new Error('usage');
  return args;
}

/** Pages through the export into `out` (written whole under a temporary name); returns the number of fights. */
export async function exportCombats({ url, out, page = 200, token, fetchImpl = fetch }) {
  if (!token) throw new Error('operator_token_missing');
  const temp = `${out}.${process.pid}.tmp`;
  const fd = fs.openSync(temp, 'w', 0o600);
  let fights = 0;
  try {
    let after = 0;
    for (;;) {
      const response = await fetchImpl(new URL('/v1/captures/combats/export', url), {
        method: 'POST', headers: { 'content-type': 'application/json', authorization: `Bearer ${token}` },
        body: JSON.stringify({ v: 1, after, limit: page }),
      });
      if (response.status !== 200) throw new Error(`export_failed_${response.status}`);
      const body = await response.json();
      if (body?.ok !== true || !Array.isArray(body.fights) || !(body.next === null || Number.isSafeInteger(body.next))) throw new Error('export_invalid_answer');
      for (const line of body.fights) fs.writeSync(fd, `${JSON.stringify(line)}\n`);
      fights += body.fights.length;
      if (body.next === null) break;
      after = body.next;
    }
    fs.fsyncSync(fd);
  } catch (e) {
    fs.closeSync(fd);
    fs.rmSync(temp, { force: true });
    throw e;
  }
  fs.closeSync(fd);
  fs.renameSync(temp, out);
  return fights;
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  try {
    const args = parseArgs(process.argv.slice(2));
    const fights = await exportCombats({ ...args, token: process.env.POOL_OPERATOR_TOKEN ?? '' });
    console.log(JSON.stringify({ ok: true, fights }));
  } catch (e) {
    console.error(JSON.stringify({ ok: false, error: String(e?.message ?? e).slice(0, 80) }));
    process.exitCode = 1;
  }
}
