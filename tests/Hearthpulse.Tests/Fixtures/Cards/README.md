# Card list fixtures

Cut-down answers of the data API's stored card lists (docs/card-page.md), read on 2026-11-16 with plain GET requests:

| File | Request | Kept |
|---|---|---|
| `cards-standard.json` | `GET https://api.kolodahearthstone.com/datasets/hsreplay_cards_legend_included_winrate` | the six most played cards, and the first card of each odd shape: played 0 times (`CATA_820`, no `winrate_when_played`), no `keep_percentage` (`TIME_020`), `cardClass: null` (`END_011`) |
| `cards-wild.json` | `GET https://api.kolodahearthstone.com/datasets/hsreplay_cards_wild_legend_14d` | the five most played cards |

`…_included_winrate` holds the same rows as `…_included_popularity`, the list the app asks for; the tests route the
fixture to that path. The envelope is the server's (`source_id`, `fetched_at`, `data`, `publication`), with the
collector's run details (`backend`, `runtime`, `data.structured.source.diagnostics`) dropped; both copies of the list
(`data.structured.cards`, `data.hsreplay_extracted.cards`) hold the kept rows, unchanged. The numbers the tests
assert are read from these rows.
