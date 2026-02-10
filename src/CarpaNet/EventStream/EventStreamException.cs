using System;

namespace CarpaNet.EventStream;

/// <summary>
/// Exception thrown when an error occurs in an event stream.
/// </summary>
public sealed class EventStreamException : Exception
{
    /// <summary>
    /// The error name from the event stream.
    /// </summary>
    public string? ErrorName { get; }

    /// <summary>
    /// The error header from the stream.
    /// </summary>
    public EventStreamHeader? Header { get; }

    /// <summary>
    /// Creates a new EventStreamException.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EventStreamException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new EventStreamException.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public EventStreamException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new EventStreamException from an error header.
    /// </summary>
    /// <param name="header">The error header.</param>
    public EventStreamException(EventStreamHeader header)
        : base(header.Message ?? header.Error ?? "Unknown event stream error")
    {
        Header = header;
        ErrorName = header.Error;
    }

    /// <summary>
    /// Creates a new EventStreamException with error name and message.
    /// </summary>
    /// <param name="errorName">The error name.</param>
    /// <param name="message">The error message.</param>
    public EventStreamException(string errorName, string message) : base(message)
    {
        ErrorName = errorName;
    }
}
