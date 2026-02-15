# CarpaNet.OAuth

[![NuGet Version](https://img.shields.io/nuget/v/CarpaNet.OAuth.svg)](https://www.nuget.org/packages/CarpaNet.OAuth/) ![License](https://img.shields.io/badge/License-MIT-blue.svg)

![CarpaNet Logo](https://user-images.githubusercontent.com/898335/253740405-4b0ae177-cc49-4c26-b6b0-ab8e835a0e62.png)

CarpaNet.OAuth is an OAuth library for CarpaNet.

![1444070256569233](https://user-images.githubusercontent.com/898335/167266846-1ad2648f-91c1-4a04-a18d-6dd4d6c7d21c.gif)

This library is experimental and not stable. Expect issues and bugs!

### OAuthSession

This is an OAuth 2.0 flow orchestrator supporting PAR (Pushed Authorization Requests), PKCE, and DPoP. Use this to produce an `ATProtoOAuthClient` via `CallbackAsync` or `RestoreSessionAsync`.

```csharp
var config = new OAuthClientConfig
{
    ClientId = clientId,
    RedirectUri = redirectUri,
    Scope = "atproto transition:generic",
    JsonOptions = myJsonOptions,
    SessionStore = mySessionStore
};

using var oauthClient = new OAuthSession(config);
var authUrl = await oauthClient.AuthorizeAsync(handle);
// ... redirect user to authUrl, receive callback ...
var session = await oauthClient.CallbackAsync(callbackUrl);
// session implements IATProtoClient
```