using System;

namespace CarpaNet.EventStream;

/// <summary>
/// Represents the header of an event stream frame.
/// </summary>
public sealed class EventStreamHeader
{
    /// <summary>
    /// Operation type indicating regular message (1), error (-1), or info (0).
    /// </summary>
    public int Op { get; set; }

    /// <summary>
    /// Message type discriminator (e.g., "#commit", "#handle", "#identity").
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// Error name when Op is -1.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Error message when Op is -1.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Whether this frame represents a message (Op = 1).
    /// </summary>
    public bool IsMessage => Op == 1;

    /// <summary>
    /// Whether this frame represents an error (Op = -1).
    /// </summary>
    public bool IsError => Op == -1;

    /// <summary>
    /// Whether this frame represents an info/keepalive (Op = 0).
    /// </summary>
    public bool IsInfo => Op == 0;
}
