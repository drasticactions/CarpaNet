// <copyright file="LoginViewController.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using AppBsky.Feed;
using BSkyOAuth;
using CarpaNet;
using CarpaNet.OAuth;
using CarpaNet.OAuth.Storage;

/// <summary>
/// Login View Controller.
/// </summary>
public sealed class LoginViewController : UIViewController
{
    private const string ClientMetadataUrl = "https://drasticactions.vip/client-metadata.json";

    private const string RedirectUri = "vip.drasticactions:/callback";

    private readonly OAuthManager oauthManager;
    private readonly ATProtoOAuthClient oauthClient;

    private OAuthSession client;

    private UIButton authButton;

    private UIButton getTimelineButton;

    private UIButton loadSessionButton;

    private UITextField handleField;

    private OauthStore sessionStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoginViewController"/> class.
    /// </summary>
    public LoginViewController()
    {
        this.oauthManager = new OAuthManager(this, "vip.drasticactions", this.OnSuccess, this.OnError);

        this.sessionStore = new OauthStore();

        var config = new OAuthClientConfig
        {
            ClientId = ClientMetadataUrl,
            RedirectUri = RedirectUri,
            Scope = "atproto transition:generic",
            JsonOptions = ATProtoClientFactory.CreateJsonOptions(),
            SessionStore = this.sessionStore
        };

        this.oauthClient = new ATProtoOAuthClient(config);

        this.View!.BackgroundColor = UIColor.SystemBackground;

        this.handleField = new UITextField();
        this.handleField.Placeholder = "Handle";
        this.handleField.TranslatesAutoresizingMaskIntoConstraints = false;
        this.View!.AddSubview(this.handleField);

        this.authButton = new UIButton(UIButtonType.System);
        this.authButton.SetTitle("Authenticate", UIControlState.Normal);
        this.authButton.TranslatesAutoresizingMaskIntoConstraints = false;
        this.authButton.TouchUpInside += this.AuthButton_TouchUpInside;
        this.View!.AddSubview(this.authButton);

        this.getTimelineButton = new UIButton(UIButtonType.System);
        this.getTimelineButton.SetTitle("Get Timeline", UIControlState.Normal);
        this.getTimelineButton.TranslatesAutoresizingMaskIntoConstraints = false;
        this.getTimelineButton.Enabled = false;
        this.getTimelineButton.TouchUpInside += this.GetTimelineButton_TouchUpInside;
        this.View!.AddSubview(this.getTimelineButton);

        this.loadSessionButton = new UIButton(UIButtonType.System);
        this.loadSessionButton.SetTitle("Load Session", UIControlState.Normal);
        this.loadSessionButton.TranslatesAutoresizingMaskIntoConstraints = false;
        this.loadSessionButton.TouchUpInside += this.LoadSessionButton_TouchUpInside;
        this.View!.AddSubview(this.loadSessionButton);

        this.View!.AddConstraints(new[]
        {
            NSLayoutConstraint.Create(this.handleField, NSLayoutAttribute.Top, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Top, 1, 100),
            NSLayoutConstraint.Create(this.handleField, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Leading, 1, 20),
            NSLayoutConstraint.Create(this.handleField, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Trailing, 1, -20),
            NSLayoutConstraint.Create(this.handleField, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 40),
            NSLayoutConstraint.Create(this.authButton, NSLayoutAttribute.Top, NSLayoutRelation.Equal, this.handleField, NSLayoutAttribute.Bottom, 1, 20),
            NSLayoutConstraint.Create(this.authButton, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Leading, 1, 20),
            NSLayoutConstraint.Create(this.authButton, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Trailing, 1, -20),
            NSLayoutConstraint.Create(this.authButton, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 40),
            NSLayoutConstraint.Create(this.getTimelineButton, NSLayoutAttribute.Top, NSLayoutRelation.Equal, this.authButton, NSLayoutAttribute.Bottom, 1, 20),
            NSLayoutConstraint.Create(this.getTimelineButton, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Leading, 1, 20),
            NSLayoutConstraint.Create(this.getTimelineButton, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Trailing, 1, -20),
            NSLayoutConstraint.Create(this.getTimelineButton, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 40),
            NSLayoutConstraint.Create(this.loadSessionButton, NSLayoutAttribute.Top, NSLayoutRelation.Equal, this.getTimelineButton, NSLayoutAttribute.Bottom, 1, 20),
            NSLayoutConstraint.Create(this.loadSessionButton, NSLayoutAttribute.Leading, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Leading, 1, 20),
            NSLayoutConstraint.Create(this.loadSessionButton, NSLayoutAttribute.Trailing, NSLayoutRelation.Equal, this.View!, NSLayoutAttribute.Trailing, 1, -20),
            NSLayoutConstraint.Create(this.loadSessionButton, NSLayoutAttribute.Height, NSLayoutRelation.Equal, 1, 40),
        });
    }

    private async void AuthButton_TouchUpInside(object? sender, EventArgs e)
    {
        var atIdentifier = new ATIdentifier(this.handleField.Text ?? string.Empty);
        if (!atIdentifier.IsValid)
        {
            this.InvokeOnMainThread(() =>
            {
                var alert = UIAlertController.Create("Error", "Invalid handle", UIAlertControllerStyle.Alert);
                alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                this.PresentViewController(alert, true, null);
            });

            return;
        }

        var url = await this.oauthClient.AuthorizeAsync(atIdentifier);

        this.oauthManager.StartAuthentication(url);
    }

    private async void OnSuccess(NSUrl? callbackUrl)
    {
        // OnSuccess means we got a successful response from the session, but
        // there may be an error in the response. We need to check for that.
        if (callbackUrl != null)
        {
            var parameters = callbackUrl.Query?.TrimStart('?')
                .Split('&')
                .Select(param => param.Split('='))
                .ToDictionary(split => split[0], split => Uri.UnescapeDataString(split[1])) ?? new Dictionary<string, string>();

            if (parameters.TryGetValue("code", out string? code))
            {
                // If we got a code, we can complete the authentication process.
                this.client = await this.oauthClient.CallbackAsync(callbackUrl.ToString());
                if (this.client != null)
                {
                    this.InvokeOnMainThread(() =>
                    {
                        this.getTimelineButton.Enabled = true;
                        var alert = UIAlertController.Create("Success", $"Authenticated as {this.client.Did}", UIAlertControllerStyle.Alert);
                        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                        this.PresentViewController(alert, true, null);
                    });
                }
                else
                {
                    this.InvokeOnMainThread(() =>
                    {
                        var alert = UIAlertController.Create("Error", "Failed to authenticate", UIAlertControllerStyle.Alert);
                        alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                        this.PresentViewController(alert, true, null);
                    });
                }
            }
            else if (parameters.TryGetValue("error", out string? error))
            {
                this.InvokeOnMainThread(() =>
                {
                    var alert = UIAlertController.Create("Error", error, UIAlertControllerStyle.Alert);
                    alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                    this.PresentViewController(alert, true, null);
                });
            }
        }
    }

    private async void GetTimelineButton_TouchUpInside(object? sender, EventArgs e)
    {
        try
        {
            var parameters = new GetTimelineParameters { Limit = 10 };
            var timeline = await this.client.AppBskyFeedGetTimelineAsync(parameters);

            var message = $"Feed items: {timeline.Feed?.Count ?? 0}\n";
            if (timeline.Feed != null)
            {
                foreach (var item in timeline.Feed)
                {
                    var author = item.Post?.Author;
                    message += $"\n@{author?.Handle} ({author?.DisplayName})";
                }
            }

            this.InvokeOnMainThread(() =>
            {
                var alert = UIAlertController.Create("Timeline", message, UIAlertControllerStyle.Alert);
                alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                this.PresentViewController(alert, true, null);
            });
        }
        catch (Exception ex)
        {
            this.InvokeOnMainThread(() =>
            {
                var alert = UIAlertController.Create("Error", $"Failed to get timeline: {ex.Message}", UIAlertControllerStyle.Alert);
                alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                this.PresentViewController(alert, true, null);
            });
        }
    }

    private async void LoadSessionButton_TouchUpInside(object? sender, EventArgs e)
    {
        var alert = UIAlertController.Create("Restore Session", "Enter your DID", UIAlertControllerStyle.Alert);
        alert.AddTextField(field => field.Placeholder = "did:plc:...");

        var tcs = new TaskCompletionSource<string?>();
        alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, _ => tcs.SetResult(null)));
        alert.AddAction(UIAlertAction.Create("Restore", UIAlertActionStyle.Default, _ => tcs.SetResult(alert.TextFields?[0].Text)));
        this.PresentViewController(alert, true, null);

        var did = await tcs.Task;
        if (string.IsNullOrWhiteSpace(did))
            return;

        try
        {
            var session = await this.oauthClient.RestoreSessionAsync(did);
            if (session != null)
            {
                this.client = session;
                this.InvokeOnMainThread(() =>
                {
                    this.getTimelineButton.Enabled = true;
                    var successAlert = UIAlertController.Create("Success", $"Session restored for {session.Did}", UIAlertControllerStyle.Alert);
                    successAlert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                    this.PresentViewController(successAlert, true, null);
                });
            }
            else
            {
                this.InvokeOnMainThread(() =>
                {
                    var errorAlert = UIAlertController.Create("Error", "No saved session found for that DID.", UIAlertControllerStyle.Alert);
                    errorAlert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                    this.PresentViewController(errorAlert, true, null);
                });
            }
        }
        catch (Exception ex)
        {
            this.InvokeOnMainThread(() =>
            {
                var errorAlert = UIAlertController.Create("Error", $"Failed to restore session: {ex.Message}", UIAlertControllerStyle.Alert);
                errorAlert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
                this.PresentViewController(errorAlert, true, null);
            });
        }
    }

    private void OnError(NSError? error)
    {
        this.InvokeOnMainThread(() =>
        {
            var alert = UIAlertController.Create("Error", error!.LocalizedDescription, UIAlertControllerStyle.Alert);
            alert.AddAction(UIAlertAction.Create("OK", UIAlertActionStyle.Default, null));
            this.PresentViewController(alert, true, null);
        });
    }
}

public sealed class OauthStore : IOAuthSessionStore
{
    private readonly string _directory;

    public OauthStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BSkyOAuth");
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
