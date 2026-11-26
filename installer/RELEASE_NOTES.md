## Hearthpulse 0.16.0

### What's new
- **Licence.** From this release Hearthpulse comes with an end-user licence agreement: https://github.com/Zulut30/Hearthpulse-Companion/blob/main/LICENSE (also LICENSE.txt next to the app and Settings → About → Licence agreement). It applies to 0.16.0 and later, also when the app updates itself. If you do not agree, turn off automatic updates and keep 0.15.0, or uninstall Hearthpulse.
- **Collection, Achievements and Recap.** New pages: sets and dust, hero skins, card backs and coins, your achievements and a recap of your month or year. On Windows they are read from the running game and stay on your PC.
- **More of Battlegrounds in the combat odds.** Spells, hero powers, trinkets, quests, buddies, Secrets, Timewarped minions and anomalies now act in simulated fights; Duos fights include your partner's board.
- **Your data.** Game statistics stay on by default, now without an in-app notice. Settings → Data has a switch for each kind of sending and lists every field; turning game records off asks the server to delete what was sent (Settings → Data shows when it is done). Battlegrounds fights are sent too, only to measure the simulator. New: "Read data from the game client" (Settings → Data) stops every read of the game's memory. Privacy policy: hearthpulse.net/privacy (in Russian)
- **Overlay.** Turn timers, the end-of-game toast and the Battlegrounds lobby list are now off by default; turn them on in Settings → Overlay.

### More
- Pages and tabs cross-fade, a theme changes without the window blinking, and content stays in place while it loads.
- The match page has "View in browser": it opens your game on hearthpulse.net and, when the game is not there yet, asks first and uploads it. A replay uploaded from the library opens its page too.
- Cleaner screens: overlay panels fit the screen at a large overlay size, counters sit lower, under the opponent's panel, and empty tiers are gone from the tier lists.

### Faster
- Smaller updates: from 0.15.0 the installed Windows app downloads only the changed files.
- The combat simulator runs fights on several threads and warms up when a Battlegrounds game starts.
- The Arena, Tavern Brawl and Settings pages are built when you first open them; Settings shows its open tab first and builds the others in the background.
- Themes apply faster, and card art already in memory is not decoded again.

### Fixes
- Tracking no longer mixes up you and your opponent when the opponent's Start of Game card is shown from the deck.
- Battlegrounds: combat rules corrected from real fights.
- Achievements: when the page cannot be put together, it shows an error with "Try again" instead of loading forever.
- Replays: replay cards and the match header show the class hero and no format for Arena games; imported replays keep the order of Bob's offers.
- Overlay: trinket badges show their update delay in full, and counter names wrap.
- Windows app: the tray menu takes the theme's colours, and rounded shapes keep their corners at any size.
- Names cut short in tier lists, and on the deck page of the macOS and Linux app, show in full in a tooltip; in a narrow window that app gives hero names two lines.
- Many smaller layout fixes in both apps.

### Known issues
- The Windows setup and app have no Authenticode signature, so Windows SmartScreen may warn when you download the setup by hand. Choose "More info" → "Run anyway".
- Only an update from 0.15.0 downloads just the changed files; an update from 0.14.0 or older uses the full setup.
- macOS packages are not notarized: open the app with right-click → Open the first time.
- Linux packages do not update themselves: download the new package and install it as before.
- On macOS and Linux the app reads nothing from the game's memory: rating, collection, card backs, coins and achievements stay empty.
- When a board has a card whose effect the simulator does not model yet, the odds are approximate and say how many effects were not simulated; odds marked "≈" rest on estimates.
- The Arena draft helper is still a beta and off by default.

### Other
- Data: Hearthpulse data, HearthstoneJSON (card data and art), hsbg.cards (Battlegrounds card details and pictures HearthstoneJSON lacks), Blizzard's public Battlegrounds leaderboard (opponents' MMR, off by default), bg.hs-manacost.ru (the Battlegrounds tier tabs), hs-manacost.ru (the Brawl article), icons from game-icons.net (CC BY 3.0). Keyboard and focus behaviours are ported from UI SDK (Apache 2.0) and the update signature check from RFC 8032. Every data source is named in the setup's notice (README.txt on macOS and Linux); fonts and third-party code are in THIRD-PARTY-NOTICES.txt next to the app.
- Hearthpulse is proprietary software (All rights reserved).
- Hearthpulse is not affiliated with or endorsed by Blizzard Entertainment. Hearthstone is a trademark of Blizzard Entertainment, Inc. Use it at your own risk.
