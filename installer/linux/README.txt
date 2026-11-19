HEARTHPULSE FOR LINUX

Hearthpulse is a Hearthstone companion: deck tracker, Battlegrounds advisor, combat
odds, in-game overlay and match history. On Linux, Hearthstone runs through Wine,
Lutris, Bottles or Steam (Proton); Hearthpulse finds it in those prefixes and reads
the game's own log files. It never changes the game.

INSTALL
  For your user only (no password):   ./install.sh
  For every user on this computer:     sudo ./install.sh
This adds Hearthpulse to your app menu and puts a shortcut on your desktop.
Prefer a package? Use the .deb (Ubuntu, Debian, Mint) or .rpm (Fedora, openSUSE).

UNINSTALL
  hearthpulse-uninstall            (or ./uninstall.sh from this folder)
  hearthpulse-uninstall --purge    also deletes your match history and settings
Packages: sudo apt remove hearthpulse   /   sudo dnf remove hearthpulse

FIRST START
Hearthpulse turns on the log sections it needs in Hearthstone's log.config inside
the Wine prefix. Restart Hearthstone once afterwards. If Hearthstone is installed
somewhere unusual, set "HearthstonePath" in ~/.local/share/Hearthpulse/settings.json.

OVERLAY
The in-game overlay works on X11 and on Wayland desktops through XWayland (the
default for Wine games). Your rating and the lobby tribes are read from game
memory on Windows only; on Linux those values stay empty. Reading your own
card collection and Arcane Dust balance from memory is also Windows-only;
Linux does not read them.

Community stats: Hearthpulse data (api.kolodahearthstone.com; the Battlegrounds tier tabs read the site's open
statistics service, bg.hs-manacost.ru: that server sees your IP address and the time of the request, and gets no
account data, no cookie and nothing about your games). Russian archetype names: the Hearthpulse archetype
dictionary (api.blizzcore.ru). Card data and art: HearthstoneJSON. The Brawl page's article: hs-manacost.ru (its
"Потасовка" posts and cover pictures, opened in the browser).
Navigation icons from game-icons.net (CC BY 3.0, https://creativecommons.org/licenses/by/3.0/), drawn in gold:
"house" by Delapouite; "beer-stein", "poker-hand", "vine-leaf", "crossed-swords", "fist", "hourglass"
and "cog" by Lorc. Fonts and libraries: THIRD-PARTY-NOTICES.txt.
Not affiliated with Blizzard Entertainment.

GAME STATISTICS
- On by default; expanded source sending starts only after the updated in-app
  notice and a statistics endpoint are available. Turn it off in Settings >
  Data, "Send my games to Hearthpulse statistics".
- The app sends compact game records and a sanitized journal of observed games
  and Arena draft choices to Hearthpulse's private statistics service: ordered
  visible offers and choices, played/drawn cards, buys, sells, shop refreshes,
  resources, visible board states and outcomes. Incomplete observations are
  marked separately. Stored journals can be reprocessed as the parser improves.
- After a Battlegrounds game the journal also holds each of its fights as the
  combat simulator saw it at the first attack: both boards (cards, stats,
  keywords, enchantments and their values, Blood Gem buffs, the card that
  created each minion), minions that died before the first attack, heroes,
  hero powers and trinkets with their values, your player enchantments, the
  lobby's tribes, the order of the sides (which entered play first, no seat
  numbers), tavern tier, health and player counters such as Blood Gem values of
  both sides, the fight step by step (attacks, damage, deaths, summons, stat
  changes and effects on the boards), the odds shown, the result and damage.
  For the opponent this includes what the game may not show: the card that
  created a minion, its enchantments and their values, his counters. Never your
  hand or the cards it gets. It is used only to measure the simulator: not
  published, never joined with site profiles or other players' data.
- No name, BattleTag, account, deck name, exact rating, private network log or
  hidden opponent cards (except the fight inputs above). Source identities
  are random per installation and generation, separate from the website
  account; only aggregate statistics are published, including in paid
  features. Storage is up to 180 days.
- Turning sending off clears pending journals and asks the server to delete
  uploaded data. Pending deletion remains visible until the server confirms it.
- Shared games for the public statistics pool, also on by default after the
  same in-app notice: after a Battlegrounds game, the heroes offered and
  picked, placement, rating rounded to 500, lobby tribes and anomaly, final
  board, trinkets, fights, your picks and the cards you played (your
  Battlegrounds games of the last 30 days once as well); after a Ranked or
  Casual game, the cards a Discover-style card offered and the one you took.
  No name, BattleTag, account, deck or anything about other players. The
  totals are public. Turn it off in Settings > Data, "Share my games with the
  stats pool", or with "Turn off" on the notice: nothing more is sent. Shared
  games cannot be deleted on request; they expire after 60 days.
- The expanded source service is a synthetic pilot in this build; its default
  endpoint is unset. Public activation requires the matching published privacy
  policy and verified hosting/backup deletion procedures.

CRASH REPORTS
- Off by default. A report of an unexpected error goes to hearthpulse.net only
  when you press "Send report" in the error window, or when you turn on
  "Send crash reports automatically" in Settings > Data (turning it off deletes
  reports waiting to go).
- A report holds the app, system and .NET versions, the interface language,
  the error type and its text, and where in the code it happened. Names,
  BattleTags, e-mail addresses, paths, quoted values and long numbers are cut
  out of the text before it is sent. It is not tied to your account, and your
  IP address is not kept. Reports are kept for 90 days.
