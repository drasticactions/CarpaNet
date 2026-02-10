using System;

namespace CarpaNet.EventStream;

/// <summary>
/// Represents a parsed event stream frame containing header and payload.
/// </summary>
public sealed class EventStreamFrame
{
    /// <summary>
    /// The frame header containing operation type and message type.
    /// </summary>
    public EventStreamHeader Header { get; }

    /// <summary>
    /// The raw payload bytes (the second CBOR object in the frame).
    /// </summary>
    public ReadOnlyMemory<byte> PayloadBytes { get; }

    /// <summary>
    /// Creates a new EventStreamFrame.
    /// </summary>
    /// <param name="header">The parsed header.</param>
    /// <param name="payloadBytes">The raw payload bytes.</param>
    public EventStreamFrame(EventStreamHeader header, ReadOnlyMemory<byte> payloadBytes)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        PayloadBytes = payloadBytes;
    }
}
