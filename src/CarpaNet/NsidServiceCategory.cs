namespace CarpaNet;

/// <summary>
/// Categories of NSID namespaces that determine routing behavior.
/// </summary>
public enum NsidServiceCategory
{
    /// <summary>
    /// Unknown or custom namespace.
    /// </summary>
    Unknown,

    /// <summary>
    /// Core ATProto endpoints (com.atproto.*).
    /// These go directly to the PDS for authenticated requests,
    /// or can be made to any PDS for public data reads.
    /// </summary>
    AtProto,

    /// <summary>
    /// Bluesky app endpoints (app.bsky.*).
    /// These are handled by the AppView, typically proxied through the PDS
    /// for authenticated requests, or accessed directly via public.api.bsky.app
    /// for public reads.
    /// </summary>
    BlueskyApp,

    /// <summary>
    /// Bluesky chat endpoints (chat.bsky.*).
    /// These require proxying through the PDS to the chat service.
    /// </summary>
    BlueskyChat,

    /// <summary>
    /// Ozone moderation endpoints (tools.ozone.*).
    /// These require proxying through the PDS to the moderation service.
    /// </summary>
    Ozone
}
