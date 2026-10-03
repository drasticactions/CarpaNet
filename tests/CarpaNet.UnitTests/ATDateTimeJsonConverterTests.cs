using System.Text.Json;
using Xunit;

namespace CarpaNet.UnitTests;

public class ATDateTimeJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new() { Converters = { new ATDateTimeJsonConverter() } };

    [Theory]
    [InlineData(0, "2024-01-01T00:00:00.000Z")]
    [InlineData(9_999, "2024-01-01T00:00:00.000Z")] // sub-millisecond ticks are truncated
    [InlineData(1_239_999, "2024-01-01T00:00:00.123Z")] // truncated, not rounded
    public void Format_IsUtcWithMilliseconds(long ticks, string expected)
    {
        var value = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(ticks);
        Assert.Equal(expected, ATDateTimeJsonConverter.Format(value));
        Assert.Equal($"\"{expected}\"", JsonSerializer.Serialize(value, Options));
    }

    [Fact]
    public void Format_ConvertsToUtc()
    {
        var value = new DateTimeOffset(2024, 1, 1, 9, 30, 0, TimeSpan.FromHours(9.5));
        Assert.Equal("2024-01-01T00:00:00.000Z", ATDateTimeJsonConverter.Format(value));
    }

    [Theory]
    [InlineData("2024-01-01T00:00:00Z")]
    [InlineData("2024-01-01T00:00:00.000Z")]
    [InlineData("2024-01-01T00:00:00.000000Z")]
    [InlineData("2024-01-01T00:00:00.000000000Z")]
    [InlineData("2024-01-01T00:00:00+00:00")]
    [InlineData("2024-01-01T00:00:00-00:00")]
    [InlineData("2024-01-01T05:30:00.000+05:30")]
    [InlineData("2023-12-31T19:00:00.000000000000-05:00")]
    public void Read_AcceptsAtprotoDatetimes(string value)
    {
        var expected = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, JsonSerializer.Deserialize<DateTimeOffset>($"\"{value}\"", Options));
        Assert.True(ATDateTimeJsonConverter.TryParse(value, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void Read_KeepsSubMillisecondPrecision()
    {
        var result = JsonSerializer.Deserialize<DateTimeOffset>("\"2024-01-01T00:00:00.123456789Z\"", Options);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(1_234_567), result);
    }

    [Theory]
    [InlineData("\"not a date\"")]
    [InlineData("\"\"")]
    [InlineData("123")]
    [InlineData("null")]
    public void Read_RejectsInvalidValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTimeOffset>(json, Options));
    }

    [Fact]
    public void NullableValues_UseTheConverter()
    {
        Assert.Equal("{\"At\":\"2024-01-01T00:00:00.000Z\"}", JsonSerializer.Serialize(new Holder { At = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) }, Options));
    }

    private sealed class Holder
    {
        public DateTimeOffset? At { get; set; }
    }
}
