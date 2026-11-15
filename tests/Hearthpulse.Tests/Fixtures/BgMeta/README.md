# BgMeta fixtures

Real answers of `https://api.kolodahearthstone.com`, one plain `GET` per address, cut down.

| File | Address | What was cut |
|---|---|---|
| `heroes-<mmr>.json` | `/api/bg/heroes?mode=solo&mmr=<MMR>` (2026-10-22) | A few heroes per slice. |
| `minions-<mmr>.json` | `/v1/battlegrounds/minions/stats?mmr=<MMR>&period=CURRENT_BATTLEGROUNDS_PATCH&limit=500` (2026-10-22) | A few minions per slice. |
| `hero-compositions-top_50_percent.json` | `/v1/battlegrounds/heroes/compositions?mmr=TOP_50_PERCENT` (2026-11-15) | The four heroes of `heroes-top_50_percent.json` (dbfIds 59807, 63602, 60366, 68129) of the 116 sent; `meta.count` set to 4. Their rows are as sent, `placement_distribution` included. |
