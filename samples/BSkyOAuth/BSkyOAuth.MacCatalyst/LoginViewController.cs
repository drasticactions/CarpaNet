// <copyright file="LoginViewController.cs" company="Drastic Actions">
// Copyright (c) Drastic Actions. All rights reserved.
// </copyright>

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
    }

    private async void LoadSessionButton_TouchUpInside(object? sender, EventArgs e)
    {
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
    public Task DeleteAsync(string sub, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<OAuthSessionData?> GetAsync(string sub, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task StoreAsync(string sub, OAuthSessionData data, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}