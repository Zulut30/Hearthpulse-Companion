-- Battlegrounds fights from source v3 bg_combat records (TASK-2026-11-19 U, docs/source-capture.md "Battlegrounds
-- fights"). One row per fight of a committed capture, written in the manifest's transaction from the validated source.
-- Additive and idempotent. Retention follows the capture: a row goes with its capture when that expires (the archive's
-- retention, 180 days by default), with its generation on forget, and with the capture when it is deleted; nothing is
-- published from it. Read only by the operator export (/v1/captures/combats/export, node/export-combats.mjs).
CREATE TABLE IF NOT EXISTS bg_combats (
  id INTEGER PRIMARY KEY,
  capture TEXT NOT NULL,
  generation TEXT NOT NULL,
  fight INTEGER NOT NULL,
  day INTEGER NOT NULL,
  build INTEGER,
  mode TEXT NOT NULL,
  created INTEGER NOT NULL,
  -- The validated bg_combat data object, as JSON; at most 56 KiB.
  body TEXT NOT NULL,
  UNIQUE (capture, fight)
);
CREATE INDEX IF NOT EXISTS bg_combats_by_generation ON bg_combats(generation);
