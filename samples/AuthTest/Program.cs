using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppBsky.Actor;
using AppBsky.Feed;
using AppBsky.Notification;
using ComAtproto.Repo;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using CarpaNet.Storage;
using CarpaNet;

const string SessionDir = ".atproto-sessions";

Console.WriteLine("=== CarpaNet Auth Test ===");

while (true)
{
    Console.WriteLine();
    Console.WriteLine("Choose auth method:");
    Console.WriteLine("  1. Password (App Password)");
    Console.WriteLine("  2. OAuth (Localhost)");
    Console.WriteLine("  3. Restore Password Session");
    Console.WriteLine("  4. Restore OAuth Session");
    Console.WriteLine("  5. Exit");
    Console.Write("> ");

    var choice = Console.ReadLine()?.Trim();

    switch (choice)
    {
        case "1":
            await PasswordFlowAsync();
            break;
        case "2":
            await OAuthFlowAsync();
            break;
        case "3":
            await RestorePasswordSessionAsync();
            break;
        case "4":
            await RestoreOAuthSessionAsync();
            break;
        case "5":
            return;
        default:
            Console.WriteLine("Invalid choice.");
            break;
    }
}

async Task PasswordFlowAsync()
{
    Console.Write("Handle or DID: ");
    var identifier = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(identifier))
    {
        Console.WriteLine("Identifier is required.");
        return;
    }

    Console.Write("App Password: ");
    var password = ReadPassword();
    Console.WriteLine();

    if (string.IsNullOrEmpty(password))
    {
        Console.WriteLine("Password is required.");
        return;
    }

    try
    {
        var sessionStore = new FileSessionStore(GetSessionDir());
        var client = ATProtoClientFactory.CreateSessionClient(sessionStore);
        var session = await client.LoginAsync(identifier, password);
        Console.WriteLine($"Logged in as {session.Handle} ({session.Did})");
        Console.WriteLine("Session saved automatically.");

        await AuthenticatedMenuAsync(client, session.Handle, session.Did);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Login failed: {ex.Message}");
    }
}

async Task OAuthFlowAsync()
{
    Console.Write("Handle: ");
    var handle = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(handle))
    {
        Console.WriteLine("Handle is required.");
        return;
    }

    // Find a free port
    var listener = new HttpListener();
    var port = FindFreePort();
    var redirectUri = $"http://127.0.0.1:{port}/callback";
    var clientId = $"http://localhost?redirect_uri={Uri.EscapeDataString(redirectUri)}&scope={Uri.EscapeDataString("atproto transition:generic")}";

    listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    listener.Start();

    try
    {
        var sessionStore = new FileOAuthSessionStore(GetSessionDir());

        var config = new OAuthClientConfig
        {
            ClientId = clientId,
            RedirectUri = redirectUri,
            Scope = "atproto transition:generic",
            JsonOptions = ATProtoClientFactory.CreateJsonOptions(),
            SessionStore = sessionStore
        };

        using var oauthClient = new OAuthSession(config);

        Console.WriteLine("Starting OAuth flow...");
        var authUrl = await oauthClient.AuthorizeAsync(handle);

        Console.WriteLine($"Opening browser for authorization...");
        Console.WriteLine($"If the browser doesn't open, visit: {authUrl}");

        // Open browser
        try
        {
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch
        {
            Console.WriteLine("Could not open browser automatically.");
        }

        Console.WriteLine("Waiting for callback...");

        // Wait for callback
        var context = await listener.GetContextAsync();
        var callbackUrl = context.Request.Url!.ToString();

        // Send response to browser
        var responseBytes = Encoding.UTF8.GetBytes(
            "<html><body><h1>Authorization Complete</h1><p>You can close this tab.</p></body></html>");
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = responseBytes.Length;
        await context.Response.OutputStream.WriteAsync(responseBytes);
        context.Response.Close();

        Console.WriteLine("Processing callback...");

        var session = await oauthClient.CallbackAsync(callbackUrl);
        Console.WriteLine($"Authenticated as {session.Did}");
        Console.WriteLine("OAuth session saved automatically.");

        await AuthenticatedMenuAsync(session, null, session.Did);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"OAuth flow failed: {ex.Message}");
    }
    finally
    {
        listener.Stop();
    }
}

async Task RestorePasswordSessionAsync()
{
    Console.Write("DID to restore: ");
    var did = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(did))
    {
        Console.WriteLine("DID is required.");
        return;
    }

    try
    {
        Console.WriteLine($"Restoring session for {did}...");
        var sessionStore = new FileSessionStore(GetSessionDir());
        var client = ATProtoClientFactory.CreateSessionClient(sessionStore);
        var restored = await client.RestoreSessionAsync(did);

        if (!restored)
        {
            Console.WriteLine("No saved password session found for that DID.");
            return;
        }

        // Refresh to verify the session is still valid
        await client.TokenProvider.RefreshAsync();
        Console.WriteLine($"Session restored for {client.Handle} ({client.AuthenticatedDid})");

        await AuthenticatedMenuAsync(client, client.Handle, client.AuthenticatedDid!);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to restore session: {ex.Message}");
    }
}

async Task RestoreOAuthSessionAsync()
{
    Console.Write("DID to restore: ");
    var did = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(did))
    {
        Console.WriteLine("DID is required.");
        return;
    }

    try
    {
        Console.WriteLine($"Restoring OAuth session for {did}...");

        var sessionStore = new FileOAuthSessionStore(GetSessionDir());

        // Load stored session data to get the client config
        var sessionData = await sessionStore.GetAsync(did);
        if (sessionData == null || string.IsNullOrEmpty(sessionData.ClientId))
        {
            Console.WriteLine("No saved OAuth session found for that DID.");
            return;
        }

        var config = new OAuthClientConfig
        {
            ClientId = sessionData.ClientId,
            RedirectUri = sessionData.RedirectUri ?? string.Empty,
            Scope = sessionData.Scope ?? "atproto",
            JsonOptions = ATProtoClientFactory.CreateJsonOptions(),
            SessionStore = sessionStore
        };

        using var oauthClient = new OAuthSession(config);
        var session = await oauthClient.RestoreSessionAsync(did);

        if (session == null)
        {
            Console.WriteLine("Failed to restore OAuth session.");
            return;
        }

        Console.WriteLine($"OAuth session restored for {session.Did}");
        await AuthenticatedMenuAsync(session, null, session.Did);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to restore OAuth session: {ex.Message}");
    }
}

async Task AuthenticatedMenuAsync(IATProtoClient client, string? handle, string did)
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Authenticated as {handle ?? did} ===");
        Console.WriteLine("  1. Get my profile");
        Console.WriteLine("  2. Get my preferences");
        Console.WriteLine("  3. Get my timeline");
        Console.WriteLine("  4. Get notifications");
        Console.WriteLine("  5. Create a post");
        Console.WriteLine("  6. Delete a post");
        Console.WriteLine("  7. Save session");
        Console.WriteLine("  8. Show session tokens");
        Console.WriteLine("  9. Exit");
        Console.Write("> ");

        var choice = Console.ReadLine()?.Trim();

        try
        {
            switch (choice)
            {
                case "1":
                    await GetProfileAsync(client, handle ?? did);
                    break;
                case "2":
                    await GetPreferencesAsync(client);
                    break;
                case "3":
                    await GetTimelineAsync(client);
                    break;
                case "4":
                    await GetNotificationsAsync(client);
                    break;
                case "5":
                    await CreatePostAsync(client, did);
                    break;
                case "6":
                    await DeletePostAsync(client, did);
                    break;
                case "7":
                    SaveCurrentSession(client);
                    break;
                case "8":
                    await ShowSessionTokensAsync(client);
                    break;
                case "9":
                    return;
                default:
                    Console.WriteLine("Invalid choice.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}

async Task GetProfileAsync(IATProtoClient client, string actor)
{
    var parameters = new GetProfileParameters { Actor = new ATHandle(actor) };
    var profile = await client.AppBskyActorGetProfileAsync(parameters);

    Console.WriteLine($"  Display Name: {profile.DisplayName}");
    Console.WriteLine($"  Handle: {profile.Handle}");
    Console.WriteLine($"  DID: {profile.Did}");
    Console.WriteLine($"  Followers: {profile.FollowersCount}");
    Console.WriteLine($"  Following: {profile.FollowsCount}");
    Console.WriteLine($"  Posts: {profile.PostsCount}");

    if (!string.IsNullOrEmpty(profile.Description))
    {
        Console.WriteLine($"  Bio: {profile.Description}");
    }
}

async Task GetPreferencesAsync(IATProtoClient client)
{
    var prefs = await client.AppBskyActorGetPreferencesAsync();
    Console.WriteLine($"  Preferences count: {prefs.Preferences?.Count ?? 0}");
}

async Task GetTimelineAsync(IATProtoClient client)
{
    var parameters = new GetTimelineParameters { Limit = 5 };
    var timeline = await client.AppBskyFeedGetTimelineAsync(parameters);

    Console.WriteLine($"  Feed items: {timeline.Feed?.Count ?? 0}");
    if (timeline.Feed != null)
    {
        foreach (var item in timeline.Feed)
        {
            var author = item.Post?.Author;
            Console.WriteLine($"  - @{author?.Handle}: (post by {author?.DisplayName})");
        }
    }
}

async Task GetNotificationsAsync(IATProtoClient client)
{
    var parameters = new ListNotificationsParameters { Limit = 10 };
    var notifications = await client.AppBskyNotificationListNotificationsAsync(parameters);

    Console.WriteLine($"  Notifications: {notifications.Notifications?.Count ?? 0}");
    if (notifications.Notifications != null)
    {
        foreach (var notif in notifications.Notifications)
        {
            Console.WriteLine($"  - [{notif.Reason}] from @{notif.Author?.Handle}");
        }
    }
}

async Task CreatePostAsync(IATProtoClient client, string did)
{
    Console.Write("Post text: ");
    var text = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine("Post text cannot be empty.");
        return;
    }

    var post = new AppBsky.Feed.Post
    {
        Text = text,
        CreatedAt = DateTimeOffset.UtcNow
    };

    var input = new CreateRecordInput
    {
        Repo = new ATIdentifier(did),
        Collection = AppBsky.Feed.Post.RecordType,
        Record = post.ToJson()
    };

    var result = await client.ComAtprotoRepoCreateRecordAsync(input);
    Console.WriteLine($"  Post created!");
    Console.WriteLine($"  URI: {result.Uri}");
    Console.WriteLine($"  CID: {result.Cid}");
}

async Task DeletePostAsync(IATProtoClient client, string did)
{
    Console.Write("AT URI of post to delete: ");
    var uriString = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(uriString))
    {
        Console.WriteLine("URI cannot be empty.");
        return;
    }

    var atUri = new ATUri(uriString);
    if (!atUri.IsValid || string.IsNullOrEmpty(atUri.RecordKey))
    {
        Console.WriteLine("Invalid AT URI.");
        return;
    }

    var input = new DeleteRecordInput
    {
        Repo = new ATIdentifier(did),
        Collection = AppBsky.Feed.Post.RecordType,
        Rkey = atUri.RecordKey
    };

    await client.ComAtprotoRepoDeleteRecordAsync(input);
    Console.WriteLine("  Post deleted.");
}

async Task ShowSessionTokensAsync(IATProtoClient client)
{
    if (client is ATProtoSessionClient sessionClient)
    {
        var tp = sessionClient.TokenProvider;
        Console.WriteLine($"  DID: {tp.CurrentDid}");
        Console.WriteLine($"  Handle: {tp.Handle}");
        Console.WriteLine($"  PDS URL: {tp.PdsUrl}");
        Console.WriteLine($"  Has Valid Token: {tp.HasValidToken}");
        Console.WriteLine($"  Access JWT: {tp.AccessJwt}");
        Console.WriteLine($"  Refresh JWT: {tp.RefreshJwt}");
        if (tp.AccessExpiry != default)
        {
            var remaining = tp.AccessExpiry - DateTimeOffset.UtcNow;
            Console.WriteLine($"  Expires At: {tp.AccessExpiry:u}");
            Console.WriteLine($"  Expires In: {(remaining > TimeSpan.Zero ? remaining.ToString(@"hh\:mm\:ss") : "expired")}");
        }
    }
    else if (client is CarpaNet.OAuth.ATProtoOAuthClient oauthSession)
    {
        var tp = (CarpaNet.OAuth.DPoPTokenProvider)oauthSession.TokenProvider;
        Console.WriteLine($"  DID: {tp.CurrentDid}");
        Console.WriteLine($"  PDS URL: {tp.PdsUrl}");
        Console.WriteLine($"  Has Valid Token: {tp.HasValidToken}");
        Console.WriteLine($"  Access Token: {tp.AccessToken}");
        Console.WriteLine($"  Refresh Token: {tp.RefreshToken}");
        if (tp.ExpiresAt.HasValue)
        {
            var remaining = tp.ExpiresAt.Value - DateTimeOffset.UtcNow;
            Console.WriteLine($"  Expires At: {tp.ExpiresAt.Value:u}");
            Console.WriteLine($"  Expires In: {(remaining > TimeSpan.Zero ? remaining.ToString(@"hh\:mm\:ss") : "expired")}");
        }
        else
        {
            Console.WriteLine("  Expires At: unknown");
        }
    }
    else
    {
        Console.WriteLine("  Unknown client type.");
    }
}

void SaveCurrentSession(IATProtoClient client)
{
    if (client is ATProtoSessionClient)
    {
        Console.WriteLine("Password sessions are saved automatically.");
    }
    else
    {
        Console.WriteLine("OAuth sessions are saved automatically.");
    }
}

// --- Session persistence helpers ---

static string GetSessionDir()
{
    var dir = Path.Combine(AppContext.BaseDirectory, SessionDir);
    Directory.CreateDirectory(dir);
    return dir;
}

static int FindFreePort()
{
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static string ReadPassword()
{
    var sb = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (sb.Length > 0)
            {
                sb.Remove(sb.Length - 1, 1);
                Console.Write("\b \b");
            }
        }
        else
        {
            sb.Append(key.KeyChar);
            Console.Write('*');
        }
    }
    return sb.ToString();
}

// --- File-backed session stores ---

public sealed class FileSessionStore : ISessionStore
{
    private readonly string _directory;

    public FileSessionStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public Task StoreAsync(string sub, SessionData data, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(data, AuthTestJsonContext.Default.SessionData);
        File.WriteAllText(GetPath(sub), json);
        return Task.CompletedTask;
    }

    public Task<SessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sub);
        if (!File.Exists(path))
            return Task.FromResult<SessionData?>(null);
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize(json, AuthTestJsonContext.Default.SessionData);
        return Task.FromResult(data);
    }

    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sub);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string sub) =>
        Path.Combine(_directory, $"session-{sub.Replace(":", "_")}.json");
}

public sealed class FileOAuthSessionStore : IOAuthSessionStore
{
    private readonly string _directory;

    public FileOAuthSessionStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(data, AuthTestJsonContext.Default.OAuthSessionData);
        File.WriteAllText(GetPath(sub), json);
        return Task.CompletedTask;
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sub);
        if (!File.Exists(path))
            return Task.FromResult<OAuthSessionData?>(null);
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize(json, AuthTestJsonContext.Default.OAuthSessionData);
        return Task.FromResult(data);
    }

    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sub);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetPath(string sub) =>
        Path.Combine(_directory, $"oauth-{sub.Replace(":", "_")}.json");
}

// --- AOT-safe JSON context ---

[JsonSerializable(typeof(SessionData))]
[JsonSerializable(typeof(OAuthSessionData))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class AuthTestJsonContext : JsonSerializerContext
{
}
