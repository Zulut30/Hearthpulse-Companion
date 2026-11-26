# The game client is only read: Power.log first, memory reads for what the log lacks, never writes or input

Game state comes from Hearthstone's Power.log. Values the log does not carry (ranks, the Arena draft offer,
Battlegrounds lobby names for the MMR option, button costs, and since 2026-11-16 the player's own card collection and
arcane dust balance, and since 2026-11-19 its card backs, coins and achievements (catalog and progress), all asked
for only outside games and kept on the machine only: [collection.md](../collection.md),
[collection-cosmetics.md](../collection-cosmetics.md)) are read from
the client's memory through a handle opened
for `PROCESS_VM_READ | PROCESS_QUERY_LIMITED_INFORMATION` only (`src/Hearthpulse.Core/Memory/ProcessMemory.cs`). The
app never writes to the client's memory, injects code, hooks the client or sends input to it: any of those would put
the app among the tools that risk players' accounts.

## Consequences

- A change that touches game memory, input or adds a new read goes through the `hearthpulse-legal` check before merge.
- Memory values are optional: when the client moves its fields, the read returns null and the feature degrades; the
  tracker never depends on memory for game state the log has.
- A memory read is still subject to [0005](0005-hidden-information.md): reading a value does not make it showable.
- The player can turn every read off (2026-11-26): Settings → Data → "Read data from the game client"
  (`AppSettings.ReadGameMemory`, on by default; `GameSession.SetGameMemoryEnabled`). Off, the memory reader gets no
  request and closes its handle to the game, the card under the mouse is not read, and the pages that show a value read
  from the client (collection, achievements, the Battlegrounds rating and lobby) say that reading is off and where to
  turn it on; the tracking from the logs is unchanged.
