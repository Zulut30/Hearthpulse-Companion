# Generated pool spot checks

`cards-256589.json` - eight Battlegrounds pool minions as HearthstoneJSON enUS build 256589 (patch 37.0.2, data of
2026-11-03) prints them, fields cut to what the check reads. `GeneratedPoolsTests` compares the Ghastcoiler and Undead
pools (`GhastcoilerPool.g.cs`, `UndeadPool.g.cs`) with them: cards whose stats changed server-side after 37.0.0.255130
(Sacrificial Wrathguard, Ship Master Eudora, Champion of Sargeras, The Last One Standing), Rot Hide Gnoll (back in the
pool), two that both pools hold, and one that neither does.

Made from a snapshot of `tools/effects/fetch_bg_data.py` (2026-11-19):

```
python3 -I -c "import json,sys; c={x['id']:x for x in json.load(open(sys.argv[1],encoding='utf-8'))}; keep=('id','dbfId','name','type','set','attack','health','techLevel','races','mechanics','text','isBattlegroundsPoolMinion','battlegroundsPremiumDbfId'); print(json.dumps([{k:c[i][k] for k in keep if k in c[i]} for i in sys.argv[2:]],indent=1,ensure_ascii=False))" <snapshot>/cards.enUS.json BG36_362 BG33_828 BG27_016 BG34_320 BG25_013 BG28_300 BG25_022 BG31_810
```

After a patch: regenerate the pools, then rebuild this file from the new snapshot with the same command.
