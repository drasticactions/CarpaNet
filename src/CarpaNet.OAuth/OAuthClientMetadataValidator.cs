// Ported from atproto/packages/oauth/oauth-types/src/{oauth-client-metadata,oauth-redirect-uri,uri,util,oauth-scope,
// atproto-oauth-scope,oauth-client-id-discoverable}.ts, atproto/packages/oauth/oauth-client/src/validate-client-metadata.ts
// and atproto/packages/oauth/oauth-provider/src/client/client-manager.ts (validateClientMetadata,
// validateLoopbackClientMetadata, validateDiscoverableClientMetadata) @ a7c8604, by way of SocialApp's port.
using System;
using System.Collections.Generic;
using System.Linq;
using CarpaNet.OAuth.Scopes;

namespace CarpaNet.OAuth;

/// <summary>
/// The atproto OAuth client metadata rules: the <c>oauth-types</c> schema, the OAuth client's
/// <c>validateClientMetadata</c>, and the authorization server's atproto-specific checks
/// (<c>ClientManager.validateClientMetadata</c> and the loopback and discoverable client rules).
/// </summary>
/// <remarks>
/// The rules that need a client keyset (for <c>private_key_jwt</c>) only check that the document
/// declares a signing algorithm and a JWKS, since a document does not carry the private keys.
/// </remarks>
public static class OAuthClientMetadataValidator
{
    private static readonly string[] KnownGrantTypes =
    {
        "authorization_code", "implicit", "refresh_token", "password", "client_credentials",
        "urn:ietf:params:oauth:grant-type:jwt-bearer", "urn:ietf:params:oauth:grant-type:saml2-bearer",
    };

    private static readonly string[] KnownResponseTypes =
    {
        "code", "token", "none", "code id_token token", "code id_token", "code token", "id_token token", "id_token",
    };

    private static readonly string[] KnownAuthMethods =
    {
        "client_secret_basic", "client_secret_jwt", "client_secret_post", "none", "private_key_jwt",
        "self_signed_tls_client_auth", "tls_client_auth",
    };

    /// <summary>
    /// Validates <paramref name="metadata"/>.
    /// </summary>
    /// <param name="metadata">The client metadata.</param>
    /// <exception cref="InvalidOAuthClientMetadataException">The first broken rule.</exception>
    public static void Validate(OAuthClientMetadata metadata)
    {
        var error = Check(metadata);
        if (error != null)
        {
            throw new InvalidOAuthClientMetadataException(error);
        }
    }

    /// <summary>
    /// Returns the first broken rule, or null when <paramref name="metadata"/> is valid.
    /// </summary>
    /// <param name="metadata">The client metadata.</param>
    /// <returns>The error message, or null.</returns>
    public static string? Check(OAuthClientMetadata metadata)
    {
        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        // Schema defaults (RFC 7591 / OIDC registration), as applied by oauthClientMetadataSchema.
        IReadOnlyList<string>? responseTypes = metadata.ResponseTypes ?? new[] { "code" };
        IReadOnlyList<string>? grantTypes = metadata.GrantTypes ?? new[] { "authorization_code" };
        var authMethod = metadata.TokenEndpointAuthMethod ?? "client_secret_basic";
        var applicationType = metadata.ApplicationType ?? OAuthApplicationType.Web;

        // --- oauth-types oauthClientMetadataSchema ---
        if (metadata.RedirectUris == null || metadata.RedirectUris.Length == 0)
        {
            return "At least one redirect_uri is required";
        }

        foreach (var uri in metadata.RedirectUris)
        {
            var redirectError = CheckRedirectUri(uri);
            if (redirectError != null)
            {
                return redirectError;
            }
        }

        if (responseTypes.Count == 0 || responseTypes.Any(t => !KnownResponseTypes.Contains(t)))
        {
            return "Invalid response_types";
        }

        if (grantTypes.Count == 0 || grantTypes.Any(t => !KnownGrantTypes.Contains(t)))
        {
            return "Invalid grant_types";
        }

        if (metadata.Scope != null && !IsOAuthScope(metadata.Scope))
        {
            return "Invalid OAuth scope";
        }

        if (!KnownAuthMethods.Contains(authMethod))
        {
            return "Invalid token_endpoint_auth_method";
        }

        if (applicationType != OAuthApplicationType.Web && applicationType != OAuthApplicationType.Native)
        {
            return "application_type must be \"web\" or \"native\"";
        }

        if (string.IsNullOrEmpty(metadata.ClientId))
        {
            return "client_id is required";
        }

        foreach (var field in new[]
                 {
                     new KeyValuePair<string, string?>("client_uri", metadata.ClientUri),
                     new KeyValuePair<string, string?>("policy_uri", metadata.PolicyUri),
                     new KeyValuePair<string, string?>("tos_uri", metadata.TosUri),
                     new KeyValuePair<string, string?>("logo_uri", metadata.LogoUri),
                     new KeyValuePair<string, string?>("jwks_uri", metadata.JwksUri),
                 })
        {
            if (field.Value != null)
            {
                var uriError = CheckWebUri(field.Value);
                if (uriError != null)
                {
                    return $"{field.Key}: {uriError}";
                }
            }
        }

        // --- oauth-client validateClientMetadata ---
        var isLoopbackId = metadata.ClientId.StartsWith("http:", StringComparison.Ordinal);
        if (isLoopbackId)
        {
            if (AtprotoLoopbackClientId.TryParse(metadata.ClientId, out var idError) == null)
            {
                return $"Invalid loopback client ID: {idError}";
            }
        }
        else
        {
            var idError = CheckDiscoverableClientId(metadata.ClientId);
            if (idError != null)
            {
                return idError;
            }
        }

        // CarpaNet's own scope parser: it keeps every value as-is, so "atproto" must be one of them.
        var scopeSet = ScopeSet.Parse(metadata.Scope);
        if (metadata.Scope == null || !scopeSet.Contains(AtprotoScope.Atproto))
        {
            return "Client metadata must include the \"atproto\" scope";
        }

        if (!responseTypes.Contains("code"))
        {
            return "\"response_types\" must include \"code\"";
        }

        if (!grantTypes.Contains("authorization_code"))
        {
            return "\"grant_types\" must include \"authorization_code\"";
        }

        switch (authMethod)
        {
            case "none":
                if (metadata.TokenEndpointAuthSigningAlg != null)
                {
                    return "\"token_endpoint_auth_signing_alg\" must not be provided when \"token_endpoint_auth_method\" is \"none\"";
                }

                break;
            case "private_key_jwt":
                if (string.IsNullOrEmpty(metadata.TokenEndpointAuthSigningAlg))
                {
                    return "\"token_endpoint_auth_signing_alg\" must be provided when \"token_endpoint_auth_method\" is \"private_key_jwt\"";
                }

                if (metadata.Jwks == null && metadata.JwksUri == null)
                {
                    return "Client authentication method \"private_key_jwt\" requires a JWKS";
                }

                break;
            default:
                return $"Unsupported \"token_endpoint_auth_method\" value: {authMethod}";
        }

        // --- oauth-provider ClientManager.validateClientMetadata (atproto rules) ---
        if (metadata.Jwks != null && metadata.JwksUri != null)
        {
            return "jwks_uri and jwks are mutually exclusive";
        }

        if (metadata.ClientUri != null && WebUrl.TryParse(metadata.ClientUri, out var clientUriUrl) && IsLocalHostname(clientUriUrl!.Hostname))
        {
            return "client_uri hostname is invalid";
        }

        var scopes = metadata.Scope.Split(' ');
        if (scopeSet.Count != scopes.Length)
        {
            return $"Duplicate scope \"{FirstDuplicate(scopes)}\"";
        }

        var duplicateGrant = FirstDuplicate(grantTypes);
        if (duplicateGrant != null)
        {
            return $"Duplicate grant type \"{duplicateGrant}\"";
        }

        foreach (var grantType in grantTypes)
        {
            switch (grantType)
            {
                case "implicit":
                    return $"Grant type \"{grantType}\" is not allowed";
                case "authorization_code":
                case "refresh_token":
                    break;
                default:
                    return $"Grant type \"{grantType}\" is not supported";
            }
        }

        if (authMethod == "private_key_jwt" && metadata.Jwks != null && (metadata.Jwks.Keys == null || metadata.Jwks.Keys.Length == 0))
        {
            return "private_key_jwt auth method requires at least one key in jwks";
        }

        if (!metadata.DpopBoundAccessTokens)
        {
            return "\"dpop_bound_access_tokens\" must be true";
        }

        // Every scope value that names a known permission must be well formed.
        var scopeError = CheckScopeValues(scopeSet);
        if (scopeError != null)
        {
            return scopeError;
        }

        if (applicationType == OAuthApplicationType.Native && authMethod != "none")
        {
            return "Native clients must authenticate using \"none\" method";
        }

        foreach (var redirectUri in metadata.RedirectUris)
        {
            var redirectError = CheckRedirectUriForClient(redirectUri, applicationType);
            if (redirectError != null)
            {
                return redirectError;
            }
        }

        if (isLoopbackId)
        {
            if (metadata.ClientUri != null)
            {
                return "client_uri is not allowed for loopback clients";
            }

            if (applicationType != OAuthApplicationType.Native)
            {
                return "Loopback clients must have application_type \"native\"";
            }

            if (authMethod != "none")
            {
                return $"Loopback clients are not allowed to use \"token_endpoint_auth_method\" {authMethod}";
            }

            return null;
        }

        // Discoverable client.
        var clientIdUrl = WebUrl.Parse(metadata.ClientId);
        if (metadata.ClientUri != null)
        {
            var clientUri = WebUrl.Parse(metadata.ClientUri);
            if (clientUri.Origin != clientIdUrl.Origin)
            {
                return "client_uri must have the same origin as the client_id";
            }

            if (clientIdUrl.Pathname != clientUri.Pathname)
            {
                var parent = clientUri.Pathname.EndsWith("/", StringComparison.Ordinal) ? clientUri.Pathname : clientUri.Pathname + "/";
                if (!clientIdUrl.Pathname.StartsWith(parent, StringComparison.Ordinal))
                {
                    return "client_uri must be a parent URL of the client_id";
                }
            }
        }

        foreach (var redirectUri in metadata.RedirectUris)
        {
            var url = WebUrl.Parse(redirectUri);
            if (url.Protocol.IndexOf('.') >= 0)
            {
                var protocol = ReverseDomain(clientIdUrl.Hostname) + ":";
                if (url.Protocol != protocol)
                {
                    return $"Private-Use URI Scheme redirect URI, for discoverable client metadata, must be the fully qualified domain name (FQDN) of the client_id, in reverse order ({protocol})";
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a <c>scope</c> on its own, as a client would before using it: the OAuth scope syntax, the
    /// <c>atproto</c> scope, no duplicates, and every <c>include:</c>, <c>rpc:</c>, <c>repo:</c>,
    /// <c>blob:</c>, <c>account:</c> and <c>identity:</c> value well formed (parsed with
    /// <see cref="ScopeSet.Parse"/> and the permission parsers). Unknown values are allowed.
    /// </summary>
    /// <param name="scope">The space-separated scope.</param>
    /// <returns>The error message, or null when the scope is valid.</returns>
    public static string? CheckScope(string? scope)
    {
        if (string.IsNullOrEmpty(scope))
        {
            return "Missing scope property";
        }

        if (!IsOAuthScope(scope!))
        {
            return "Invalid OAuth scope";
        }

        var set = ScopeSet.Parse(scope);
        if (!set.Contains(AtprotoScope.Atproto))
        {
            return "Missing \"atproto\" scope";
        }

        var values = scope!.Split(' ');
        if (set.Count != values.Length)
        {
            return $"Duplicate scope \"{FirstDuplicate(values)}\"";
        }

        return CheckScopeValues(set);
    }

    /// <summary>
    /// <c>OAUTH_SCOPE_REGEXP</c>: single-space separated tokens of printable ASCII characters other
    /// than <c>"</c> and <c>\</c> (RFC 6749 section 3.3).
    /// </summary>
    internal static bool IsOAuthScope(string input) => AtprotoLoopbackClientId.IsOAuthScope(input);

    /// <summary>
    /// <c>oauthRedirectUriSchema</c>: an https URI, a loopback IP URI or a private-use scheme URI.
    /// </summary>
    internal static string? CheckRedirectUri(string value)
    {
        if (value == null)
        {
            return "Invalid URL";
        }

        if (value.StartsWith("https:", StringComparison.Ordinal))
        {
            return CheckHttpsUri(value);
        }

        if (value.StartsWith("http:", StringComparison.Ordinal))
        {
            return CheckLoopbackRedirectUri(value);
        }

        if (HasPrivateUseSchemePrefix(value))
        {
            return CheckPrivateUseUri(value);
        }

        return "URL must use the \"https:\" or \"http:\" protocol, or a private-use URI scheme (RFC 8252)";
    }

    /// <summary>
    /// <c>loopbackRedirectURISchema</c>: a loopback URI that does not use <c>localhost</c>.
    /// </summary>
    internal static string? CheckLoopbackRedirectUri(string value)
    {
        var error = CheckLoopbackUri(value);
        if (error != null)
        {
            return error;
        }

        return value.StartsWith("http://localhost", StringComparison.Ordinal)
            ? "Use of \"localhost\" hostname is not allowed (RFC 8252), use a loopback IP such as \"127.0.0.1\" instead"
            : null;
    }

    /// <summary>
    /// <c>webUriSchema</c>: a loopback http URI or an https URI.
    /// </summary>
    internal static string? CheckWebUri(string value)
    {
        if (value.StartsWith("http://", StringComparison.Ordinal))
        {
            return CheckLoopbackUri(value);
        }

        if (value.StartsWith("https://", StringComparison.Ordinal))
        {
            return CheckHttpsUri(value);
        }

        return "URL must use the \"http:\" or \"https:\" protocol";
    }

    /// <summary>
    /// <c>oauthClientIdDiscoverableSchema</c>: the rules for a hosted client ID URL.
    /// </summary>
    internal static string? CheckDiscoverableClientId(string value)
    {
        var error = CheckHttpsUri(value);
        if (error != null)
        {
            return error;
        }

        var url = WebUrl.Parse(value);
        if (url.HasCredentials)
        {
            return "ClientID must not contain credentials";
        }

        if (url.Hash.Length > 0)
        {
            return "ClientID must not contain a fragment";
        }

        if (url.Pathname == "/")
        {
            return "ClientID must contain a path component (e.g. \"/client-metadata.json\")";
        }

        if (url.Pathname.EndsWith("/", StringComparison.Ordinal))
        {
            return "ClientID path must not end with a trailing slash";
        }

        if (IsHostnameIP(url.Hostname))
        {
            return "ClientID hostname must not be an IP address";
        }

        if (ExtractUrlPath(value) != url.Pathname)
        {
            return $"ClientID must be in canonical form (\"{url.Href}\", got \"{value}\")";
        }

        return null;
    }

    private static string? CheckScopeValues(ScopeSet set)
    {
        foreach (var value in set)
        {
            bool valid;
            switch (Prefix(value))
            {
                case IncludeScope.Prefix:
                    valid = IncludeScope.TryParse(value, out _);
                    break;
                case RpcPermission.Prefix:
                    valid = RpcPermission.TryParse(value, out _);
                    break;
                case RepoPermission.Prefix:
                    valid = RepoPermission.TryParse(value, out _);
                    break;
                case BlobPermission.Prefix:
                    valid = BlobPermission.TryParse(value, out _);
                    break;
                case AccountPermission.Prefix:
                    valid = AccountPermission.TryParse(value, out _);
                    break;
                case IdentityPermission.Prefix:
                    valid = IdentityPermission.TryParse(value, out _);
                    break;
                default:
                    valid = true;
                    break;
            }

            if (!valid)
            {
                return $"Invalid scope \"{value}\"";
            }
        }

        return null;
    }

    private static string Prefix(string value)
    {
        var end = value.IndexOfAny(new[] { ':', '?' });
        return end < 0 ? value : value.Substring(0, end);
    }

    private static string? CheckRedirectUriForClient(string redirectUri, string applicationType)
    {
        if (!WebUrl.TryParse(redirectUri, out var url))
        {
            return $"Invalid redirect URI {redirectUri}";
        }

        if (url!.HasCredentials)
        {
            return $"Redirect URI {url.Href} must not contain credentials";
        }

        if (url.Hostname == "localhost")
        {
            return $"Loopback redirect URI {url.Href} is not allowed (use explicit IPs instead)";
        }

        if (url.Hostname == "127.0.0.1" || url.Hostname == "[::1]")
        {
            if (applicationType != OAuthApplicationType.Native)
            {
                return "Loopback redirect URIs are only allowed for native apps";
            }

            return url.Protocol != "http:" ? $"Loopback redirect URI {url.Href} must use HTTP" : null;
        }

        if (url.Protocol == "http:")
        {
            return "Only loopback redirect URIs are allowed to use the \"http\" scheme";
        }

        if (url.Protocol == "https:")
        {
            return IsLocalHostname(url.Hostname)
                ? $"Redirect URI \"{url.Href}\"'s domain name must not be a local hostname"
                : null;
        }

        if (url.Protocol.IndexOf('.') >= 0)
        {
            return applicationType != OAuthApplicationType.Native
                ? "Private-Use URI Scheme redirect URI are only allowed for native apps"
                : null;
        }

        return $"Invalid redirect URI scheme \"{url.Protocol}\"";
    }

    private static string? CheckDangerousUri(string value, out WebUrl? url)
    {
        url = null;
        return value.IndexOf(':') >= 0 && WebUrl.TryParse(value, out url) ? null : "Invalid URL";
    }

    private static string? CheckLoopbackUri(string value)
    {
        var error = CheckDangerousUri(value, out var url);
        if (error != null)
        {
            return error;
        }

        if (!value.StartsWith("http://", StringComparison.Ordinal))
        {
            return "URL must use the \"http:\" protocol";
        }

        return IsLoopbackHost(url!.Hostname) ? null : "URL must use \"localhost\", \"127.0.0.1\" or \"[::1]\" as hostname";
    }

    private static string? CheckHttpsUri(string value)
    {
        var error = CheckDangerousUri(value, out var url);
        if (error != null)
        {
            return error;
        }

        if (!value.StartsWith("https://", StringComparison.Ordinal))
        {
            return "URL must use the \"https:\" protocol";
        }

        var hostname = url!.Hostname;
        if (IsLoopbackHost(hostname))
        {
            return "https: URL must not use a loopback host";
        }

        if (!IsHostnameIP(hostname))
        {
            if (hostname.IndexOf('.') < 0)
            {
                return "Domain name must contain at least two segments";
            }

            if (hostname.EndsWith(".local", StringComparison.Ordinal))
            {
                return "Domain name must not end with \".local\"";
            }
        }

        return null;
    }

    private static string? CheckPrivateUseUri(string value)
    {
        var error = CheckDangerousUri(value, out var url);
        if (error != null)
        {
            return error;
        }

        var dotIdx = value.IndexOf('.');
        var colonIdx = value.IndexOf(':');
        if (dotIdx == -1 || colonIdx == -1 || dotIdx > colonIdx)
        {
            return "Private-use URI scheme requires a \".\" as part of the protocol";
        }

        if (url!.Protocol.IndexOf('.') < 0)
        {
            return "Invalid private-use URI scheme";
        }

        var scheme = url.Protocol.Substring(0, url.Protocol.Length - 1);
        var domain = ReverseDomain(scheme);
        string? issue = null;
        if (IsLocalHostname(domain))
        {
            issue = "Private-use URI Scheme redirect URI must not be a local hostname";
        }

        // <scheme>:/{path}: no authority ("//"), so no credentials, host or port.
        if (string.CompareOrdinal(value, colonIdx + 1, "//", 0, 2) == 0 ||
            url.HasCredentials || url.Hostname.Length > 0 || url.HasPort)
        {
            return "Private-Use URI Scheme must be in the form <scheme>:/{path} (notice the single slash!) as per RFC 8252";
        }

        return issue;
    }

    // ^[^.:]+(?:\.[^.:]+)+:
    private static bool HasPrivateUseSchemePrefix(string value)
    {
        var colonIdx = value.IndexOf(':');
        if (colonIdx <= 0)
        {
            return false;
        }

        var segments = value.Substring(0, colonIdx).Split('.');
        return segments.Length >= 2 && segments.All(s => s.Length > 0);
    }

    // ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ or a bracketed IPv6 address.
    private static bool IsHostnameIP(string hostname)
    {
        if (hostname.StartsWith("[", StringComparison.Ordinal) && hostname.EndsWith("]", StringComparison.Ordinal))
        {
            return true;
        }

        var parts = hostname.Split('.');
        return parts.Length == 4 && parts.All(p => p.Length > 0 && p.All(c => c >= '0' && c <= '9'));
    }

    private static bool IsLoopbackHost(string host) => host == "localhost" || host == "127.0.0.1" || host == "[::1]";

    private static bool IsLocalHostname(string hostname)
    {
        var parts = hostname.Split('.');
        if (parts.Length < 2)
        {
            return true;
        }

        switch (parts[parts.Length - 1].ToLowerInvariant())
        {
            case "test":
            case "local":
            case "localhost":
            case "invalid":
            case "example":
                return true;
            default:
                return false;
        }
    }

    private static string ReverseDomain(string domain) => string.Join(".", Enumerable.Reverse(domain.Split('.')));

    private static string? FirstDuplicate(IReadOnlyList<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                return value;
            }
        }

        return null;
    }

    // extractUrlPath: the path of an http(s) URL without the URL parser's normalization.
    private static string ExtractUrlPath(string url)
    {
        var endOfProtocol = url.StartsWith("https://", StringComparison.Ordinal) ? 8
            : url.StartsWith("http://", StringComparison.Ordinal) ? 7
            : -1;
        if (endOfProtocol == -1)
        {
            throw new ArgumentException("URL must use the \"https:\" or \"http:\" protocol", nameof(url));
        }

        var hashIdx = url.IndexOf('#', endOfProtocol);
        var questionIdx = url.IndexOf('?', endOfProtocol);
        var queryStrIdx = questionIdx != -1 && (hashIdx == -1 || questionIdx < hashIdx) ? questionIdx : -1;
        var pathEnd = hashIdx == -1
            ? queryStrIdx == -1 ? url.Length : queryStrIdx
            : queryStrIdx == -1 ? hashIdx : Math.Min(hashIdx, queryStrIdx);
        var slashIdx = url.IndexOf('/', endOfProtocol);
        var pathStart = slashIdx == -1 || slashIdx > pathEnd ? pathEnd : slashIdx;
        if (endOfProtocol == pathStart)
        {
            throw new ArgumentException("URL must contain a host", nameof(url));
        }

        return url.Substring(pathStart, pathEnd - pathStart);
    }

    /// <summary>
    /// The parts of a URL the rules read, with the WHATWG URL names, taken from <see cref="Uri"/>.
    /// </summary>
    private sealed class WebUrl
    {
        private WebUrl(Uri uri)
        {
            Protocol = uri.Scheme + ":";
            Hostname = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;
            Pathname = uri.AbsolutePath;
            Hash = uri.Fragment == "#" ? string.Empty : uri.Fragment;
            HasCredentials = uri.UserInfo.Length > 0;
            HasPort = !uri.IsDefaultPort;
            Href = uri.AbsoluteUri;
            Origin = Protocol + "//" + uri.Authority;
        }

        /// <summary>The scheme followed by <c>:</c>, lowercase.</summary>
        public string Protocol { get; }

        /// <summary>The host (ASCII; IPv6 in brackets), or empty when the URL has no authority.</summary>
        public string Hostname { get; }

        /// <summary>The path.</summary>
        public string Pathname { get; }

        /// <summary>The fragment with its <c>#</c>, or empty.</summary>
        public string Hash { get; }

        /// <summary>Whether the URL has a user name or password.</summary>
        public bool HasCredentials { get; }

        /// <summary>Whether the URL has a port that is not the scheme's default.</summary>
        public bool HasPort { get; }

        /// <summary>The serialized URL.</summary>
        public string Href { get; }

        /// <summary>The scheme, host and (non-default) port.</summary>
        public string Origin { get; }

        public static bool TryParse(string value, out WebUrl? url)
        {
            url = null;
            if (value == null || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.IsUnc)
            {
                return false;
            }

            url = new WebUrl(uri);
            return true;
        }

        public static WebUrl Parse(string value) =>
            TryParse(value, out var url) ? url! : throw new FormatException($"Invalid URL: {value}");
    }
}
