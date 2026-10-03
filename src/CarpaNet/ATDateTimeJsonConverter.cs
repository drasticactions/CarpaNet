using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet;

/// <summary>
/// JSON converter for atproto <c>datetime</c> values.
/// </summary>
/// <remarks>
/// Values are written in the form atproto recommends and the TypeScript SDK writes:
/// <c>YYYY-MM-DDTHH:mm:ss.sssZ</c> (UTC, millisecond precision). Reading accepts any valid
/// atproto datetime (any number of fractional digits, <c>Z</c> or a numeric offset).
/// </remarks>
public sealed class ATDateTimeJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string CanonicalFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// The maximum number of fractional second digits .NET parses (100 ns ticks).
    /// </summary>
    private const int MaxFractionDigits = 7;

    /// <summary>
    /// Formats a value as an atproto datetime: <c>YYYY-MM-DDTHH:mm:ss.sssZ</c>.
    /// The value is converted to UTC and truncated to milliseconds.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The formatted datetime string.</returns>
    public static string Format(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString(CanonicalFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses an atproto datetime string. Fractional seconds beyond 100 ns precision are truncated.
    /// A value without an offset is treated as UTC.
    /// </summary>
    /// <param name="value">The string to parse.</param>
    /// <param name="result">The parsed value.</param>
    /// <returns>True if the string was parsed.</returns>
    public static bool TryParse(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = TrimExtraFractionDigits(value!.Trim());
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out result);
    }

    /// <inheritdoc/>
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a datetime string, got {reader.TokenType}.");
        }

        if (reader.TryGetDateTimeOffset(out var fast))
        {
            return fast;
        }

        var text = reader.GetString();
        if (TryParse(text, out var parsed))
        {
            return parsed;
        }

        throw new JsonException($"Invalid datetime value: '{text}'.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(Format(value));
    }

    /// <summary>
    /// Removes fractional second digits beyond what .NET can parse (atproto allows any number).
    /// </summary>
    private static string TrimExtraFractionDigits(string value)
    {
        var timeIndex = value.IndexOf('T');
        if (timeIndex < 0)
        {
            timeIndex = value.IndexOf('t');
        }

        if (timeIndex < 0)
        {
            return value;
        }

        var dot = value.IndexOf('.', timeIndex);
        if (dot < 0)
        {
            return value;
        }

        var end = dot + 1;
        while (end < value.Length && value[end] >= '0' && value[end] <= '9')
        {
            end++;
        }

        var digits = end - dot - 1;
        if (digits <= MaxFractionDigits)
        {
            return value;
        }

        return value.Substring(0, dot + 1 + MaxFractionDigits) + value.Substring(end);
    }
}
