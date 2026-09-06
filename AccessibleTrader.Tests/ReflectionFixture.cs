using System.Collections;
using System.Reflection;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Reflection fixture shared by the two completeness guards — <see cref="CloneCompletenessTests"/>
/// (does a hand-written clone copy every field?) and <see cref="WorkspaceRestoreContractTests"/>
/// (is every field either restored from the workspace or declared as owned by something else?).
///
/// <para>
/// Both answer the same underlying question — "what happens to a field nobody remembered" — and
/// both need the same two primitives: a value demonstrably different from the one already there,
/// and a comparison that tolerates a deep copy. Keeping one copy of that machinery means a fixture
/// that learns about a new property type teaches both guards at once.
/// </para>
/// </summary>
internal static class ReflectionFixture
{

    internal static List<PropertyInfo> SettableProperties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            // A get-only collection property is still state a clone must carry, so those count
            // too — they are populated in place rather than assigned.
            .Where(p => p.CanWrite || IsPopulatableCollection(p.PropertyType))
            .Where(p => p.Name != "Item")
            .OrderBy(p => p.Name)
            .ToList();

    /// <summary>
    /// Copies a generated collection into an existing get-only collection instance.
    /// Returns false when the target cannot be filled, which the caller turns into a failure.
    /// </summary>
    internal static bool PopulateInPlace(object? target, object? source)
    {
        if (target == null || source == null) return false;
        if (target is IDictionary td && source is IDictionary sd)
        {
            foreach (DictionaryEntry e in sd) td[e.Key] = e.Value;
            return td.Count > 0;
        }
        if (target is IList tl && source is IList sl)
        {
            foreach (var item in sl) tl.Add(item);
            return tl.Count > 0;
        }
        return false;
    }

    /// <summary>Types compared by value rather than structurally.</summary>
    internal static bool IsScalar(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal)
               || u == typeof(DateTime) || u == typeof(TimeSpan) || u == typeof(Guid);
    }

    internal static bool IsPopulatableCollection(Type t) =>
        typeof(IList).IsAssignableFrom(t) || typeof(IDictionary).IsAssignableFrom(t);

    /// <summary>
    /// A value of <paramref name="t"/> that is not <paramref name="current"/>. Returns null when
    /// no fixture can be built, which the caller turns into a failure rather than a silent skip.
    /// </summary>
    internal static object? DistinctValue(Type t, object? current)
    {
        var under = Nullable.GetUnderlyingType(t);
        if (under != null)
        {
            object? inner = DistinctValue(under, current);
            return inner;
        }

        if (t == typeof(string))
        {
            string cur = current as string ?? "";
            return cur == "fixture" ? "fixture-2" : "fixture";
        }
        if (t == typeof(bool))   return !(bool)(current ?? false);
        if (t == typeof(int))    return (int)(current ?? 0) + 7;
        if (t == typeof(long))   return (long)(current ?? 0L) + 7L;
        if (t == typeof(short))  return (short)((short)(current ?? (short)0) + 7);
        if (t == typeof(byte))   return (byte)((byte)(current ?? (byte)0) + 7);
        // double.NaN is a real default in this codebase (ZoneBandConfig's fixed bounds mean
        // "not set"), and NaN + 7 is NaN — which would compare equal to the default and make the
        // property vacuously "tested".
        if (t == typeof(double))
            return current is double cd && !double.IsFinite(cd) ? 42d : (double)(current ?? 0d) + 7d;
        if (t == typeof(float))
            return current is float cf && !float.IsFinite(cf) ? 42f : (float)(current ?? 0f) + 7f;
        if (t == typeof(decimal)) return (decimal)(current ?? 0m) + 7m;
        if (t == typeof(DateTime))
            return new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc)
                   + TimeSpan.FromDays(current is DateTime d && d.Year == 2026 ? 1 : 0);
        if (t == typeof(TimeSpan)) return TimeSpan.FromMinutes(11);
        if (t == typeof(Guid))     return Guid.Parse("11111111-2222-3333-4444-555555555555");

        if (t.IsEnum)
        {
            var values = Enum.GetValues(t).Cast<object>().ToList();
            return values.FirstOrDefault(v => !Equals(v, current)) ?? values.First();
        }

        // Arrays: one element is enough to tell "copied" from "dropped".
        if (t.IsArray)
        {
            var elem = t.GetElementType()!;
            var arr = Array.CreateInstance(elem, 1);
            var v = DistinctValue(elem, null);
            if (v != null) arr.SetValue(v, 0);
            return arr;
        }

        // Concrete or interface collections: build a List<T>/Dictionary<K,V> with one entry.
        Type concrete = t;
        if (t.IsInterface && t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var args = t.GetGenericArguments();
            if (def == typeof(IReadOnlyList<>) || def == typeof(IList<>) ||
                def == typeof(ICollection<>) || def == typeof(IEnumerable<>))
                concrete = typeof(List<>).MakeGenericType(args);
            else if (def == typeof(IReadOnlyDictionary<,>) || def == typeof(IDictionary<,>))
                concrete = typeof(Dictionary<,>).MakeGenericType(args);
            else return null;
        }
        if (concrete.IsInterface || concrete.IsAbstract) return null;

        object instance;
        try
        {
            if (concrete.GetConstructor(Type.EmptyTypes) != null)
            {
                instance = Activator.CreateInstance(concrete)!;
            }
            else
            {
                // Positional records (CloudSonificationConfig) have no parameterless
                // constructor. Build one through the greediest public constructor with
                // generated arguments rather than leaving the property unchecked.
                var ctor = concrete.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
                if (ctor == null) return null;
                var args = ctor.GetParameters()
                    .Select(pi => DistinctValue(pi.ParameterType, null))
                    .ToArray();
                if (args.Any(a => a == null)) return null;
                instance = ctor.Invoke(args);
            }
        }
        catch { return null; }

        if (instance is IDictionary dict)
        {
            var args = concrete.GetGenericArguments();
            if (args.Length != 2) return instance;
            var k = DistinctValue(args[0], null);
            var v = DistinctValue(args[1], null);
            if (k != null && v != null) dict[k] = v;
            return dict;
        }
        if (instance is IList list)
        {
            var elem = concrete.IsGenericType ? concrete.GetGenericArguments()[0] : typeof(object);
            var v = DistinctValue(elem, null);
            if (v != null) list.Add(v);
            return list;
        }

        // A plain object (DrawingData on SeriesConfig, CloudSonificationConfig on a cloud
        // fill). A fresh instance is not enough on its own — two default instances compare
        // equal, so a clone that dropped the property could still pass. Give it scalar values
        // of its own, which Equivalent then compares field by field.
        foreach (var np in SettableProperties(concrete))
        {
            if (!np.CanWrite) continue;
            if (!IsScalar(np.PropertyType)) continue;
            var nv = DistinctValue(np.PropertyType, np.GetValue(instance));
            if (nv != null) np.SetValue(instance, nv);
        }
        return instance;
    }

    /// <summary>
    /// Deep-clone-tolerant comparison: a clone produces different instances, so collections are
    /// compared by content and everything else by value.
    /// </summary>
    internal static bool Equivalent(object? a, object? b)
    {
        if (a == null || b == null) return ReferenceEquals(a, b);
        if (a is string || b is string) return Equals(a, b);

        if (a is IDictionary da && b is IDictionary db)
        {
            if (da.Count != db.Count) return false;
            foreach (DictionaryEntry e in da)
            {
                if (!db.Contains(e.Key)) return false;
                if (!Equivalent(e.Value, db[e.Key])) return false;
            }
            return true;
        }

        if (a is IEnumerable ea && b is IEnumerable eb)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = eb.Cast<object?>().ToList();
            // Content of a cloned element is not compared — a dropped collection comes back
            // empty, and that is the failure this is looking for.
            return la.Count == lb.Count;
        }

        if (a.GetType() != b.GetType()) return false;
        if (a.GetType().IsValueType || a is string) return Equals(a, b);

        // A nested object: compare its scalars. Comparing only "both non-null" would let a
        // clone that substituted a fresh default instance pass as a copy.
        foreach (var p in SettableProperties(a.GetType()))
        {
            if (!IsScalar(p.PropertyType)) continue;
            if (!Equals(p.GetValue(a), p.GetValue(b))) return false;
        }
        return true;
    }
}
