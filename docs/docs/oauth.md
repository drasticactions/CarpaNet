# OAuth Authentication

OAuth is the recommended auth method for user-facing apps. Requires the `CarpaNet.OAuth` package.

## Desktop/Console App Flow

```csharp
using CarpaNet.OAuth;

// 1. Configure with loopback URI for desktop apps
var port = 8080;
var config = new OAuthClientConfig
{
    ClientId = OAuthClientConfig.CreateLoopbackClientId(port),
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
