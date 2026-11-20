using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Hearthpulse.Core.Battlegrounds;
using Hearthpulse.Core.Battlegrounds.Effects;

namespace Hearthpulse.Tests.Sim;

/// <summary>
/// A thread of many fights reuses one <see cref="Fight"/>, its two <see cref="Side"/>s and its <see cref="Unit"/>s
/// (<see cref="Fight.Next"/>, <c>Fight.Init</c>, <c>Side.Init</c>, <c>Unit.Init</c>). A field left out of a reset would carry
/// the last fight into the next and quietly change the odds. The reset test finds every instance field by reflection,
/// sets each one to a value no reset leaves (so a field added later is covered without editing the test), sets the
/// objects up for another fight and compares them field by field with a fight built new.
/// </summary>
public sealed class SimPoolResetTests
{
    private const string Titus = "BG25_354";
    private const string Ghastcoiler = "BGS_008";
    private const string HarmlessBonehead = "BG28_300";
    private const string CordPuller = "BG29_611";
    private const string ScarletSkull = "BG25_022";
    private const string DeathlyStriker = "BG31_835";
    private const string EternalTycoon = "BG34_403";
    private const string RelentlessDeflector = "BG34_405";
    private const string BladeCollector = "BG26_817";

    /// <summary>
    /// Fields a reused fight keeps by design, with the reason. A kept list is the same object, emptied by the reset (its
    /// content is still dirtied and compared); the others are scratch memory the next fight never reads before writing.
    /// </summary>
    private static readonly Dictionary<string, string> Kept = new(StringComparer.Ordinal)
    {
        ["Fight._dead"] = "list: the scratch's death list, emptied",
        ["Fight._targets"] = "list: the scratch's target candidates, emptied",
        ["Fight._buffers"] = "scratch: observer buffers, read only up to the count a snapshot just copied",
        ["Fight._scratch"] = "scratch: the thread's scratch itself",
        ["Fight.<Ours>k__BackingField"] = "side: the reused side, set up again (its own fields are checked)",
        ["Fight.<Theirs>k__BackingField"] = "side: the reused side, set up again (its own fields are checked)",
        ["Side._board"] = "list: the side's board, emptied",
        ["Side._extras"] = "list: the side's player-level holders, emptied",
        ["Side._dead"] = "list: the side's dead, emptied",
    };

    private static SimMinion M(string id, int attack, int health, Keyword keywords = Keyword.None, bool golden = false,
                               CardMechanic mechanics = CardMechanic.None) =>
        SimMinion.Create(id, attack, health, Tribe.Undead, keywords, golden, mechanics) with { TechLevel = 4 };

    private static SimMinion Dr(string id, int attack, int health, Keyword keywords = Keyword.None, bool golden = false) =>
        M(id, attack, health, keywords, golden, CardMechanic.Deathrattle);

    private static SimSide S(params SimMinion[] minions) => new(minions) { TavernTier = 6, Health = 30 };

    /// <summary>Boards with deathrattles, reborn, avenge, windfury and a Duos teammate, so a fight changes most fields.</summary>
    private static readonly (SimSide Ours, SimSide Theirs)[] Boards =
    [
        (S(M(Titus, 4, 10, golden: true), Dr(HarmlessBonehead, 2, 2, golden: true), Dr(CordPuller, 3, 3, Keyword.DivineShield),
           Dr(Ghastcoiler, 7, 7), Dr(ScarletSkull, 3, 3, Keyword.Reborn)) with { PlayerId = 3 },
         S(M(BladeCollector, 6, 9, Keyword.Windfury), Dr(HarmlessBonehead, 2, 2), M(string.Empty, 5, 5, Keyword.Venomous),
           M(string.Empty, 8, 8, Keyword.Taunt | Keyword.DivineShield)) with { PlayerId = 5 }),
        (S(M(DeathlyStriker, 5, 8), M(EternalTycoon, 4, 10, golden: true), M(RelentlessDeflector, 3, 12, Keyword.Taunt),
           Dr(HarmlessBonehead, 1, 1, golden: true), Dr(CordPuller, 2, 2)),
         S(M(DeathlyStriker, 5, 8, golden: true), M(EternalTycoon, 4, 10), Dr(HarmlessBonehead, 1, 1), Dr(Ghastcoiler, 4, 4))),
        (S(M(string.Empty, 2, 3), M(string.Empty, 1, 1, Keyword.Reborn)) with
         {
             Teammate = S(Dr(HarmlessBonehead, 2, 2), M(string.Empty, 6, 6, Keyword.Windfury)),
         },
         S(M(string.Empty, 9, 9, Keyword.DivineShield), Dr(CordPuller, 3, 3)) with
         {
             Teammate = S(M(string.Empty, 3, 30, Keyword.Taunt)),
         }),
    ];

    [Fact]
    public void Every_field_of_a_reused_fight_its_sides_and_units_is_set_up_again()
    {
        foreach (var (first, second) in new[] { (0, 1), (1, 2), (2, 0), (2, 1) })
        {
            var scratch = new Fight.Scratch();
            var dirty = Fight.Next(scratch, Boards[first].Ours, Boards[first].Theirs, EffectRegistry.Default, Chooser(1), Prepared(first));
            dirty.Run(firstAttackerIsOurs: null, firstAttackerEntityId: 0);

            // Every field of the fight, both sides and every unit the scratch holds, to a value no reset leaves.
            var objects = new List<object> { dirty, dirty.Ours, dirty.Theirs };
            objects.AddRange(scratch.Units);
            foreach (var o in objects)
            {
                Dirty(o);
            }

            var chooser = Chooser(2);
            var reused = Fight.Next(scratch, Boards[second].Ours, Boards[second].Theirs, EffectRegistry.Default, chooser, Prepared(second));
            var fresh = Fight.Next(new Fight.Scratch(), Boards[second].Ours, Boards[second].Theirs, EffectRegistry.Default, chooser, Prepared(second));

            Assert.Same(dirty, reused);
            Compare(fresh, reused, $"boards {first}->{second}: fight", new Dictionary<object, object>(ReferenceEqualityComparer.Instance));
        }
    }

    [Fact]
    public void The_dirtying_reaches_every_field_and_the_comparison_sees_a_left_out_reset()
    {
        // The check itself: a unit whose reset forgot a field is told apart from a new one.
        var chooser = Chooser(1);
        var fight = Fight.Next(new Fight.Scratch(), Boards[0].Ours, Boards[0].Theirs, EffectRegistry.Default, chooser, Prepared(0));
        var fresh = Fight.Next(new Fight.Scratch(), Boards[0].Ours, Boards[0].Theirs, EffectRegistry.Default, chooser, Prepared(0));
        var unit = fight.Ours.Minions[0];
        unit.AttackDepth = 3;
        var error = Assert.ThrowsAny<Exception>(() =>
            Compare(fresh, fight, "fight", new Dictionary<object, object>(ReferenceEqualityComparer.Instance)));
        Assert.Contains("AttackDepth", error.Message, StringComparison.Ordinal);

        // Every instance field of the three types is either dirtied or kept with a reason (none skipped silently).
        foreach (var type in new[] { typeof(Fight), typeof(Side), typeof(Unit) })
        {
            foreach (var field in Fields(type))
            {
                var key = $"{type.Name}.{field.Name}";
                if (Kept.TryGetValue(key, out var reason))
                {
                    Assert.False(string.IsNullOrWhiteSpace(reason), key);
                }
            }
        }

        Assert.All(Kept.Keys, key => Assert.Contains(key, new[] { typeof(Fight), typeof(Side), typeof(Unit) }
            .SelectMany(t => Fields(t).Select(f => $"{t.Name}.{f.Name}"))));
    }

    [Fact]
    public void Fights_on_reused_objects_play_out_as_fights_built_new()
    {
        // Boards in turn through one scratch (more units, then fewer, then a Duos fight), every fight against a new one
        // with the same picks: same result, same survivors, same deathrattle count.
        var scratch = new Fight.Scratch();
        for (var i = 0; i < 60; i++)
        {
            var board = i % Boards.Length;
            var pooled = Fight.Next(scratch, Boards[board].Ours, Boards[board].Theirs, EffectRegistry.Default, Chooser(i), Prepared(board));
            var pooledOutcome = pooled.Run(null, 0);
            var pooledState = State(pooled);

            var fresh = new Fight(Boards[board].Ours, Boards[board].Theirs, EffectRegistry.Default, Chooser(i), trace: false);
            var freshOutcome = fresh.Run(null, 0);

            Assert.Equal(freshOutcome, pooledOutcome);
            Assert.Equal(State(fresh), pooledState);
        }
    }

    private static string State(Fight fight) =>
        $"{fight.Ours} | {fight.Theirs} | dr {fight.Ours.DeathrattlesTriggered}/{fight.Theirs.DeathrattlesTriggered} | dead {fight.Ours.DeadThisFight.Count}/{fight.Theirs.DeadThisFight.Count} | {fight.Truncated}";

    private static RandomChooser Chooser(int seed) => new(new Random(seed));

    // One per board, as a simulation shares it between its fights (both fights of a comparison get the same one).
    private static readonly Fight.PreparedEffects[] PreparedBoards =
        [.. Boards.Select(b => Fight.PreparedEffects.For(b.Ours, b.Theirs, EffectRegistry.Default, manyFights: true))];

    private static Fight.PreparedEffects Prepared(int board) => PreparedBoards[board];

    private static IEnumerable<FieldInfo> Fields(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool IsEngineObject(Type type) => type == typeof(Fight) || type == typeof(Side) || type == typeof(Unit);

    // ================================================================================================ dirtying

    private static void Dirty(object target)
    {
        foreach (var field in Fields(target.GetType()))
        {
            var key = $"{target.GetType().Name}.{field.Name}";
            var value = field.GetValue(target);
            if (Kept.TryGetValue(key, out var reason))
            {
                if (reason.StartsWith("list:", StringComparison.Ordinal))
                {
                    ((List<Unit>)value!).Add(DummyUnit.Value);
                }

                continue;
            }

            field.SetValue(target, Dirtied(field.FieldType, value, key));
        }
    }

    private static object? Dirtied(Type type, object? value, string key)
    {
        if (type == typeof(bool))
        {
            return !(bool)value!;
        }

        if (type == typeof(int))
        {
            return (int)value! + 7919;
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) ^ 0x2A);
        }

        if (type.IsValueType)
        {
            // A struct: each of its fields dirtied in a boxed copy.
            var boxed = value ?? RuntimeHelpers.GetUninitializedObject(type);
            foreach (var field in Fields(type))
            {
                field.SetValue(boxed, Dirtied(field.FieldType, field.GetValue(boxed), $"{key}.{field.Name}"));
            }

            return boxed;
        }

        return value is null ? Sentinel(type, key) : null;
    }

    private static readonly Lazy<Unit> DummyUnit = new(() =>
        new Fight(S(M(string.Empty, 1, 1)), S(M(string.Empty, 1, 1)), EffectRegistry.Default, Chooser(9), trace: false).Ours.Minions[0]);

    /// <summary>A non-null value of <paramref name="type"/> for a field that is null now; a new field type needs a line here.</summary>
    private static object Sentinel(Type type, string key)
    {
        if (type == typeof(Unit)) return DummyUnit.Value;
        if (type == typeof(Side)) return DummyUnit.Value.Side;
        if (type == typeof(Fight)) return DummyUnit.Value.Side.Fight;
        if (type == typeof(string)) return "dirty";
        if (type == typeof(SimSide)) return S();
        if (type == typeof(SimMinion)) return M("dirty", 1, 1);
        if (type == typeof(EffectRegistry)) return EffectRegistry.FromTypes();
        if (type == typeof(ICombatChooser)) return Chooser(7);
        if (type == typeof(Fight.Scratch)) return new Fight.Scratch();
        if (type == typeof(SpellResolver)) return (SpellResolver)((Fight _, in SpellCast _) => { });
        if (type.IsArray) return Array.CreateInstance(type.GetElementType()!, 1);
        if (type.IsGenericType && type.GetConstructor(Type.EmptyTypes) is { } ctor) return ctor.Invoke(null);
        throw new InvalidOperationException($"{key}: no dirty value for {type}; add one to {nameof(Sentinel)}.");
    }

    // ================================================================================================ comparison

    private static void Compare(object? expected, object? actual, string path, Dictionary<object, object> seen)
    {
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
            {
                throw new InvalidOperationException($"{path}: expected {Show(expected)}, got {Show(actual)}");
            }

            return;
        }

        var type = expected.GetType();
        if (type != actual.GetType())
        {
            throw new InvalidOperationException($"{path}: expected a {type}, got a {actual.GetType()}");
        }

        if (IsEngineObject(type))
        {
            // The same role in both graphs: a fresh object maps to one reused object, always the same one.
            if (seen.TryGetValue(expected, out var mapped))
            {
                if (!ReferenceEquals(mapped, actual))
                {
                    throw new InvalidOperationException($"{path}: refers to another {type.Name} than the new fight does");
                }

                return;
            }

            seen[expected] = actual;
            foreach (var field in Fields(type))
            {
                var key = $"{type.Name}.{field.Name}";
                if (Kept.TryGetValue(key, out var reason) && reason.StartsWith("scratch:", StringComparison.Ordinal))
                {
                    continue;
                }

                Compare(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}", seen);
            }

            return;
        }

        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException($"{path}: expected {expected}, got {actual}");
            }

            return;
        }

        if (type.IsValueType)
        {
            foreach (var field in Fields(type))
            {
                Compare(field.GetValue(expected), field.GetValue(actual), $"{path}.{field.Name}", seen);
            }

            return;
        }

        if (expected is IDictionary expectedMap)
        {
            var actualMap = (IDictionary)actual;
            if (expectedMap.Count != actualMap.Count)
            {
                throw new InvalidOperationException($"{path}: expected {expectedMap.Count} entries, got {actualMap.Count}");
            }

            foreach (DictionaryEntry entry in expectedMap)
            {
                Compare(entry.Value, actualMap[entry.Key], $"{path}[{entry.Key}]", seen);
            }

            return;
        }

        if (expected is IEnumerable sequence and (IList or ICollection) && expected is not Array { Rank: > 1 })
        {
            var left = sequence.Cast<object?>().ToList();
            var right = ((IEnumerable)actual).Cast<object?>().ToList();
            if (left.Count != right.Count)
            {
                throw new InvalidOperationException($"{path}: expected {left.Count} items, got {right.Count}");
            }

            for (var i = 0; i < left.Count; i++)
            {
                Compare(left[i], right[i], $"{path}[{i}]", seen);
            }

            return;
        }

        // Inputs both fights were given (boards, specs, effects, the registry, the chooser): the very same objects.
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException($"{path}: expected the same {type.Name} as the new fight, got another");
        }
    }

    private static string Show(object? value) => value is null ? "null" : value.ToString() ?? value.GetType().Name;
}
