using System;
using System.Collections.Generic;
using System.Text;

namespace CarpaNet.OAuth.Scopes;

/// <summary>
/// Parsed form of an atproto scope string: <c>prefix[:positional][?key=value&amp;...]</c>.
/// Port of <c>ScopeStringSyntax</c> from <c>@atproto/oauth-scopes</c>.
/// </summary>
internal sealed class ScopeStringSyntax
{
    private readonly List<KeyValuePair<string, string>>? _params;

    private ScopeStringSyntax(string prefix, string? positional, List<KeyValuePair<string, string>>? parameters)
    {
        Prefix = prefix;
        Positional = positional;
        _params = parameters;
    }

    /// <summary>
    /// The scope prefix (resource name), e.g. <c>repo</c>.
    /// </summary>
    public string Prefix { get; }

    /// <summary>
    /// The decoded positional parameter (after <c>:</c>), or null when absent.
    /// </summary>
    public string? Positional { get; }

    /// <summary>
    /// Whether the scope string is for the given prefix, i.e. equals it or is followed by <c>:</c> or <c>?</c>.
    /// </summary>
    public static bool IsScopeStringFor(string? value, string prefix)
    {
        if (value == null)
        {
            return false;
        }

        if (value.Length > prefix.Length)
        {
            var next = value[prefix.Length];
            return (next == ':' || next == '?') && value.StartsWith(prefix, StringComparison.Ordinal);
        }

        return string.Equals(value, prefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Parses a scope string. Returns null when the positional parameter contains malformed percent-encoding.
    /// </summary>
    public static ScopeStringSyntax? Parse(string scope)
    {
        var paramIdx = scope.IndexOf('?');
        var colonIdx = scope.IndexOf(':');
        var prefixEnd = paramIdx == -1 ? colonIdx : colonIdx == -1 ? paramIdx : Math.Min(paramIdx, colonIdx);

        if (prefixEnd == -1)
        {
            return new ScopeStringSyntax(scope, null, null);
        }

        var prefix = scope.Substring(0, prefixEnd);

        string? positional = null;
        if (colonIdx != -1 && (paramIdx == -1 || colonIdx < paramIdx))
        {
            var end = paramIdx == -1 ? scope.Length : paramIdx;
            positional = DecodeComponent(scope, colonIdx + 1, end);
            if (positional == null)
            {
                return null;
            }
        }

        List<KeyValuePair<string, string>>? parameters = null;
        if (paramIdx != -1 && paramIdx < scope.Length - 1)
        {
            parameters = ParseQuery(scope, paramIdx + 1);
        }

        return new ScopeStringSyntax(prefix, positional, parameters);
    }

    /// <summary>
    /// Whether every named parameter key is one of the allowed keys.
    /// </summary>
    public bool HasOnlyKeys(string key1, string? key2 = null)
    {
        if (_params == null)
        {
            return true;
        }

        foreach (var kvp in _params)
        {
            if (!string.Equals(kvp.Key, key1, StringComparison.Ordinal) &&
                !string.Equals(kvp.Key, key2, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets all values of a named parameter, or null when absent.
    /// </summary>
    public List<string>? GetMulti(string key)
    {
        if (_params == null)
        {
            return null;
        }

        List<string>? values = null;
        foreach (var kvp in _params)
        {
            if (string.Equals(kvp.Key, key, StringComparison.Ordinal))
            {
                (values ??= new List<string>(1)).Add(kvp.Value);
            }
        }

        return values;
    }

    /// <summary>
    /// Gets a single-valued named parameter.
    /// </summary>
    /// <returns>False when the parameter is present more than once.</returns>
    public bool TryGetSingle(string key, out string? value)
    {
        value = null;
        if (_params == null)
        {
            return true;
        }

        foreach (var kvp in _params)
        {
            if (string.Equals(kvp.Key, key, StringComparison.Ordinal))
            {
                if (value != null)
                {
                    return false;
                }

                value = kvp.Value;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets a single-valued parameter that may be given positionally or by name (but not both).
    /// </summary>
    /// <returns>False when the syntax is invalid for this parameter.</returns>
    public bool TryGetPositionalSingle(string key, out string? value)
    {
        if (!TryGetSingle(key, out value))
        {
            return false;
        }

        if (value != null)
        {
            return Positional == null;
        }

        value = Positional;
        return true;
    }

    /// <summary>
    /// Gets a multi-valued parameter that may be given positionally (as a single value) or by name (but not both).
    /// </summary>
    /// <returns>False when the syntax is invalid for this parameter.</returns>
    public bool TryGetPositionalMulti(string key, out List<string>? values)
    {
        values = GetMulti(key);
        if (values != null)
        {
            return Positional == null;
        }

        if (Positional != null)
        {
            values = new List<string>(1) { Positional };
        }

        return true;
    }

    /// <summary>
    /// Formats a scope string, encoding components the same way as the reference implementation
    /// (<c>encodeURIComponent</c> for the positional parameter, <c>URLSearchParams</c> for named
    /// parameters, then un-escaping <c>: / + , @ %</c>).
    /// </summary>
    public static string Format(string prefix, string? positional, IReadOnlyList<KeyValuePair<string, string>>? parameters)
    {
        var sb = new StringBuilder(prefix.Length + 32);
        sb.Append(prefix);

        if (positional != null)
        {
            sb.Append(':');
            AppendEncoded(sb, positional, form: false);
        }

        if (parameters != null && parameters.Count > 0)
        {
            sb.Append('?');
            for (var i = 0; i < parameters.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('&');
                }

                AppendEncoded(sb, parameters[i].Key, form: true);
                sb.Append('=');
                AppendEncoded(sb, parameters[i].Value, form: true);
            }
        }

        return sb.ToString();
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string scope, int start)
    {
        // application/x-www-form-urlencoded parsing, as done by URLSearchParams
        var result = new List<KeyValuePair<string, string>>(2);
        var pos = start;
        while (pos <= scope.Length)
        {
            var amp = scope.IndexOf('&', pos);
            var end = amp == -1 ? scope.Length : amp;

            if (end > pos)
            {
                var eq = scope.IndexOf('=', pos, end - pos);
                string key;
                string value;
                if (eq == -1)
                {
                    key = DecodeForm(scope, pos, end);
                    value = string.Empty;
                }
                else
                {
                    key = DecodeForm(scope, pos, eq);
                    value = DecodeForm(scope, eq + 1, end);
                }

                result.Add(new KeyValuePair<string, string>(key, value));
            }

            pos = end + 1;
        }

        return result;
    }

    private static string DecodeForm(string value, int start, int end)
    {
        var needsDecode = false;
        for (var i = start; i < end; i++)
        {
            if (value[i] == '%' || value[i] == '+')
            {
                needsDecode = true;
                break;
            }
        }

        var part = value.Substring(start, end - start);
        if (!needsDecode)
        {
            return part;
        }

        // Lenient: malformed escapes are kept as-is (like URLSearchParams)
        return Uri.UnescapeDataString(part.Replace('+', ' '));
    }

    /// <summary>
    /// Strict percent-decoding (like <c>decodeURIComponent</c>). Returns null on malformed input.
    /// </summary>
    private static string? DecodeComponent(string value, int start, int end)
    {
        var hasEscape = false;
        for (var i = start; i < end; i++)
        {
            if (value[i] == '%')
            {
                if (i + 2 >= end ||
                    !AtprotoSyntax.IsHexDigit(value[i + 1]) ||
                    !AtprotoSyntax.IsHexDigit(value[i + 2]))
                {
                    return null;
                }

                hasEscape = true;
                i += 2;
            }
        }

        var part = value.Substring(start, end - start);
        return hasEscape ? Uri.UnescapeDataString(part) : part;
    }

    private static void AppendEncoded(StringBuilder sb, string value, bool form)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (IsUnencoded(c, form))
            {
                sb.Append(c);
            }
            else if (form && c == ' ')
            {
                sb.Append('+');
            }
            else
            {
                AppendPercentEncoded(sb, value, ref i);
            }
        }
    }

    private static bool IsUnencoded(char c, bool form)
    {
        if (AtprotoSyntax.IsAsciiLetterOrDigit(c))
        {
            return true;
        }

        switch (c)
        {
            // Unreserved in both encodeURIComponent and URLSearchParams
            case '-':
            case '_':
            case '.':
            case '*':
            // Chars the reference implementation normalizes back after encoding
            case ':':
            case '/':
            case '+':
            case ',':
            case '@':
            case '%':
                return true;
            // Unreserved in encodeURIComponent only
            case '!':
            case '~':
            case '\'':
            case '(':
            case ')':
                return !form;
            default:
                return false;
        }
    }

    private static void AppendPercentEncoded(StringBuilder sb, string value, ref int index)
    {
        int codePoint = value[index];

        if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        {
            codePoint = char.ConvertToUtf32(value[index], value[index + 1]);
            index++;
        }
        else if (char.IsSurrogate(value[index]))
        {
            codePoint = 0xFFFD; // Lone surrogate
        }

        if (codePoint < 0x80)
        {
            AppendByte(sb, codePoint);
        }
        else if (codePoint < 0x800)
        {
            AppendByte(sb, 0xC0 | (codePoint >> 6));
            AppendByte(sb, 0x80 | (codePoint & 0x3F));
        }
        else if (codePoint < 0x10000)
        {
            AppendByte(sb, 0xE0 | (codePoint >> 12));
            AppendByte(sb, 0x80 | ((codePoint >> 6) & 0x3F));
            AppendByte(sb, 0x80 | (codePoint & 0x3F));
        }
        else
        {
            AppendByte(sb, 0xF0 | (codePoint >> 18));
            AppendByte(sb, 0x80 | ((codePoint >> 12) & 0x3F));
            AppendByte(sb, 0x80 | ((codePoint >> 6) & 0x3F));
            AppendByte(sb, 0x80 | (codePoint & 0x3F));
        }
    }

    private static void AppendByte(StringBuilder sb, int b)
    {
        const string hex = "0123456789ABCDEF";
        sb.Append('%');
        sb.Append(hex[(b >> 4) & 0xF]);
        sb.Append(hex[b & 0xF]);
    }
}
