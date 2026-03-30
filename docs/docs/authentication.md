# Authentication

CarpaNet supports two authentication methods: app passwords (for scripts and bots) and [OAuth 2.0](oauth.md) (for user-facing apps).

## App Password

The simplest way to authenticate. Create an app password in your Bluesky account settings, then:

```csharp
var client = await ATProtoClient.CreateWithSessionAsync(
    identifier: "alice.bsky.social",
    password: "xxxx-xxxx-xxxx-xxxx",
    options: new ATProtoClientOptions
    {
        JsonOptions = ATProtoJsonContext.DefaultOptions,
        CborContext = ATProtoCborContext.Default,
    });
```

## Session Persistence

Implement `ISessionStore` to persist sessions across app restarts:

```csharp
var client = ATProtoClientFactory.Create(new ATProtoClientOptions
{
    SessionStore = new MySessionStore(),
});
bool restored = await client.RestoreSessionAsync(userDid);
```

## Token Refresh

Token refresh is handled automatically when `AutoRetryOnAuthFailure` is enabled (the default). Listen for refresh events to persist updated tokens:

```csharp
if (client.TokenProvider is { } provider)
{
    provider.TokenRefreshed += (sender, args) =>
    {
        SaveTokens(args.Did, args.AccessToken, args.RefreshToken);
    };
}
```
