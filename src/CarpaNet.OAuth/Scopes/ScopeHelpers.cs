using System;
using System.Collections.Generic;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// Shared helpers for the scope permission types.
/// </summary>
internal static class ScopeHelpers
{
    public const string Wildcard = "*";

    public static bool Contains(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Copies the values into an array, validating each one.
    /// </summary>
    public static string[] ToValidatedArray(IEnumerable<string> values, Func<string, bool> validate, string paramName)
    {
        if (values == null)
        {
            throw new ArgumentNullException(paramName);
        }

        var list = new List<string>(values);
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one value is required.", paramName);
        }

        foreach (var value in list)
        {
            if (!validate(value))
            {
                throw new ArgumentException($"Invalid value: '{value}'", paramName);
            }
        }

        return list.ToArray();
    }

    /// <summary>
    /// Normalizes a list of NSID-or-wildcard values: a wildcard absorbs every other value,
    /// otherwise duplicates are removed and values are sorted (ordinal).
    /// </summary>
    public static string[] NormalizeWildcardList(IReadOnlyList<string> values)
    {
        if (values.Count > 1 && Contains(values, Wildcard))
        {
            return new[] { Wildcard };
        }

        return SortedUnique(values);
    }

    public static string[] SortedUnique(IReadOnlyList<string> values)
    {
        if (values.Count == 1)
        {
            return new[] { values[0] };
        }

        var set = new SortedSet<string>(values, StringComparer.Ordinal);
        var result = new string[set.Count];
        set.CopyTo(result);
        return result;
    }

    /// <summary>
    /// Adds a multi-valued parameter, either as the positional value (single value) or as repeated named parameters.
    /// </summary>
    public static string? AddPositionalMulti(string key, string[] values, List<KeyValuePair<string, string>> parameters)
    {
        if (values.Length == 1)
        {
            return values[0];
        }

        foreach (var value in values)
        {
            parameters.Add(new KeyValuePair<string, string>(key, value));
        }

        return null;
    }
}
