using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Http;

/// <summary>
/// Tests for URL building and XRPC handling, tested through the public client.
/// </summary>
public class XrpcUrlBuildingTests
{
    private static ATProtoPublicClient CreateClient(HttpClient httpClient, Uri baseUrl)
    {
        return new ATProtoPublicClient(
            httpClient,
            TestHelpers.CreateJsonOptions(),
            TestHelpers.CreateCborContext(),
            baseUrl);
    }

    [Fact]
    public async Task GetAsync_WithNsidOnly_BuildsCorrectUrl()
    {
        string? capturedUrl = null;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://bsky.social") };
        using var client = CreateClient(httpClient, new Uri("https://bsky.social"));

        await client.GetAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("https://bsky.social/xrpc/app.bsky.feed.getTimeline", capturedUrl);
    }

    [Fact]
    public async Task GetAsync_WithParameters_BuildsCorrectQueryString()
    {
        string? capturedUrl = null;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://bsky.social") };
        using var client = CreateClient(httpClient, new Uri("https://bsky.social"));

        var parameters = new Dictionary<string, string>
        {
            { "limit", "50" },
            { "cursor", "abc123" }
        };

        await client.GetAsync<object>("app.bsky.feed.getTimeline", parameters);

        // Check both parameters are present
        Assert.Contains("limit=50", capturedUrl);
        Assert.Contains("cursor=abc123", capturedUrl);
        Assert.Contains("&", capturedUrl);
    }

    [Fact]
    public async Task GetAsync_WithSpecialCharacters_EncodesCorrectly()
    {
        Uri? capturedUri = null;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, new Uri("https://bsky.social"));

        var parameters = new Dictionary<string, string>
        {
            { "query", "hello world" }
        };

        await client.GetAsync<object>("app.bsky.feed.searchPosts", parameters);

        // The query string should contain our value - check via Query property which preserves encoding
        // or via the decoded string value
        Assert.NotNull(capturedUri);
        var query = capturedUri!.Query;
        // Query includes the '?' prefix
        Assert.Contains("query=hello", query.TrimStart('?'));
    }

    [Fact]
    public async Task GetAsync_WithEmptyParameterValue_SkipsParameter()
    {
        string? capturedUrl = null;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            capturedUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient, new Uri("https://bsky.social"));

        var parameters = new Dictionary<string, string>
        {
            { "limit", "50" },
            { "cursor", "" } // Empty value
        };

        await client.GetAsync<object>("app.bsky.feed.getTimeline", parameters);

        Assert.Equal("https://bsky.social/xrpc/app.bsky.feed.getTimeline?limit=50", capturedUrl);
    }
}

public class RateLimitInfoTests
{
    [Fact]
    public void FromHeaders_ParsesAllHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            { "RateLimit-Limit", "3000" },
            { "RateLimit-Remaining", "2500" },
            { "RateLimit-Reset", "1704067200" },
            { "RateLimit-Policy", "3000;w=300" }
        };

        var info = RateLimitInfo.FromHeaders(name =>
            headers.TryGetValue(name, out var value) ? value : null);

        Assert.NotNull(info);
        Assert.Equal(3000, info!.Limit);
        Assert.Equal(2500, info.Remaining);
        Assert.Equal(1704067200L, info.ResetUnixTime);
        Assert.Equal("3000;w=300", info.Policy);
    }

    [Fact]
    public void FromHeaders_ParsesRetryAfter()
    {
        var headers = new Dictionary<string, string>
        {
            { "Retry-After", "60" }
        };

        var info = RateLimitInfo.FromHeaders(name =>
            headers.TryGetValue(name, out var value) ? value : null);

        Assert.NotNull(info);
        Assert.Equal(60, info!.RetryAfterSeconds);
    }

    [Fact]
    public void FromHeaders_ReturnsNullForNoHeaders()
    {
        var info = RateLimitInfo.FromHeaders(_ => null);

        Assert.Null(info);
    }

    [Fact]
    public void ResetTime_ConvertsUnixTimestamp()
    {
        var info = new RateLimitInfo
        {
            ResetUnixTime = 1704067200
        };

        var resetTime = info.ResetTime;

        Assert.NotNull(resetTime);
        Assert.Equal(2024, resetTime!.Value.Year);
        Assert.Equal(1, resetTime.Value.Month);
        Assert.Equal(1, resetTime.Value.Day);
    }

    [Fact]
    public void GetRetryAfter_PrefersRetryAfterHeader()
    {
        var info = new RateLimitInfo
        {
            RetryAfterSeconds = 60,
            ResetUnixTime = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()
        };

        var retry = info.GetRetryAfter();

        Assert.NotNull(retry);
        Assert.Equal(TimeSpan.FromSeconds(60), retry);
    }

    [Fact]
    public void GetRetryAfter_FallsBackToResetTime()
    {
        var resetTime = DateTimeOffset.UtcNow.AddMinutes(2);
        var info = new RateLimitInfo
        {
            ResetUnixTime = resetTime.ToUnixTimeSeconds()
        };

        var retry = info.GetRetryAfter();

        Assert.NotNull(retry);
        // Should be approximately 2 minutes (allow some tolerance)
        Assert.True(retry!.Value.TotalMinutes >= 1.9 && retry.Value.TotalMinutes <= 2.1);
    }

    [Fact]
    public void GetRetryAfter_ReturnsZeroForPastResetTime()
    {
        var info = new RateLimitInfo
        {
            ResetUnixTime = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()
        };

        var retry = info.GetRetryAfter();

        Assert.NotNull(retry);
        Assert.Equal(TimeSpan.Zero, retry);
    }

    [Fact]
    public void GetRetryAfter_ReturnsNullForNoData()
    {
        var info = new RateLimitInfo();

        var retry = info.GetRetryAfter();

        Assert.Null(retry);
    }
}

public class ATProtoExceptionTests
{
    [Fact]
    public void ATProtoException_SetsAllProperties()
    {
        var ex = new ATProtoException("Test message", "InvalidRequest", HttpStatusCode.BadRequest);

        Assert.Equal("Test message", ex.Message);
        Assert.Equal("InvalidRequest", ex.ErrorCode);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public void ATProtoException_WithInnerException_SetsInnerException()
    {
        var inner = new Exception("Inner");
        var ex = new ATProtoException("Outer", inner, "Error", HttpStatusCode.InternalServerError);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void AuthenticationException_ShouldRefresh_TrueForExpiredToken()
    {
        var ex = new AuthenticationException("Token expired", "ExpiredToken");

        Assert.True(ex.ShouldRefresh);
    }

    [Fact]
    public void AuthenticationException_ShouldRefresh_FalseForOtherErrors()
    {
        var ex = new AuthenticationException("Invalid token", "InvalidToken");

        Assert.False(ex.ShouldRefresh);
    }

    [Fact]
    public void AuthenticationException_HasCorrectStatusCode()
    {
        var ex = new AuthenticationException("Unauthorized");

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public void RateLimitException_HasCorrectStatusCode()
    {
        var ex = new RateLimitException("Rate limited");

        Assert.Equal((HttpStatusCode)429, ex.StatusCode);
    }

    [Fact]
    public void RateLimitException_StoresRateLimitInfo()
    {
        var info = new RateLimitInfo { Limit = 100, Remaining = 0 };
        var ex = new RateLimitException("Rate limited", info);

        Assert.Same(info, ex.RateLimitInfo);
    }

    [Fact]
    public void RateLimitException_RetryAfter_DelegatesToRateLimitInfo()
    {
        var info = new RateLimitInfo { RetryAfterSeconds = 30 };
        var ex = new RateLimitException("Rate limited", info);

        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public void ValidationException_HasCorrectStatusCode()
    {
        var ex = new ValidationException("Invalid request");

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }
}
