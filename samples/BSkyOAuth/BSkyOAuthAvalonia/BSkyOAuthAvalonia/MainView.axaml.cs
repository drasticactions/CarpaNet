using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AppBsky.Feed;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CarpaNet;
using CarpaNet.Identity;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;
using Microsoft.Extensions.Logging;

namespace BSkyOAuthAvalonia;

public partial class MainView : UserControl
{
    private const string ClientMetadataUrl = "https://drasticactions.vip/client-metadata.json";
    private const string RedirectUri = "vip.drasticactions:/callback";

    private readonly OAuthSession oauthSession;
    private readonly OauthStore sessionStore;

    private ATProtoOAuthClient? client;

    public MainView()
    {
        InitializeComponent();

        this.sessionStore = new OauthStore();

        var config = new OAuthClientConfig
        {
            ClientId = ClientMetadataUrl,
            RedirectUri = RedirectUri,
            Scope = "atproto transition:generic",
            JsonOptions = ATProtoClientFactory.CreateJsonOptions(),
            SessionStore = this.sessionStore,
            LoggerFactory = new WasmLoggerFactory(),
        };

        this.oauthSession = new OAuthSession(config);
    }

    private async void OnLoginClicked(object? sender, RoutedEventArgs e)
    {
        var handle = HandleEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            StatusLabel.Text = "Please enter a handle.";
            return;
        }

        var atIdentifier = new ATIdentifier(handle);
        if (!atIdentifier.IsValid)
        {
            StatusLabel.Text = "Invalid handle.";
            return;
        }

        SetLoading(true);

        try
        {
            var authUrl = await this.oauthSession.AuthorizeAsync(atIdentifier);

            var topLevel = TopLevel.GetTopLevel(this);
            var options = new WebAuthenticatorOptions(
                new Uri(authUrl),
                new Uri(RedirectUri));

            var result = await WebAuthenticationBroker.AuthenticateAsync(topLevel!, options);

            var callbackUrl = result.CallbackUri.ToString();
            this.client = await this.oauthSession.CallbackAsync(callbackUrl);

            if (this.client != null)
            {
                GetTimelineBtn.IsEnabled = true;
                StatusLabel.Text = $"Authenticated as {this.client.Did}";
            }
            else
            {
                StatusLabel.Text = "Authentication failed.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Authentication cancelled.";
        }
        catch (Exception ex)
        {
            var errorDetail = $"Error: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}";
            StatusLabel.Text = errorDetail;
            Console.Error.WriteLine(errorDetail);
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async void OnLoadSessionClicked(object? sender, RoutedEventArgs e)
    {
        var handle = HandleEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(handle))
        {
            StatusLabel.Text = "Enter a DID or handle to restore.";
            return;
        }

        var atIdentifier = new ATIdentifier(handle);
        if (!atIdentifier.IsValid)
        {
            StatusLabel.Text = "Invalid DID or handle.";
            return;
        }

        SetLoading(true);

        try
        {
            var did = handle;
            if (atIdentifier.IsHandle)
            {
                using var resolver = new IdentityResolver();
                var doc = await resolver.ResolveAsync(did);
                did = doc.Id;
            }

            var session = await this.oauthSession.RestoreSessionAsync(did);
            if (session != null)
            {
                this.client = session;
                GetTimelineBtn.IsEnabled = true;
                StatusLabel.Text = $"Session restored for {session.Did}";
            }
            else
            {
                StatusLabel.Text = "No saved session found.";
            }
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async void OnGetTimelineClicked(object? sender, RoutedEventArgs e)
    {
        if (this.client == null)
            return;

        SetLoading(true);

        try
        {
            var parameters = new GetTimelineParameters { Limit = 25 };
            var timeline = await this.client.AppBskyFeedGetTimelineAsync(parameters);

            var items = new List<TimelineItem>();
            if (timeline.Feed != null)
            {
                foreach (var item in timeline.Feed)
                {
                    var author = item.Post?.Author;
                    var text = string.Empty;
                    if (item.Post?.Record is JsonElement recordElement)
                    {
                        if (recordElement.TryGetProperty("text", out var textElement))
                        {
                            text = textElement.GetString() ?? string.Empty;
                        }
                    }

                    items.Add(new TimelineItem
                    {
                        AuthorDisplay = $"@{author?.Handle} ({author?.DisplayName})",
                        Text = text,
                    });
                }
            }

            TimelineView.ItemsSource = items;
            StatusLabel.Text = $"Loaded {items.Count} posts";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void SetLoading(bool isLoading)
    {
        LoadingIndicator.IsVisible = isLoading;
        LoginBtn.IsEnabled = !isLoading;
        LoadSessionBtn.IsEnabled = !isLoading;
        GetTimelineBtn.IsEnabled = !isLoading && this.client != null;
    }
}

public class TimelineItem
{
    public string AuthorDisplay { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

public sealed class OauthStore : IOAuthSessionStore
{
    private readonly string _directory;

    public OauthStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BSkyOAuthAvalonia");
        Directory.CreateDirectory(_directory);
    }

    public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(data, OAuthStoreJsonContext.Default.OAuthSessionData);
        File.WriteAllText(GetPath(sub), json);
        return Task.CompletedTask;
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        var path = GetPath(sub);
        if (!File.Exists(path))
            return Task.FromResult<OAuthSessionData?>(null);
        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize(json, OAuthStoreJsonContext.Default.OAuthSessionData);
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

[JsonSerializable(typeof(OAuthSessionData))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class OAuthStoreJsonContext : JsonSerializerContext
{
}

internal sealed class WasmLoggerFactory : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new WasmLogger(categoryName);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    private sealed class WasmLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = $"[{logLevel}] {category}: {formatter(state, exception)}";
            if (exception != null) msg += $"\n{exception}";
            Console.WriteLine(msg);
        }
    }
}

