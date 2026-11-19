using System.Globalization;
using Hearthpulse.Core.Memory;

namespace Hearthpulse.Probe;

/// <summary>
/// Developer reconnaissance of the client's objects: start at a net cache object, a service or a static field, follow
/// a path of field names and item indexes, and print the object reached field by field (and the first items when it
/// is a list, an array, a set or a map). Read-only, like every read of this tool; printed to this console only.
/// </summary>
internal static class Walk
{
    // Mono type codes of the field types printed as values.
    private const int Boolean = 0x02;
    private const int Int32 = 0x08;
    private const int UInt32 = 0x09;
    private const int Int64 = 0x0A;
    private const int Single = 0x0C;
    private const int String = 0x0E;
    private const int ValueType = 0x11;

    // Mono array header: the length at 24, the elements from 32 (MonoOffsets, internal to Core).
    private const ulong ArrayLength = 24;
    private const ulong ArrayData = 32;

    /// <summary>
    /// <c>walk &lt;root&gt; [segment...]</c>. Root: <c>nc:&lt;Class&gt;</c> (a net cache object by class name),
    /// <c>svc:&lt;Service&gt;</c>, <c>st:&lt;Class&gt;:&lt;StaticField&gt;</c>. Segment: a field name, <c>#n</c> (item n of a
    /// List or an object array), <c>v#n</c> (value n of a Blizzard Map), <c>d#n</c> (value n of a Dictionary),
    /// <c>@n</c> (how many items to print, default 12).
    /// </summary>
    public static int Run(string[] args)
    {
        using var memory = new HearthstoneMemory();
        if (!memory.EnsureAttached() || memory.Runtime is not { } rt)
        {
            Console.WriteLine("Not attached: " + memory.Status);
            return 2;
        }

        if (args[0] == "services")
        {
            // The type names of the client's service locator (the roots svc: takes).
            var builders = rt.FindClass("Hearthstone.HearthstoneJobs")?.GetStatic("s_dependencyBuilder") ?? default;
            foreach (var builder in builders.ListItems())
            {
                foreach (var entry in builder["m_serviceLocator"]["m_services"].DictionaryValues())
                {
                    Console.WriteLine(entry.String("<ServiceTypeName>k__BackingField"));
                }
            }

            return 0;
        }

        var obj = Root(rt, args[0]);
        var show = 12;
        foreach (var segment in args.Skip(1))
        {
            if (segment.StartsWith('@'))
            {
                show = int.Parse(segment[1..], CultureInfo.InvariantCulture);
                continue;
            }

            obj = Step(rt, obj, segment);
            Console.WriteLine($"{segment} => {obj}");
        }

        Dump(rt, obj, show);
        return 0;
    }

    private static MonoObject Root(MonoRuntime rt, string root)
    {
        if (root.StartsWith("nc:", StringComparison.Ordinal))
        {
            return HearthstoneMemory.FindNetCacheObject(rt, root[3..]);
        }

        if (root.StartsWith("svc:", StringComparison.Ordinal))
        {
            return HearthstoneMemory.FindService(rt, root[4..]);
        }

        if (root.StartsWith("st:", StringComparison.Ordinal))
        {
            var parts = root[3..].Split(':');
            return rt.FindClass(parts[0])?.GetStatic(parts[1]) ?? default;
        }

        throw new ArgumentException("root: nc:<Class> | svc:<Service> | st:<Class>:<Field>", nameof(root));
    }

    private static MonoObject Step(MonoRuntime rt, MonoObject obj, string segment)
    {
        if (segment.StartsWith('#'))
        {
            var index = int.Parse(segment[1..], CultureInfo.InvariantCulture);
            if (obj.Class?.FindField("_items") is not null)
            {
                return obj.ListItems().Skip(index).FirstOrDefault();
            }

            return new MonoArrayView(rt, obj.Address).ObjectAt(index);
        }

        if (segment.StartsWith("v#", StringComparison.Ordinal))
        {
            return obj.MapValues().Skip(int.Parse(segment[2..], CultureInfo.InvariantCulture)).FirstOrDefault();
        }

        if (segment.StartsWith("d#", StringComparison.Ordinal))
        {
            return obj.DictionaryValues().Skip(int.Parse(segment[2..], CultureInfo.InvariantCulture)).FirstOrDefault();
        }

        return obj[segment];
    }

    private static void Dump(MonoRuntime rt, MonoObject obj, int show)
    {
        var cls = obj.Class;
        Console.WriteLine($"== {cls?.FullName ?? "null"} @0x{obj.Address:X}");
        if (cls is null)
        {
            return;
        }

        if (cls.Name.EndsWith("[]", StringComparison.Ordinal))
        {
            var array = new MonoArrayView(rt, obj.Address);
            Console.WriteLine($"  array length {array.Length}, element size {cls.ElementSize}");
            for (var i = 0; i < Math.Min(show, array.Length); i++)
            {
                var item = array.ObjectAt(i);
                Console.WriteLine($"  [{i}] int={array.IntAt(i)} obj={Describe(rt, item)}");
            }

            return;
        }

        foreach (var field in cls.AllFields().Where(f => !f.IsStatic))
        {
            Console.WriteLine($"  {field.Name,-48} 0x{field.Offset:X3} t{field.TypeCode:X2} {Value(rt, obj, field)}");
        }
    }

    private static string Value(MonoRuntime rt, MonoObject obj, MonoField field)
    {
        var at = obj.Address + (ulong)field.Offset;
        return field.TypeCode switch
        {
            Boolean => rt.Memory.ReadBool(at).ToString(),
            Int32 or UInt32 or ValueType => rt.Memory.ReadInt32(at).ToString(CultureInfo.InvariantCulture),
            Int64 => rt.Memory.ReadInt64(at).ToString(CultureInfo.InvariantCulture),
            Single => BitConverter.Int32BitsToSingle(rt.Memory.ReadInt32(at)).ToString(CultureInfo.InvariantCulture),
            String => "\"" + rt.Memory.ReadMonoString(rt.Memory.ReadPtr(at)) + "\"",
            _ => Describe(rt, rt.Object(rt.Memory.ReadPtr(at))),
        };
    }

    private static string Describe(MonoRuntime rt, MonoObject obj)
    {
        if (obj.IsNull)
        {
            return "null";
        }

        var cls = obj.Class;
        if (cls is null)
        {
            return $"?@0x{obj.Address:X}";
        }

        if (cls.Name == "String")
        {
            return "\"" + rt.Memory.ReadMonoString(obj.Address) + "\"";
        }

        var size = cls.FindField("_size") is not null ? $" size={obj.Int("_size")}"
            : cls.FindField("_count") is not null ? $" count={obj.Int("_count")}"
            : cls.Name.EndsWith("[]", StringComparison.Ordinal) ? $" length={new MonoArrayView(rt, obj.Address).Length}"
            : string.Empty;
        return $"{cls.FullName}@0x{obj.Address:X}{size}";
    }

    /// <summary>An array at an address (<see cref="MonoArray"/> has no public constructor).</summary>
    private readonly struct MonoArrayView(MonoRuntime rt, ulong address)
    {
        public int Length
        {
            get
            {
                var length = rt.Memory.ReadInt64(address + ArrayLength);
                return length is < 0 or > 1_000_000 ? 0 : (int)length;
            }
        }

        public MonoObject ObjectAt(int index) => rt.Object(rt.Memory.ReadPtr(address + ArrayData + (ulong)index * 8));

        public int IntAt(int index) => rt.Memory.ReadInt32(address + ArrayData + (ulong)index * 4);
    }
}
