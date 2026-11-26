HEARTHPULSE FOR MAC

Hearthpulse is a Hearthstone companion: deck tracker, Battlegrounds advisor, combat
odds, in-game overlay and match history. It reads the log files Hearthstone writes
in /Applications/Hearthstone/Logs and never changes the game.

LICENCE
Hearthpulse is proprietary software. Installing or using it means you accept the
licence agreement in LICENSE.txt, which comes with the app.

INSTALL
Double-click "Install Hearthpulse". It copies Hearthpulse to Applications, puts a
shortcut on your Desktop and starts it.
If macOS says the file "cannot be opened": right-click it, choose Open, then Open
again. Hearthpulse is not notarized by Apple, so macOS asks once.
(From the .dmg you can also drag Hearthpulse into Applications; from the .pkg just
follow the setup wizard.)

UNINSTALL
Double-click "Uninstall Hearthpulse" (also kept in
~/Library/Application Support/Hearthpulse). It asks whether to keep your match history.

FIRST START
Hearthpulse turns on the log sections it needs in
~/Library/Preferences/Blizzard/Hearthstone/log.config. Restart Hearthstone once
afterwards. The overlay floats over the game, including in full screen.
Hearthpulse reads values from the game's memory on Windows only; on Mac it reads
nothing from memory. Your rating, the lobby tribes, your card collection and Arcane
Dust balance, card backs, coins and achievements therefore stay empty, and the
Arena draft helper does not see the cards you are offered.

Community stats: Hearthpulse data (api.kolodahearthstone.com; the Battlegrounds tier tabs read the site's open
statistics service, bg.hs-manacost.ru: that server sees your IP address and the time of the request, and gets no
account data, no cookie and nothing about your games). Russian archetype names: the Hearthpulse archetype
dictionary (api.blizzcore.ru). Card data and art: HearthstoneJSON. Battlegrounds card details and pictures
HearthstoneJSON lacks: hsbg.cards. The Brawl page's article: hs-manacost.ru (its "Потасовка" posts and cover
pictures, opened in the browser).
Navigation icons from game-icons.net (CC BY 3.0, https://creativecommons.org/licenses/by/3.0/), drawn in gold:
"house", "trophy-cup" and "present" by Delapouite; "beer-stein", "poker-hand", "vine-leaf", "crossed-swords",
"fist", "hourglass", "open-book" and "cog" by Lorc. Fonts and libraries: THIRD-PARTY-NOTICES.txt.
Hearthpulse is not affiliated with or endorsed by Blizzard Entertainment. Use it at your own risk.

GAME STATISTICS
- Both kinds of sending below are on by default: the app asks no question
  and shows no notice first. They go to Hearthpulse's statistics service
  (api.kolodahearthstone.com/stats). Each has its own switch in Settings >
  Data; once a switch is off, nothing more of its kind is sent. Privacy
  policy: https://hearthpulse.net/privacy/ (in Russian)
- "Send my games to Hearthpulse statistics": compact game records and a
  sanitized journal of observed games and Arena draft choices: ordered
  visible offers and choices, played/drawn cards, buys, sells, shop
  refreshes, resources, visible board states and outcomes. Incomplete
  observations are marked separately. Stored journals can be reprocessed as
  the parser improves. Game records also hold the game's build number, the
  "Diamond 4-1" mark for Ranked games at Diamond 4 to 1 and the main tribe
  of the final Battlegrounds board; when an Arena run ends, its result is
  sent too: class, wins, losses, Arena kind (normal or Underground) and
  final deck.
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
- Turning this switch off clears the records and journals waiting to be
  sent and asks the server to delete the ones this installation sent.
  Settings > Data shows the deletion as pending until the server confirms
  it.
- "Share my games with the stats pool": games for the public statistics
  pool. After a Battlegrounds game, solo or duos, the heroes offered and
  picked, placement, rating rounded to 500, lobby tribes and anomaly, final
  board, trinkets, the turns you upgraded the tavern, each fight's result,
  damage and odds, your picks and the cards you played (your Battlegrounds
  games of the last 30 days once as well); after a Ranked or Casual game,
  the cards each Discover-style card offered, the one you took, the format
  and whether you won. No name, BattleTag, account, deck or anything about
  other players. The totals are public. Turning this switch off stops all
  further sending. Shared games cannot be deleted on request; they expire
  after 60 days.

HEARTHPULSE.NET ACCOUNT
- Only if you sign in with a hearthpulse.net account: your Standard, Wild,
  Arena and Battlegrounds games then also go to your profile on the site
  after they end (mode, start time and length, turns, result, place or
  rank, rating, heroes and classes, your deck name, code and archetype, the
  Battlegrounds lobby's tribes and anomaly, tavern tier, final board and
  fights, the heroes you were offered, who went first, your Arena score and
  the opponent's archetype once their cards showed it). Never your
  BattleTag, your opponent's name or anything hidden in the game. On by
  default while you are signed in; turn it off in Settings > Account, "Send
  games to my profile". On the site's account page you can see, download or
  delete what the app sent.
- A replay goes to the site only when you upload it from History and
  confirm. It is kept there for 180 days; delete it in History or on the
  account page.

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
