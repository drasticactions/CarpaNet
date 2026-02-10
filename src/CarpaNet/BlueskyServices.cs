namespace CarpaNet;

/// <summary>
/// Well-known Bluesky service endpoints and DIDs.
/// </summary>
public static class BlueskyServices
{
    /// <summary>
    /// Public Bluesky AppView API (for unauthenticated requests).
    /// Use this for public app.bsky.* endpoints without authentication.
    /// </summary>
    public const string PublicAppView = "https://public.api.bsky.app";

    /// <summary>
    /// Bluesky AppView API.
    /// </summary>
    public const string AppView = "https://api.bsky.app";

    /// <summary>
    /// Bluesky AppView service DID for proxy header.
    /// </summary>
    public const string AppViewServiceDid = "did:web:api.bsky.app#bsky_appview";

    /// <summary>
    /// Bluesky Entryway (for authentication and account management).
    /// </summary>
    public const string Entryway = "https://bsky.social";

    /// <summary>
    /// Bluesky Relay (for firehose subscription).
    /// </summary>
    public const string Relay = "https://bsky.network";

    /// <summary>
    /// Bluesky Chat/DM service.
    /// </summary>
    public const string Chat = "https://api.bsky.chat";

    /// <summary>
    /// Bluesky Chat service DID for proxy header.
    /// </summary>
    public const string ChatServiceDid = "did:web:api.bsky.chat#bsky_chat";

    /// <summary>
    /// Bluesky Ozone moderation service.
    /// </summary>
    public const string Ozone = "https://mod.bsky.app";

    /// <summary>
    /// Bluesky Ozone service DID for proxy header.
    /// </summary>
    public const string OzoneServiceDid = "did:plc:ar7c4by46qjdydhdevvrndac#atproto_labeler";

    /// <summary>
    /// Bluesky Jetstream instance 1, US-East.
    /// </summary>
    public const string Jetstream1UsEast = "https://jetstream1.us-east.bsky.network";

    /// <summary>
    /// Bluesky Jetstream instance 2, US-East.
    /// </summary>
    public const string Jetstream2UsEast = "https://jetstream2.us-east.bsky.network";

    /// <summary>
    /// Bluesky Jetstream instance 1, US-West.
    /// </summary>
    public const string Jetstream1UsWest = "https://jetstream1.us-west.bsky.network";

    /// <summary>
    /// Bluesky Jetstream instance 2, US-West.
    /// </summary>
    public const string Jetstream2UsWest = "https://jetstream2.us-west.bsky.network";
}
