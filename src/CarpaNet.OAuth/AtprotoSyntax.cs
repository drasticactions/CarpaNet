using System;

namespace CarpaNet.OAuth;

/// <summary>
/// Allocation-free syntax checks for atproto identifiers, mirroring the validation rules of the
/// reference TypeScript packages (<c>@atproto/did</c> and <c>@atproto/syntax</c>).
/// </summary>
internal static class AtprotoSyntax
{
    private const string DidPlcPrefix = "did:plc:";
    private const string DidWebPrefix = "did:web:";
    private const int DidPlcLength = 32;

    /// <summary>
    /// Whether the value is a DID using one of the atproto blessed methods (<c>did:plc</c> or <c>did:web</c>).
    /// </summary>
    public static bool IsAtprotoDid(string? value)
    {
        if (value == null)
        {
            return false;
        }

        if (value.StartsWith(DidPlcPrefix, StringComparison.Ordinal))
        {
            return IsDidPlc(value);
        }

        if (value.StartsWith(DidWebPrefix, StringComparison.Ordinal))
        {
            return IsAtprotoDidWeb(value);
        }

        return false;
    }

    /// <summary>
    /// Whether the value is an absolute DID reference (<c>did:...#fragment</c>) whose DID uses an atproto method.
    /// </summary>
    public static bool IsAtprotoDidRefAbsolute(string? value)
    {
        if (value == null)
        {
            return false;
        }

        var hashIndex = value.IndexOf('#');
        if (hashIndex == -1 || hashIndex == value.Length - 1)
        {
            return false; // No fragment, or empty fragment
        }

        if (value.IndexOf('#', hashIndex + 1) != -1)
        {
            return false; // More than one '#'
        }

        return IsFragment(value, hashIndex + 1, value.Length) &&
               IsAtprotoDid(value.Substring(0, hashIndex));
    }

    /// <summary>
    /// Whether the value is a syntactically valid NSID.
    /// </summary>
    public static bool IsNsid(string? value)
    {
        if (value == null || value.Length < 5 || value.Length > 253 + 1 + 63)
        {
            return false;
        }

        var segmentCount = 0;
        var segmentStart = 0;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i < value.Length && value[i] != '.')
            {
                var c = value[i];
                if (!IsAsciiLetterOrDigit(c) && c != '-')
                {
                    return false;
                }

                continue;
            }

            var length = i - segmentStart;
            if (length < 1 || length > 63)
            {
                return false;
            }

            var first = value[segmentStart];
            var last = value[i - 1];
            if (first == '-' || last == '-')
            {
                return false;
            }

            if (segmentCount == 0 && IsAsciiDigit(first))
            {
                return false; // First segment may not start with a digit
            }

            if (i == value.Length)
            {
                // Name segment: letters and digits only, no leading digit
                if (IsAsciiDigit(first) || value.IndexOf('-', segmentStart) != -1)
                {
                    return false;
                }
            }

            segmentCount++;
            segmentStart = i + 1;
        }

        return segmentCount >= 3;
    }

    private static bool IsDidPlc(string value)
    {
        if (value.Length != DidPlcLength)
        {
            return false;
        }

        for (var i = DidPlcPrefix.Length; i < DidPlcLength; i++)
        {
            var c = value[i];
            if (!((c >= 'a' && c <= 'z') || (c >= '2' && c <= '7')))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAtprotoDidWeb(string value)
    {
        var start = DidWebPrefix.Length;
        if (value.Length > 2048 || value.Length == start || value[start] == ':')
        {
            return false;
        }

        // Method-specific identifier characters (DID spec)
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
            {
                continue;
            }

            if (c == ':')
            {
                // Atproto does not allow path components in Web DIDs
                return false;
            }

            if (c == '%')
            {
                if (i + 2 >= value.Length ||
                    !IsUpperHexDigit(value[i + 1]) ||
                    !IsUpperHexDigit(value[i + 2]))
                {
                    return false;
                }

                i += 2;
                continue;
            }

            return false;
        }

        // Atproto does not allow port numbers in Web DIDs, except for localhost
        var isLocalhost = string.Equals(value, "did:web:localhost", StringComparison.Ordinal) ||
                          value.StartsWith("did:web:localhost%3A", StringComparison.Ordinal);
        if (!isLocalhost && value.IndexOf("%3A", start, StringComparison.Ordinal) != -1)
        {
            return false;
        }

        var host = Uri.UnescapeDataString(value.Substring(start));
        return Uri.TryCreate("https://" + host, UriKind.Absolute, out _);
    }

    private static bool IsFragment(string value, int start, int end)
    {
        for (var i = start; i < end; i++)
        {
            var c = value[i];
            if (IsAsciiLetterOrDigit(c))
            {
                continue;
            }

            switch (c)
            {
                // unreserved
                case '-':
                case '.':
                case '_':
                case '~':
                // sub-delims
                case '!':
                case '$':
                case '&':
                case '\'':
                case '(':
                case ')':
                case '*':
                case '+':
                case ',':
                case ';':
                case '=':
                // pchar extra / fragment extra
                case ':':
                case '@':
                case '/':
                case '?':
                    continue;
                case '%':
                    if (i + 2 >= end || !IsHexDigit(value[i + 1]) || !IsHexDigit(value[i + 2]))
                    {
                        return false;
                    }

                    i += 2;
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }

    internal static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    internal static bool IsAsciiLetterOrDigit(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');

    internal static bool IsHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

    private static bool IsUpperHexDigit(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
}
