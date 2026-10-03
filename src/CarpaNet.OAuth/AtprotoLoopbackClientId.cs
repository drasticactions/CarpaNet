// Ported from atproto/packages/oauth/oauth-types/src/{atproto-loopback-client-id,oauth-client-id-loopback,
// atproto-loopback-client-redirect-uris,oauth-redirect-uri,uri}.ts
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CarpaNet.OAuth;

/// <summary>
/// The parameters of an atproto loopback client ID.
/// </summary>
public sealed class AtprotoLoopbackClientIdParams
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AtprotoLoopbackClientIdParams"/> class.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="redirectUris">The redirect URIs.</param>
    public AtprotoLoopbackClientIdParams(string scope, IReadOnlyList<string> redirectUris)
    {
        Scope = scope;
        RedirectUris = redirectUris;
    }

    /// <summary>
    /// Gets the scope (default <c>atproto</c>).
    /// </summary>
    public string Scope { get; }

    /// <summary>
    /// Gets the redirect URIs (default <c>http://127.0.0.1/</c> and <c>http://[::1]/</c>).
    /// </summary>
    public IReadOnlyList<string> RedirectUris { get; }
}

/// <summary>
/// Development ("loopback") client IDs for native apps that do not host client metadata.
/// The client ID is <c>http://localhost</c> (no port, no path) with optional <c>scope</c> and
/// repeatable <c>redirect_uri</c> query parameters; the authorization server derives the client
/// metadata from it. Redirect URIs must use a loopback IP (<c>127.0.0.1</c> or <c>[::1]</c>), not <c>localhost</c>.
/// </summary>
public static class AtprotoLoopbackClientId
{
    /// <summary>
    /// The loopback client ID origin.
    /// </summary>
    public const string Origin = "http://localhost";

    /// <summary>
    /// The default scope of a loopback client.
    /// </summary>
    public const string DefaultScope = "atproto";

    /// <summary>
    /// Gets the default redirect URIs of a loopback client.
    /// </summary>
    public static IReadOnlyList<string> DefaultRedirectUris { get; } = new[] { "http://127.0.0.1/", "http://[::1]/" };

    /// <summary>
    /// Builds a loopback client ID (<c>buildAtprotoLoopbackClientId</c>): <c>http://localhost</c>, with the
    /// scope when it is not <c>atproto</c> and the redirect URIs when they are not the defaults.
    /// </summary>
    /// <param name="scope">The scope the client will request, or null for <c>atproto</c>. Must contain <c>atproto</c>.</param>
    /// <param name="redirectUris">The redirect URIs, or null for the defaults.</param>
    /// <returns>The client ID.</returns>
    /// <exception cref="ArgumentException">
    /// The scope is invalid or lacks <c>atproto</c>, the redirect URI list is empty, or a redirect URI is not a loopback IP URI.
    /// </exception>
    public static string Build(string? scope = null, IEnumerable<string>? redirectUris = null)
    {
        var parameters = new List<KeyValuePair<string, string>>();

        if (scope != null && scope != DefaultScope)
        {
            if (!IsAtprotoOAuthScope(scope))
            {
                throw new ArgumentException("Value must contain \"atproto\" scope value", nameof(scope));
            }

            parameters.Add(new KeyValuePair<string, string>("scope", scope));
        }

        var uris = redirectUris?.ToList();
        if (uris != null && !SetEquivalent(uris, DefaultRedirectUris))
        {
            if (uris.Count == 0)
            {
                throw new ArgumentException("Unexpected empty \"redirect_uris\" config", nameof(redirectUris));
            }

            foreach (var uri in uris)
            {
                var error = CheckLoopbackRedirectUri(uri);
                if (error != null)
                {
                    throw new ArgumentException(error, nameof(redirectUris));
                }

                parameters.Add(new KeyValuePair<string, string>("redirect_uri", uri));
            }
        }

        if (parameters.Count == 0)
        {
            return Origin;
        }

        var sb = new StringBuilder(Origin).Append('?');
        for (var i = 0; i < parameters.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('&');
            }

            sb.Append(FormEncode(parameters[i].Key)).Append('=').Append(FormEncode(parameters[i].Value));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns true when <paramref name="clientId"/> is a valid atproto loopback client ID.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <returns>True if valid.</returns>
    public static bool IsLoopbackClientId(string clientId) => TryParse(clientId, out _) != null;

    /// <summary>
    /// Parses a loopback client ID (<c>parseAtprotoLoopbackClientId</c>), filling in the defaults.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <param name="error">The reason the ID is invalid, or null.</param>
    /// <returns>The parameters, or null when the ID is invalid.</returns>
    public static AtprotoLoopbackClientIdParams? TryParse(string clientId, out string? error)
    {
        if (clientId == null)
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        error = null;
        if (!clientId.StartsWith(Origin, StringComparison.Ordinal))
        {
            error = $"Value must start with \"{Origin}\"";
            return null;
        }

        if (clientId.IndexOf('#', Origin.Length) >= 0)
        {
            error = "Value must not contain a hash component";
            return null;
        }

        // No path is allowed except a single "/", so the query string starts right after the origin (+1 for "/")
        var queryStringIdx = clientId.Length > Origin.Length && clientId[Origin.Length] == '/'
            ? Origin.Length + 1
            : Origin.Length;

        if (clientId.Length != queryStringIdx && clientId[queryStringIdx] != '?')
        {
            error = "Value must not contain a path component";
            return null;
        }

        string? scope = null;
        List<string>? redirects = null;
        var query = clientId.Length > queryStringIdx ? clientId.Substring(queryStringIdx + 1) : string.Empty;

        foreach (var pair in ParseQuery(query))
        {
            if (pair.Key == "scope")
            {
                if (scope != null)
                {
                    error = "Duplicate \"scope\" query parameter";
                    return null;
                }

                if (!IsOAuthScope(pair.Value))
                {
                    error = "Invalid \"scope\" query parameter: Invalid OAuth scope";
                    return null;
                }

                scope = pair.Value;
            }
            else if (pair.Key == "redirect_uri")
            {
                var redirectError = CheckLoopbackRedirectUri(pair.Value);
                if (redirectError != null)
                {
                    error = $"Invalid \"redirect_uri\" query parameter: {redirectError}";
                    return null;
                }

                (redirects ??= new List<string>()).Add(pair.Value);
            }
            else
            {
                error = $"Unexpected query parameter \"{pair.Key}\"";
                return null;
            }
        }

        scope ??= DefaultScope;
        if (!IsAtprotoOAuthScope(scope))
        {
            error = "ATProto Loopback ClientID must include \"atproto\" scope";
            return null;
        }

        return new AtprotoLoopbackClientIdParams(scope, redirects ?? DefaultRedirectUris.ToList());
    }

    /// <summary>
    /// Checks a loopback redirect URI (<c>oauthLoopbackClientRedirectUriSchema</c>): an <c>http:</c> URI whose
    /// host is <c>127.0.0.1</c> or <c>[::1]</c>. The <c>localhost</c> hostname is not allowed (RFC 8252).
    /// </summary>
    /// <param name="value">The redirect URI.</param>
    /// <returns>The error message, or null when the URI is valid.</returns>
    public static string? CheckLoopbackRedirectUri(string value)
    {
        if (value == null || !value.StartsWith("http://", StringComparison.Ordinal))
        {
            return "URL must use the \"http:\" protocol";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return "Invalid URL";
        }

        var host = uri.Host;
        if (host != "localhost" && host != "127.0.0.1" && host != "[::1]")
        {
            return "URL must use \"localhost\", \"127.0.0.1\" or \"[::1]\" as hostname";
        }

        if (value.StartsWith("http://localhost", StringComparison.Ordinal))
        {
            return "Use of \"localhost\" hostname is not allowed (RFC 8252), use a loopback IP such as \"127.0.0.1\" instead";
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="scope"/> is a valid OAuth scope string (RFC 6749 section 3.3):
    /// space-separated tokens of printable ASCII characters other than <c>"</c> and <c>\</c>.
    /// </summary>
    internal static bool IsOAuthScope(string scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            return false;
        }

        foreach (var token in scope.Split(' '))
        {
            if (token.Length == 0)
            {
                return false;
            }

            foreach (var c in token)
            {
                if (c < 0x21 || c > 0x7E || c == '"' || c == '\\')
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="scope"/> is a valid OAuth scope containing <c>atproto</c>.
    /// </summary>
    internal static bool IsAtprotoOAuthScope(string scope) =>
        IsOAuthScope(scope) && scope.Split(' ').Contains(DefaultScope);

    private static bool SetEquivalent(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.All(b.Contains) && b.All(a.Contains);

    /// <summary>
    /// application/x-www-form-urlencoded encoding, as done by the URL standard's URLSearchParams.
    /// </summary>
    private static string FormEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '*' || c == '-' || c == '.' || c == '_')
            {
                sb.Append(c);
            }
            else if (c == ' ')
            {
                sb.Append('+');
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// application/x-www-form-urlencoded parsing, as done by the URL standard's URLSearchParams.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        foreach (var part in query.Split('&'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var eq = part.IndexOf('=');
            var name = eq >= 0 ? part.Substring(0, eq) : part;
            var value = eq >= 0 ? part.Substring(eq + 1) : string.Empty;
            yield return new KeyValuePair<string, string>(FormDecode(name), FormDecode(value));
        }
    }

    private static string FormDecode(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '+')
            {
                bytes.Add((byte)' ');
            }
            else if (c == '%' && i + 2 < value.Length && IsHex(value[i + 1]) && IsHex(value[i + 2]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value.Substring(i, 2)));
                i++;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
