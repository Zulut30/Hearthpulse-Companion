# BgMeta fixtures

Real answers of `https://api.kolodahearthstone.com`, one plain `GET` per address, cut down.

| File | Address | What was cut |
|---|---|---|
| `heroes-<mmr>.json` | `/api/bg/heroes?mode=solo&mmr=<MMR>` (2026-10-22) | A few heroes per slice. |
| `minions-<mmr>.json` | `/v1/battlegrounds/minions/stats?mmr=<MMR>&period=CURRENT_BATTLEGROUNDS_PATCH&limit=500` (2026-10-22) | A few minions per slice. |
| `hero-59807.json`, `hero-60366.json` | `/api/bg/heroes/59807` (Lord Jaraxxus) and `/api/bg/heroes/60366` (Millificent Manastorm), 2026-11-15 | `hero.key_minions_top3` emptied; `tavern_up`, `tavern_up_by_turn`, `hero_power`, `hero_power_by_turn`, `combat_winrate` cut to their first row; `lineup` and `final_form_minions` of every composition emptied. The compositions' ids, names, games and places are as sent. |

The hero detail files were cut by this script (`python cut.py <answer.json> <fixture.json>`):

```python
import json, sys
d = json.load(open(sys.argv[1], encoding="utf-8"))
d["hero"]["key_minions_top3"] = []
for key in ("tavern_up", "tavern_up_by_turn", "hero_power", "hero_power_by_turn", "combat_winrate"):
    d[key] = d.get(key, [])[:1]
for comp in d.get("compositions", []) + [d.get("best_composition") or {}]:
    if comp:
        comp["lineup"] = []
        comp["final_form_minions"] = []
json.dump(d, open(sys.argv[2], "w", encoding="utf-8", newline="\n"), ensure_ascii=False, indent=1)
```
