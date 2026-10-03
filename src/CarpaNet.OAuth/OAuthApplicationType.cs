namespace CarpaNet.OAuth;

/// <summary>
/// The <c>application_type</c> values of OAuth client metadata.
/// </summary>
public static class OAuthApplicationType
{
    /// <summary>
    /// A native app (desktop, mobile). It may use loopback and private-use URI scheme redirect URIs.
    /// </summary>
    public const string Native = "native";

    /// <summary>
    /// A web app. It may use only https redirect URIs.
    /// </summary>
    public const string Web = "web";
}
