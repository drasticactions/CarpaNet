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
