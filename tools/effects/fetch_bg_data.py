#!/usr/bin/env python3
"""Downloads the card data the Battlegrounds effect tables are generated from into one snapshot folder.

Sources (both public, read-only; no account, no personal data in the requests):
  HearthstoneJSON  https://api.hearthstonejson.com/v1/<build>/enUS/cards.json  - every card of the patch (text,
                   mechanics, golden links, hero powers). The newest build in the index is used unless --build is given.
  Hearthpulse data API v1  https://api.kolodahearthstone.com/api/v1/...  - the Battlegrounds libraries with the
                   current pool flag `in_pool`: cards (minions and Tavern spells, `duos_only`), heroes, trinkets,
                   anomalies, quests, quest rewards, Dark Gifts and Timewarped cards.

Writes into <out>:
  cards.enUS.json            HearthstoneJSON as served
  api-<library>.json         every page of the library, merged into {"data": [...]}
  api-minions-seen.json      minions seen on boards this patch (v1/battlegrounds/minions/stats, all ranks): tokens and
                             generated minions too
  snapshot.json              {"build", "patch", "hsjson_date", "fetched_at", "counts"}: what the generated headers record

Usage (isolated interpreter; the folder is data, never run anything from it):
  python3 -I tools/effects/fetch_bg_data.py <out-folder> [--build 256589] [--patch 37.0.2.256589]
Then run the generators with --data <out-folder> (see each script's header).
"""
import argparse
import datetime
import email.utils
import json
import pathlib
import re
import sys
import urllib.request

HSJSON = "https://api.hearthstonejson.com/v1/"
API = "https://api.kolodahearthstone.com/api/v1/"
LIBRARIES = ["cards", "heroes", "trinkets", "anomalies", "quests", "rewards", "dark-gifts", "timewarped-cards"]
AGENT = "Hearthpulse-dev-tools"  # generic: no user or machine detail


def get(url):
    request = urllib.request.Request(url, headers={"User-Agent": AGENT, "Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read(), response.headers


def newest_build():
    index, _ = get(HSJSON)
    builds = sorted(int(b) for b in re.findall(r">(\d{5,7})/?<", index.decode("utf-8", "replace")))
    if not builds:
        sys.exit("no build in the HearthstoneJSON index")
    return builds[-1]


def patch_of(build):
    """The newest patch the API lists (major.minor.fix) with the data build: "37.0.2" + 256589 -> "37.0.2.256589".
    The API names patches, not data builds; check the printed pair against the patch notes (or pass --patch)."""
    body, _ = get(API.replace("/api/v1/", "/api/patches?limit=1"))
    version = json.loads(body)["patches"][0]["version"]
    return ".".join(version.split(".")[:3] + [str(build)])


def library(name):
    items, page = [], 1
    while True:
        body, _ = get(f"{API}{name}?per_page=100&page={page}")
        answer = json.loads(body)
        items += answer["data"]
        paging = answer.get("pagination") or {}
        if not paging.get("has_next"):
            return items
        page += 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("out")
    ap.add_argument("--build", type=int)
    ap.add_argument("--patch", help="patch label for the tables, e.g. 37.0.2.256589 (default: newest API patch + build)")
    args = ap.parse_args()
    out = pathlib.Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    build = args.build or newest_build()
    cards, headers = get(f"{HSJSON}{build}/enUS/cards.json")
    (out / "cards.enUS.json").write_bytes(cards)
    counts = {"hearthstonejson": len(json.loads(cards))}
    for name in LIBRARIES:
        items = library(name)
        counts[name] = len(items)
        (out / f"api-{name}.json").write_text(json.dumps({"data": items}, ensure_ascii=False), encoding="utf-8")
    # Minions seen on boards this patch (community stats, all ranks; the app's cache/api/bg-minions-all.json): it also
    # holds tokens and generated minions (Water Droplet, Tide Oracle Morgl) that no pool flag names.
    body, _ = get(API.replace("/api/v1/", "/v1/battlegrounds/minions/stats?mmr=ALL&period=CURRENT_BATTLEGROUNDS_PATCH&limit=500"))
    seen = json.loads(body)["data"]
    counts["minions-seen"] = len(seen)
    (out / "api-minions-seen.json").write_text(json.dumps({"data": seen}, ensure_ascii=False), encoding="utf-8")

    modified = headers.get("Last-Modified", "")
    snapshot = {
        "build": build,
        "patch": args.patch or patch_of(build),
        "hsjson_last_modified": modified,
        "hsjson_date": email.utils.parsedate_to_datetime(modified).date().isoformat() if modified else "",
        "fetched_at": datetime.date.today().isoformat(),
        "counts": counts,
    }
    (out / "snapshot.json").write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
    print(json.dumps(snapshot, indent=2))


if __name__ == "__main__":
    main()
