using System.Buffers.Binary;
using System.Text;

namespace Hearthpulse.Core.Memory;

public sealed partial class HearthstoneMemory
{
    // A localized string or a name longer than this is no text of the client (the longest description is some hundreds).
    private const int MaxClientText = 8_192;

    // Each slot of a corefx HashSet<int>: {hashCode, next, value}, 12 bytes.
    private const int IntSetSlotSize = 12;

    // A field of a table record, a localized value or a player state lies within the first few hundred bytes of its
    // object; an offset past this is no layout of the client (and would size a buffer by garbage).
    private const int MaxFieldOffset = 0x1000;

    /// <summary>Whether an instance field's offset, as the class metadata gives it, is one an object of the client has.</summary>
    private static bool SaneOffset(MonoField field) => field.Offset is >= 0 and <= MaxFieldOffset;

    /// <summary>
    /// One long read of the client's tables and caches (docs/collection-cosmetics.md), with the rules of
    /// <see cref="ReadCollection"/>: all or nothing. Field names go through <see cref="Field"/>; each table's records are
    /// read one piece each and must be of the first record's class; a localized value must be a <c>DbfLocValue</c>; a
    /// string must be readable and of sane length; a set or a map must count what it says it holds. The first problem is
    /// kept as the reason; the read is asked every <see cref="CancelCheckEvery"/> rows whether it is still wanted.
    /// </summary>
    private sealed class ClientRead(HearthstoneMemory memory, MonoRuntime rt, Func<bool>? cancelled)
    {
        private int _rows;
        private MonoClass? _locClass;
        private ulong _locVTable;
        private int _locValueOffset = -1;
        private byte[] _locBytes = [];

        public MonoRuntime Runtime => rt;

        /// <summary>Why the read gave nothing; null while nothing failed.</summary>
        public string? Miss { get; private set; }

        /// <summary>Notes the first reason (a spent budget wins: whatever it was reading then failed for that) and gives null.</summary>
        public T? Fail<T>(string reason)
            where T : class
        {
            Miss ??= ProcessMemory.BudgetExhausted ? BudgetMiss : reason;
            return null;
        }

        /// <summary>False, with the reason noted, once the caller no longer wants the read (asked every few hundred rows).</summary>
        public bool Wanted()
        {
            if (_rows++ % CancelCheckEvery == 0 && cancelled?.Invoke() == true)
            {
                Miss ??= "the read was no longer wanted (a game began or the client went away)";
                return false;
            }

            return Miss is null;
        }

        /// <summary>
        /// The records of <c>GameDbf.&lt;table&gt;</c> (<c>Dbf&lt;T&gt;.m_records</c>, a List&lt;T&gt;), each read once, with the
        /// offsets of <paramref name="fields"/>. Null when the table, a field or a record cannot be read.
        /// </summary>
        public DbfTable? Table(MonoClass gameDbf, string table, string[] fields, int max)
        {
            var dbf = gameDbf.GetStatic(table);
            var recordsField = memory.Field(dbf.Class, "Dbf.records", ["m_records"]);
            if (dbf.IsNull || recordsField is null)
            {
                return Fail<DbfTable>($"the table {table} or its records were not found");
            }

            if (List(dbf[recordsField], max) is not { } pointers)
            {
                return Fail<DbfTable>($"the records of {table} are no list, too many or unreadable");
            }

            if (pointers.Count == 0)
            {
                return Fail<DbfTable>($"the table {table} is empty (not loaded yet)");
            }

            var first = rt.Object(pointers[0]);
            var cls = first.Class;
            if (cls is null || !cls.Name.EndsWith("DbfRecord", StringComparison.Ordinal))
            {
                return Fail<DbfTable>($"the first record of {table} is no table record");
            }

            var offsets = new Dictionary<string, int>(fields.Length, StringComparer.Ordinal);
            foreach (var field in fields)
            {
                var name = memory.Field(cls, $"Dbf.{table}.{field}", [field]);
                if (name is null || cls.FindField(name) is not { IsStatic: false } definition)
                {
                    return Fail<DbfTable>($"the field {field} of {table} was not found");
                }

                if (!SaneOffset(definition))
                {
                    return Fail<DbfTable>($"the field {field} of {table} is at an offset out of range");
                }

                offsets[field] = definition.Offset;
            }

            var size = offsets.Values.Max() + 8;
            var vtable = rt.Memory.ReadPtr(first.Address);
            var rows = new byte[pointers.Count * size];
            for (var i = 0; i < pointers.Count; i++)
            {
                var row = rows.AsSpan(i * size, size);
                if (!Wanted())
                {
                    return null;
                }

                if (pointers[i] == 0 || !rt.Memory.TryRead(pointers[i], row) || !IsOfClass(rt, row, vtable, cls))
                {
                    return Fail<DbfTable>($"a record of {table} is null, unreadable or of another class");
                }
            }

            return new DbfTable(rows, size, offsets);
        }

        /// <summary>The pointers of a List&lt;T&gt; of references, null when it is no list, longer than <paramref name="max"/> or unreadable.</summary>
        public List<ulong>? List(MonoObject list, int max)
        {
            if (list.IsNull || list.Class?.Name != "List`1")
            {
                return null;
            }

            var items = list.Array("_items");
            var size = list.Int("_size", -1);
            if (size < 0 || size > max || (size > 0 && (items.Address == 0 || items.Length < size)))
            {
                return null;
            }

            if (size == 0)
            {
                return [];
            }

            if (rt.Memory.ReadBytes(items.Address + MonoOffsets.ArrayData, size * 8) is not { } bytes)
            {
                return null;
            }

            var pointers = new List<ulong>(size);
            for (var i = 0; i < size; i++)
            {
                pointers.Add(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(i * 8)));
            }

            return pointers;
        }

        /// <summary>The text of a localized value (<c>DbfLocValue.m_currentLocaleValue</c>, the client's language); "" for no value, null when unreadable.</summary>
        public string? Localized(ulong locValue)
        {
            if (locValue == 0)
            {
                return string.Empty;
            }

            if (_locClass is null)
            {
                var first = rt.Object(locValue);
                var cls = first.Class;
                var field = memory.Field(cls, "Dbf.loc.current", ["m_currentLocaleValue"]);
                if (cls?.Name != "DbfLocValue" || field is null || cls.FindField(field) is not { IsStatic: false } definition)
                {
                    return Fail<string>("a localized value is no DbfLocValue with its current text");
                }

                if (!SaneOffset(definition))
                {
                    return Fail<string>("the text of a localized value is at an offset out of range");
                }

                (_locClass, _locVTable, _locValueOffset) = (cls, rt.Memory.ReadPtr(locValue), definition.Offset);
                _locBytes = new byte[_locValueOffset + 8];
            }

            if (!rt.Memory.TryRead(locValue, _locBytes) || !IsOfClass(rt, _locBytes, _locVTable, _locClass))
            {
                return Fail<string>("a localized value is unreadable or of another class");
            }

            return Text(BinaryPrimitives.ReadUInt64LittleEndian(_locBytes.AsSpan(_locValueOffset)));
        }

        /// <summary>A managed string; "" for a null reference, null when its length or characters cannot be read.</summary>
        public string? Text(ulong text)
        {
            if (text == 0)
            {
                return string.Empty;
            }

            Span<byte> lengthBytes = stackalloc byte[4];
            if (!rt.Memory.TryRead(text + MonoOffsets.StringLength, lengthBytes))
            {
                return Fail<string>("a string is unreadable");
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length is < 0 or > MaxClientText)
            {
                return Fail<string>("a string's length is out of range");
            }

            if (length == 0)
            {
                return string.Empty;
            }

            return rt.Memory.ReadBytes(text + MonoOffsets.StringChars, length * 2) is { } chars
                ? Encoding.Unicode.GetString(chars)
                : Fail<string>("a string is unreadable");
        }

        /// <summary>
        /// The values of a corefx HashSet&lt;int&gt; and its version (to tell a change during the read): a live slot is one
        /// below <c>_lastIndex</c> with a hash code that is not negative; every live slot's hash code must be its value
        /// &amp; 0x7FFFFFFF and their number <c>_count</c>. Null when anything differs.
        /// </summary>
        public (HashSet<int> Values, int Version)? IntSet(MonoObject set, string what, int max)
        {
            var cls = set.Class;
            var slotsField = memory.Field(cls, "HashSet.slots", ["_slots"]);
            var countField = memory.Field(cls, "HashSet.count", ["_count"], integer: true);
            var lastField = memory.Field(cls, "HashSet.lastIndex", ["_lastIndex"], integer: true);
            var versionField = memory.Field(cls, "HashSet.version", ["_version"], integer: true);
            if (set.IsNull || cls?.Name != "HashSet`1" || slotsField is null || countField is null || lastField is null || versionField is null)
            {
                Fail<object>($"{what} is no HashSet with the fields known");
                return null;
            }

            var count = set.Int(countField, -1);
            var last = set.Int(lastField, -1);
            var version = set.Int(versionField);
            var slots = set.Array(slotsField);
            // Removed values keep their slots until the set is rebuilt: a few times the count at most, never a gigabyte.
            if (last < 0 || last > 4 * max)
            {
                Fail<object>($"{what}: the last index is out of range");
                return null;
            }

            if (count < 0 || count > max || last < count || (last > 0 && (slots.Address == 0 || slots.Length < last)))
            {
                Fail<object>($"{what}: the count is out of range or beyond the slots");
                return null;
            }

            var values = new HashSet<int>();
            if (last == 0)
            {
                return (values, version);
            }

            if (slots.ElementSize != IntSetSlotSize
                || rt.Memory.ReadBytes(slots.Address + MonoOffsets.ArrayData, last * IntSetSlotSize) is not { } bytes)
            {
                Fail<object>($"{what}: the slots are of another size or unreadable");
                return null;
            }

            for (var i = 0; i < last; i++)
            {
                var hash = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * IntSetSlotSize));
                var value = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan((i * IntSetSlotSize) + 8));
                if (hash < 0)
                {
                    continue;
                }

                if (hash != (value & 0x7FFFFFFF) || !values.Add(value))
                {
                    Fail<object>($"{what}: a slot's hash code is not its value's, or a value repeats");
                    return null;
                }
            }

            if (values.Count != count)
            {
                Fail<object>($"{what}: the live slots are not as many as the count");
                return null;
            }

            return (values, version);
        }
    }

    /// <summary>The records of one table as read: one byte image per record, with the offsets of the fields asked for.</summary>
    private sealed class DbfTable(byte[] rows, int size, Dictionary<string, int> offsets)
    {
        public int Count => rows.Length / size;

        public int Int(int row, string field) => BinaryPrimitives.ReadInt32LittleEndian(rows.AsSpan((row * size) + offsets[field]));

        public long Long(int row, string field) => BinaryPrimitives.ReadInt64LittleEndian(rows.AsSpan((row * size) + offsets[field]));

        public bool Bool(int row, string field) => rows[(row * size) + offsets[field]] != 0;

        public ulong Ptr(int row, string field) => BinaryPrimitives.ReadUInt64LittleEndian(rows.AsSpan((row * size) + offsets[field]));
    }
}
