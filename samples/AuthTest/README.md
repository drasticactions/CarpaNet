# AuthTest

An console application demonstrating CarpaNet's authentication flows and generated API bindings against live ATProtocol services.

## Prerequisites

- .NET 10 SDK
- ATProtocol Lexicon JSON files — set the `ATPROTO_LEXICON` environment variable to the directory containing them (e.g., a clone of [bluesky-social/atproto](https://github.com/bluesky-social/atproto/tree/main/lexicons))
- An ATProtocol account (e.g., Bluesky) for testing authenticated flows

## Running

```bash
export ATPROTO_LEXICON=/path/to/lexicons
dotnet run --project samples/AuthTest
```

It should also work as a NativeAOT binary:

```bash
dotnet publish samples/AuthTest -c Release
```

## How to use

I wrote this as a way to test auth and perform basic Bluesky functions, to verify they work. When starting the app, you'll get a menu of auth choices:

1. **Password (App Password)** — Logs in via `ATProtoClient.LoginAsync` using an app password. Demonstrates session creation and optional persistence to disk.
2. **OAuth (Localhost)** — Runs a full OAuth 2.0 flow using `OAuthSession`. Spins up a local HTTP listener, opens the browser for authorization, and exchanges the callback code for tokens. Sessions are automatically persisted via a file-backed `IOAuthSessionStore`.
3. **Restore Password Session** — Restores a previously saved password session from disk and refreshes the tokens.
4. **Restore OAuth Session** — Restores a previously saved OAuth session using `OAuthSession.RestoreSessionAsync`.

Once authenticated, a second menu demonstrates the source gen API calls:

- **Get profile** — `client.AppBskyActorGetProfileAsync()`
- **Get preferences** — `client.AppBskyActorGetPreferencesAsync()`
- **Get timeline** — `client.AppBskyFeedGetTimelineAsync()`
- **Get notifications** — `client.AppBskyNotificationListNotificationsAsync()`
- **Create a post** — `client.ComAtprotoRepoCreateRecordAsync()` with a `Post` record
- **Delete a post** — `client.ComAtprotoRepoDeleteRecordAsync()` by AT URI
- **Show session tokens** — Displays current token details and expiry

## Project Setup

The `.csproj` shows how a consumer project wires up CarpaNet with the source generator:

```xml
<!-- Reference the runtime library -->
<ProjectReference Include="..\..\src\CarpaNet\CarpaNet.csproj" />

<!-- Reference the source generator as an analyzer -->
<ProjectReference Include="..\..\src\CarpaNet.SourceGen\CarpaNet.SourceGen.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />

<!-- Import MSBuild targets that feed lexicons to the generator -->
<Import Project="..\..\src\CarpaNet.SourceGen\build\CarpaNet.SourceGen.targets" />

<!-- Point to lexicon JSON files -->
<LexiconFiles Include="$(ATPROTO_LEXICON)\**\*.json" />
```

The generated `ATProtoClientFactory` (from the source generator) creates clients with the correct JSON and CBOR contexts pre-configured, so you can call `ATProtoClientFactory.CreateSessionClient()` to get an `ATProtoClient` ready for `LoginAsync()` without manual setup.

## Session Storage

Sessions are saved to a `.atproto-sessions` directory alongside the binary. The sample implements `FileOAuthSessionStore` (an `IOAuthSessionStore` backed by JSON files) as a reference for how to persist OAuth sessions across app restarts.
