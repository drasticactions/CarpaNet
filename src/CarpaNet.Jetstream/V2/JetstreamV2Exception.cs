using System;

namespace CarpaNet.Jetstream;

/// <summary>
/// Well-known Jetstream v2 XRPC error names, matched structurally from error envelopes
/// (never by message substring).
/// </summary>
public static class JetstreamV2ErrorNames
{
    /// <summary>The requested seq cursor is below the server's retention floor (pre-upgrade HTTP 400).</summary>
    public const string CursorTooOld = "CursorTooOld";

    /// <summary>The negotiated zstd dictionary ID is unknown or retired (pre-upgrade HTTP 400).</summary>
    public const string UnknownZstdDictionary = "UnknownZstdDictionary";

    /// <summary>The request configuration was rejected (pre-upgrade HTTP 400).</summary>
    public const string InvalidRequest = "InvalidRequest";

    /// <summary>The client fell adversarially far behind the live tip (terminal stream error frame).</summary>
    public const string ConsumerTooSlow = "ConsumerTooSlow";

    /// <summary>The server is not ready yet (HTTP 503 during bootstrap; retryable).</summary>
    public const string ServiceUnavailable = "ServiceUnavailable";
}

/// <summary>
/// Terminal Jetstream v2 failure: an XRPC error the managed stream cannot recover from,
/// an exhausted retry/re-backfill budget, or a protocol violation. Recoverable conditions
/// (reconnects, dictionary rotation, malformed frames, per-block download failures) are
/// handled internally and logged instead.
/// </summary>
public class JetstreamV2Exception : Exception
{
    /// <summary>
    /// Creates a new exception with a message.
    /// </summary>
    /// <param name="message">The failure description.</param>
    public JetstreamV2Exception(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new exception with a message and inner exception.
    /// </summary>
    /// <param name="message">The failure description.</param>
    /// <param name="innerException">The underlying failure.</param>
    public JetstreamV2Exception(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new exception carrying a structured XRPC error name.
    /// </summary>
    /// <param name="message">The failure description.</param>
    /// <param name="errorName">The XRPC error name (see <see cref="JetstreamV2ErrorNames"/>).</param>
    public JetstreamV2Exception(string message, string? errorName)
        : base(message)
    {
        ErrorName = errorName;
    }

    /// <summary>
    /// The structured XRPC error name when the failure came from a server error envelope
    /// (see <see cref="JetstreamV2ErrorNames"/>), otherwise null.
    /// </summary>
    public string? ErrorName { get; }
}
