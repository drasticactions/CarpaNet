# OAuth Authentication

OAuth is the recommended auth method for user-facing apps. Requires the `CarpaNet.OAuth` package.

## Desktop/Console App Flow

```csharp
using CarpaNet.OAuth;

// 1. Configure with loopback URI for desktop apps
var port = 8080;
var config = new OAuthClientConfig
{
    ClientId = OAuthClientConfig.CreateLoopbackClientId(port, "atproto transition:generic"), // http://localhost?scope=...&redirect_uri=...
    RedirectUri = OAuthClientConfig.CreateLoopbackRedirectUri(port),
    Scope = "atproto transition:generic",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = new MemoryOAuthSessionStore(),
};

// 2. Start the OAuth flow
using var oauthSession = new OAuthSession(config);
var authUrl = await oauthSession.AuthorizeAsync("alice.bsky.social");

// 3. Open browser and listen for callback
Console.WriteLine($"Open: {authUrl}");
// ... start HTTP listener on port, capture callback URL ...

// 4. Exchange code for tokens
ATProtoOAuthClient atClient = await oauthSession.CallbackAsync(callbackUrl);

// 5. Use the authenticated client
var profile = await atClient.AppBskyActorGetProfileAsync(
    new AppBsky.Actor.GetProfileParameters { Actor = new ATHandle("alice.bsky.social") });

// 6. Sign out when done
await atClient.SignOutAsync();
```

## Web App Flow

```csharp
var config = new OAuthClientConfig
{
    ClientId = "https://myapp.example.com/client-metadata.json",
    RedirectUri = "https://myapp.example.com/callback",
    Scope = "atproto transition:generic",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = myPersistentSessionStore,
    StateStore = myPersistentStateStore,
};

using var oauthSession = new OAuthSession(config);
var authUrl = await oauthSession.AuthorizeAsync(userHandle);
// Redirect user to authUrl...

// In callback handler:
var atClient = await oauthSession.CallbackAsync(Request.Url.ToString());
```

## Building the Scope

`OAuthClientConfig.Scope` is a space-separated string. You can build it with `ScopeSet` (namespace `CarpaNet.OAuth.Scopes`). `ScopeSet` formats each permission in the normalized atproto scope syntax.

```csharp
using CarpaNet.OAuth.Scopes;

var scopes = new ScopeSet()
    .AddAtproto()                                         // atproto (required)
    .AddRepo("app.bsky.feed.post", RepoActions.Create)    // repo:app.bsky.feed.post?action=create
    .AddBlob("image/*")                                   // blob:image/*
    .AddRpc("app.bsky.actor.getProfile",
            "did:web:api.bsky.app#bsky_appview")           // rpc:app.bsky.actor.getProfile?aud=did:web:api.bsky.app%23bsky_appview
    .AddAccount(AccountAttribute.Email)                   // account:email
    .AddIdentity(IdentityAttribute.Handle)                // identity:handle
    .AddInclude("com.example.authBasic");                 // include:com.example.authBasic

config.SetScope(scopes); // throws if "atproto" is missing
```

To read or check scopes:

- `AtprotoScope.IsValid(value)` and `AtprotoScope.Normalize(scope)` validate and normalize scope strings.
- `RepoPermission.TryParse`, `RpcPermission.TryParse`, `BlobPermission.TryParse`, `AccountPermission.TryParse`, `IdentityPermission.TryParse` and `IncludeScope.TryParse` parse one scope value.
- `ScopeSet.Parse(grantedScope).MatchesRepo("app.bsky.feed.post", RepoActions.Create)` checks a granted scope. `MatchesRpc`, `MatchesBlob`, `MatchesAccount` and `MatchesIdentity` do the same for the other resources.

The library does not expand `include:` scopes into the permissions of their lexicon permission set.

## Hosting Client Metadata

A production client ID is the https URL of a JSON client metadata document that you host. `OAuthClientMetadata` creates, validates and serializes this document.

- `OAuthClientMetadata.CreatePublicClient(clientId, redirectUris, scope, applicationType)` creates a public client: `token_endpoint_auth_method` `none`, the `authorization_code` and `refresh_token` grants, the `code` response type and `dpop_bound_access_tokens: true`. The scope can be a string or a `ScopeSet`. `applicationType` is `OAuthApplicationType.Native` (the default) or `OAuthApplicationType.Web`.
- After you create the document, set `ClientName`, `ClientUri`, `LogoUri`, `TosUri` and `PolicyUri` as necessary.
- `Validate()` applies the atproto client metadata rules and throws `InvalidOAuthClientMetadataException` with the first broken rule. `TryValidate(out var error)` and `OAuthClientMetadataValidator.Check(metadata)` return the error instead. `OAuthClientMetadataValidator.CheckScope(scope)` checks a scope string only.
- `ToJson()` writes the document. `OAuthClientMetadata.FromJson(json)` reads it. `OAuthClientMetadata.JsonTypeInfo` is the source-generated type info, so serialization is AOT and trim safe.

Some of the rules:

- The client ID path must not be `/` and must not end with `/`. The client ID host must be a domain name, not an IP address.
- `client_uri` must have the same origin as the client ID and must be a parent URL of it.
- A private-use scheme redirect URI must be the client ID host in reverse order, followed by `:/` (for example `com.example.app:/callback` for `app.example.com`). Only native clients can use private-use scheme and loopback (`http://127.0.0.1`, `http://[::1]`) redirect URIs.
- The scope must contain `atproto`, must not contain a value two times, and each permission value must be well formed.

This ASP.NET Core minimal API example serves a native client document:

```csharp
using CarpaNet.OAuth;
using CarpaNet.OAuth.Scopes;

var scope = new ScopeSet()
    .AddAtproto()
    .AddInclude("app.bsky.authFullApp", "did:web:api.bsky.app#bsky_appview")
    .AddBlob("*/*");

var metadata = OAuthClientMetadata.CreatePublicClient(
    "https://app.example.com/client-metadata.json",
    new[] { "com.example.app:/callback", "http://127.0.0.1/callback" },
    scope);
metadata.ClientName = "Example";
metadata.ClientUri = "https://app.example.com/";
metadata.Validate(); // throws InvalidOAuthClientMetadataException

app.MapGet("/client-metadata.json", () => Results.Json(metadata, OAuthClientMetadata.JsonTypeInfo));
```

A loopback client ID (`http://localhost?...`) does not have a hosted document. The authorization server derives the document from the ID. `OAuthClientMetadata.CreateForLoopbackClientId(clientId)` returns that derived document.

## Callback Validation

`CallbackAsync` validates the authorization response before it creates the session. When a check fails, it throws `OAuthCallbackException` (with `AppState` set) and does not store a session.

- **`iss` parameter (RFC 9207).** If the callback has an `iss` parameter, it must equal the issuer that the flow started with (`issuer_mismatch`). If the server metadata has `authorization_response_iss_parameter_supported: true`, the parameter is required (`missing_iss`). These checks occur before the code is exchanged.
- **Token subject.** The `sub` of the token response must be an atproto DID (`invalid_sub`). If you started the flow with a handle or DID, `sub` must be that account's DID (`sub_mismatch`). The library resolves the DID document of `sub` and reads the protected resource metadata of its PDS. The `authorization_servers` list must contain the issuer that issued the tokens (`sub_issuer_mismatch`). When one of these checks fails, the library revokes the tokens.
- **PDS URL.** The session uses the PDS from the DID document of `sub`. This is also true when you start the flow with a PDS or entryway URL (for example `https://bsky.social`).

If you implement a persistent `IOAuthStateStore`, also store `OAuthStateData.ExpectedSub`. If it is not stored, the `sub_mismatch` check does not occur.

## Token Refresh

Before each refresh, the session resolves the account's DID document again (bypassing the cache) and
checks that its PDS still names the same authorization server. If the account has moved to a PDS
behind another authorization server, the refresh is not attempted and `SessionInvalidated` is raised
with the reason `issuer_mismatch`; a failed lookup only fails that refresh. The refreshed tokens use
the PDS from the DID document as their audience.

DPoP proofs are single use. When the session's `HttpClient` includes `RateLimitHandler`, the OAuth
client registers a callback (`RateLimitHandler.SetRetryPreparer`) so each 429 retry is signed with a
new proof. A DPoP-signed request without such a callback is not retried by the handler.

## Restoring an OAuth Session

```csharp
var config = new OAuthClientConfig
{
    ClientId = savedClientId,
    RedirectUri = savedRedirectUri,
    Scope = "atproto",
    JsonOptions = ATProtoJsonContext.DefaultOptions,
    SessionStore = mySessionStore,
};

using var oauthSession = new OAuthSession(config);
ATProtoOAuthClient atClient = await oauthSession.RestoreSessionAsync(userDid);
```

## Custom Session Storage

Implement `IOAuthSessionStore` to persist OAuth sessions (DPoP keys, tokens) across app restarts:

```csharp
public sealed class FileOAuthSessionStore : IOAuthSessionStore
{
    private readonly string _directory;

    public FileOAuthSessionStore(string directory) => _directory = directory;

    public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data);
        File.WriteAllText(GetPath(sub), json);
        return Task.CompletedTask;
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken ct)
    {
        var path = GetPath(sub);
        if (!File.Exists(path)) return Task.FromResult<OAuthSessionData?>(null);
        var data = JsonSerializer.Deserialize<OAuthSessionData>(File.ReadAllText(path));
        return Task.FromResult(data);
    }

    public Task DeleteAsync(string sub, CancellationToken ct)
    {
        File.Delete(GetPath(sub));
        return Task.CompletedTask;
    }

    private string GetPath(string sub) =>
        Path.Combine(_directory, $"oauth-{sub.Replace(":", "_")}.json");
}
```
