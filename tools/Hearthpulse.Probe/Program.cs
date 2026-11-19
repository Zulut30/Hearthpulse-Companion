using Hearthpulse.Core.Memory;
using Hearthpulse.Core.Power;

// Developer probe.
//   heroes <Power.log> [maxLines]   dump Battlegrounds hero entities at a point in the log
//   mem                             read live values from a running Hearthstone client
//   hover [seconds]                 print the hovered card id (and BG leaderboard hero) every 250 ms
//   class <Name> [field...]         dump a class's fields / follow a static path
//   lobby                           Battlegrounds lobby names by player id (opponent MMR, docs/bg-opponent-mmr.md)
//   collection [samples] [file]     the collection and dust, read twice and compared; saved to file when given (docs/collection.md)
//   walk <root> [segment...]        print a client object reached from a net cache object, service or static field
//   cosmetics [file]                card backs, coins and achievements with the player's state, saved to file when given
return args.FirstOrDefault() switch
{
    "heroes" => Heroes(args[1], args.Length > 2 ? int.Parse(args[2]) : int.MaxValue),
    "mem" => Memory(),
    "lobby" => Lobby(),
    "hover" => Hover(args.Length > 1 ? int.Parse(args[1]) : 30),
    "class" => ClassDump(args[1], args.Skip(2).ToArray()),
    "upgrade" => Upgrade(),
    "walk" => Hearthpulse.Probe.Walk.Run(args[1..]),
    "cosmetics" => Hearthpulse.Probe.Cosmetics.Run(args[1..]),
    "collection" => Collection(args.Length > 1 ? int.Parse(args[1]) : 5, args.Length > 2 ? args[2] : null),
    _ => Usage(),
};

static int Usage()
{
    Console.WriteLine("usage: heroes <Power.log> [maxLines] | mem | hover [seconds] | upgrade | lobby | collection [samples] [file] | class <Name> [path...]");
    return 1;
}

// The collection as the app reads it (HearthstoneMemory.GetCollection), then the same stacks walked a second time
// field by field through the generic object reader, and the two compared (docs/collection.md). Printed to this
// console only.
static int Collection(int samples, string? saveTo)
{
    using var memory = new HearthstoneMemory();
    if (!memory.EnsureAttached() || memory.Runtime is not { } runtime)
    {
        Console.WriteLine("Not attached: " + memory.Status);
        return 2;
    }

    var started = System.Diagnostics.Stopwatch.GetTimestamp();
    var collection = memory.GetCollection();
    var took = System.Diagnostics.Stopwatch.GetElapsedTime(started);
    if (collection is null)
    {
        Console.WriteLine("No collection (not loaded yet, or NetCacheCollection / NetCacheArcaneDustBalance moved)");
        return 3;
    }

    Console.WriteLine($"read in {took.TotalMilliseconds:N0} ms: {collection.Cards.Count} cards, dust {collection.Dust}");
    if (saveTo is not null)
    {
        // A file of the developer's choosing, outside the repository: for measurements on deck lists (docs/collection.md).
        new Hearthpulse.Core.Collection.CollectionStore(saveTo).Save(collection);
    }

    Console.WriteLine($"copies: normal {collection.Cards.Values.Sum(c => c.Normal)}, golden {collection.Cards.Values.Sum(c => c.Golden)}, other {collection.Cards.Values.Sum(c => c.Other)}");
    Console.WriteLine($"core set ids: {collection.Cards.Keys.Count(id => id.StartsWith("CORE_", StringComparison.Ordinal))}");

    var cache = HearthstoneMemory.FindNetCacheObject(runtime, "NetCacheCollection");
    var list = cache["<Stacks>k__BackingField"];
    Console.WriteLine($"stacks: {list.ClassName}, _size {list.Int("_size")}, TotalCardsOwned {cache.Int("TotalCardsOwned")}");
    var again = new Dictionary<string, (int Normal, int Golden, int Other)>(StringComparer.Ordinal);
    var finishes = new SortedDictionary<int, int>();
    string? stackClass = null, defClass = null;
    foreach (var stack in list.ListItems())
    {
        var def = stack["<Def>k__BackingField"];
        stackClass ??= stack.Class?.FullName;
        defClass ??= def.Class?.FullName;
        var id = def.String("<Name>k__BackingField") ?? "?";
        var premium = def.Int("<Premium>k__BackingField");
        var count = stack.Int("<Count>k__BackingField");
        finishes[premium] = finishes.GetValueOrDefault(premium) + 1;
        if (count == 0) continue;
        var owned = again.GetValueOrDefault(id);
        again[id] = premium switch
        {
            0 => owned with { Normal = owned.Normal + count },
            1 => owned with { Golden = owned.Golden + count },
            _ => owned with { Other = owned.Other + count },
        };
    }

    Console.WriteLine($"stack class {stackClass}, definition class {defClass}");
    Console.WriteLine("stacks by Premium value: " + string.Join(", ", finishes.Select(f => $"{f.Key}: {f.Value}")));
    var dustAgain = HearthstoneMemory.FindNetCacheObject(runtime, "NetCacheArcaneDustBalance").Long("<Balance>k__BackingField");
    var different = collection.Cards.Count(c => again.GetValueOrDefault(c.Key) != (c.Value.Normal, c.Value.Golden, c.Value.Other))
                    + again.Keys.Count(id => !collection.Cards.ContainsKey(id));
    Console.WriteLine($"second walk: {again.Count} cards, dust {dustAgain}, cards that differ: {different}, dust {(dustAgain == collection.Dust ? "same" : "DIFFERENT")}");
    foreach (var (id, copies) in collection.Cards.OrderBy(_ => Random.Shared.Next()).Take(samples))
    {
        var second = again.GetValueOrDefault(id);
        Console.WriteLine($"  {id,-24} {copies.Normal}/{copies.Golden}/{copies.Other}  second walk {second.Normal}/{second.Golden}/{second.Other}");
    }

    return different == 0 && dustAgain == collection.Dust ? 0 : 4;
}

static int Lobby()
{
    using var memory = new HearthstoneMemory();
    if (!memory.EnsureAttached())
    {
        Console.WriteLine("Not attached: " + memory.Status);
        return 2;
    }

    // Printed to this console only: the app never writes these names anywhere.
    var names = memory.GetBattlegroundsLobbyNames();
    if (names is null)
    {
        Console.WriteLine("No lobby names (not in a Battlegrounds game, or GameState.m_playerInfoMap / m_name moved)");
        return 3;
    }

    foreach (var (playerId, name) in names.OrderBy(p => p.Key))
    {
        Console.WriteLine($"pid={playerId} name={name}");
    }

    return 0;
}

static int Heroes(string path, int maxLines)
{
    var parser = new PowerParser(DateOnly.FromDateTime(DateTime.Today));
    var count = 0;
    foreach (var line in File.ReadLines(path))
    {
        if (++count > maxLines) break;
        parser.Feed(line);
    }

    parser.Flush();
    var game = parser.Game!;
    Console.WriteLine($"{game.GameType} turn={game.Turn} local={game.LocalPlayerId} opp={game.OpponentPlayerId}");
    foreach (var e in game.Entities.Values.Where(e => e.IsHero).OrderBy(e => e.GetInt(GameTag.PlayerId)).ThenBy(e => e.Id))
    {
        Console.WriteLine($"pid={e.GetInt(GameTag.PlayerId),-2} #{e.Id,-5} {e.Zone,-15} c={e.Controller,-2} {e.CardId,-24} hp={e.GetInt(GameTag.Health)} dmg={e.GetInt(GameTag.Damage)} arm={e.GetInt(GameTag.Armor)} place={e.GetInt(GameTag.PlayerLeaderboardPlace)} tier={e.GetInt(GameTag.PlayerTechLevel)} created=T{e.CreatedTurn}");
    }

    return 0;
}

static int Memory()
{
    using var memory = new HearthstoneMemory();
    if (!memory.EnsureAttached())
    {
        Console.WriteLine("Not attached: " + memory.Status);
        return 2;
    }

    Console.WriteLine(memory.Status);
    Console.WriteLine($"BG rating: {memory.GetBattlegroundsRating()}");
    Console.WriteLine($"Post-game rating: {memory.GetPostGameBattlegroundsRating()}");
    var races = memory.GetBattlegroundsRaces();
    Console.WriteLine($"BG races: {(races is null ? "n/a" : string.Join(",", races))}");
    Console.WriteLine($"BG upgrade cost: {memory.GetTavernUpgradeCost()?.ToString() ?? "n/a"}");
    Console.WriteLine($"BG buttons: {memory.GetBattlegroundsButtonCosts()?.ToString() ?? "n/a"}");
    var players = memory.GetMatchPlayers();
    Console.WriteLine($"Match: local={players?.Local} opponent={players?.Opponent}");
    return 0;
}

static int Hover(int seconds)
{
    using var memory = new HearthstoneMemory();
    if (!memory.EnsureAttached())
    {
        Console.WriteLine("Not attached: " + memory.Status);
        return 2;
    }

    // Every 250 ms: the card under the mouse (hand/board/hero...) and the hovered BG leaderboard hero.
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < end)
    {
        var card = memory.GetHoveredCardId();
        var hero = memory.GetHoveredLeaderboardHero();
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} card={card ?? "-"} leaderboard={hero ?? "-"}");
        Thread.Sleep(250);
    }

    return 0;
}

static int Upgrade()
{
    using var memory = new HearthstoneMemory();
    if (!memory.EnsureAttached() || memory.Runtime is not { } runtime)
    {
        Console.WriteLine("Not attached: " + memory.Status);
        return 2;
    }

    var zoneMgr = runtime.FindClass("ZoneMgr")?.GetStatic("s_instance") ?? default;
    if (zoneMgr.IsNull)
    {
        Console.WriteLine("ZoneMgr not available");
        return 3;
    }

    foreach (var zone in zoneMgr["m_zones"].ListItems())
    {
        Console.WriteLine($"{zone.ClassName} slot={zone.Int("m_ButtonSlot")} side={zone.Int("m_Side")}");
        foreach (var card in zone["m_cards"].ListItems())
        {
            var entity = card["m_entity"];
            Console.WriteLine($"  card={entity.String("m_cardIdInternal")} cost={entity.Int("m_realTimeCost")} tech={entity.Int("m_realTimePlayerTechLevel")}");
        }
    }

    return 0;
}

static int ClassDump(string className, string[] path)
{
    using var memory = new HearthstoneMemory();
    if (!memory.EnsureAttached() || memory.Runtime is not { } runtime)
    {
        Console.WriteLine("Not attached: " + memory.Status);
        return 2;
    }

    if (className == "netcache")
    {
        foreach (var value in HearthstoneMemory.FindService(runtime, "NetCache")["m_netCache"].MapValues())
        {
            Console.WriteLine(value.ClassName);
        }

        return 0;
    }

    var cls = runtime.FindClass(className);
    if (cls is null)
    {
        foreach (var name in runtime.ClassNames.Where(n => n.Contains(className, StringComparison.OrdinalIgnoreCase)).Take(40))
        {
            Console.WriteLine("  " + name);
        }

        return 3;
    }

    foreach (var field in cls.AllFields())
    {
        Console.WriteLine("  " + field);
    }

    if (path.Length == 0)
    {
        return 0;
    }

    var obj = cls.GetStatic(path[0]);
    foreach (var segment in path.Skip(1))
    {
        obj = obj[segment];
    }

    Console.WriteLine($"=> {obj}");
    foreach (var field in obj.Class?.AllFields() ?? Enumerable.Empty<MonoField>())
    {
        if (field.IsStatic) continue;
        Console.WriteLine($"  {field.Name,-40} int={obj.Int(field.Name),-12} ptr=0x{obj.Object(field.Name).Address:X}");
    }

    return 0;
}
