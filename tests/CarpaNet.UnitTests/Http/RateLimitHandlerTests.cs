using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Http;
using Xunit;

namespace CarpaNet.UnitTests.Http;

public class RateLimitHandlerTests
{
    [Fact]
    public async Task SendAsync_NonRateLimitResponse_ReturnsImmediately()
    {
        var innerHandler = new MockHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new RateLimitHandler(innerHandler);
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.com/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_RateLimitWithRetry_RetriesSuccessfully()
    {
        var callCount = 0;
        var innerHandler = new MockHttpMessageHandler((request, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                var response = new HttpResponseMessage((HttpStatusCode)429);
                response.Headers.Add("Retry-After", "0"); // Immediate retry
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new RateLimitHandler(innerHandler)
        {
            AutoRetryOnRateLimit = true,
            MaxRetries = 3
        };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.com/api");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SendAsync_RateLimitDisabled_DoesNotRetry()
    {
        var callCount = 0;
        var innerHandler = new MockHttpMessageHandler((request, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429));
        });

        var handler = new RateLimitHandler(innerHandler)
        {
            AutoRetryOnRateLimit = false
        };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.com/api");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task SendAsync_ExceedsMaxRetries_ReturnsRateLimitResponse()
    {
        var callCount = 0;
        var innerHandler = new MockHttpMessageHandler((request, ct) =>
        {
            callCount++;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.Add("Retry-After", "0");
            return Task.FromResult(response);
        });

        var handler = new RateLimitHandler(innerHandler)
        {
            AutoRetryOnRateLimit = true,
            MaxRetries = 2
        };
        using var client = new HttpClient(handler);

        var response = await client.GetAsync("https://example.com/api");

        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SendAsync_RateLimitEncountered_RaisesEvent()
    {
        var eventRaised = false;
        var innerHandler = new MockHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.Add("Retry-After", "0");
            return Task.FromResult(response);
        });

        var handler = new RateLimitHandler(innerHandler)
        {
            AutoRetryOnRateLimit = false
        };
        handler.RateLimitEncountered += (sender, args) =>
        {
            eventRaised = true;
            Assert.Equal(1, args.Attempt);
        };

        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.com/api");

        Assert.True(eventRaised);
    }

    [Fact]
    public async Task SendAsync_RespectsRetryAfterHeader()
    {
        var callTimes = new System.Collections.Generic.List<DateTime>();
        var innerHandler = new MockHttpMessageHandler((request, ct) =>
        {
            callTimes.Add(DateTime.UtcNow);
            if (callTimes.Count == 1)
            {
                var response = new HttpResponseMessage((HttpStatusCode)429);
                response.Headers.Add("Retry-After", "1"); // Wait 1 second
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var handler = new RateLimitHandler(innerHandler)
        {
            AutoRetryOnRateLimit = true,
            JitterFactor = 0 // Disable jitter for predictable timing
        };
        using var client = new HttpClient(handler);

        await client.GetAsync("https://example.com/api");

        Assert.Equal(2, callTimes.Count);
        var elapsed = callTimes[1] - callTimes[0];
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(900), $"Expected at least 900ms delay, got {elapsed.TotalMilliseconds}ms");
    }

    [Fact]
    public void DefaultSettings_AreCorrect()
    {
        var handler = new RateLimitHandler();

        Assert.True(handler.AutoRetryOnRateLimit);
        Assert.Equal(3, handler.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(1), handler.BaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), handler.MaxDelay);
        Assert.Equal(0.2, handler.JitterFactor);
    }
}

public class HttpClientFactoryTests
{
    [Fact]
    public void Create_ReturnsConfiguredClient()
    {
        var options = new HttpClientFactoryOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            UserAgent = "TestAgent/1.0"
        };

        using var client = HttpClientFactory.Create(options);

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.Contains("TestAgent", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public void Create_WithDefaultOptions_Succeeds()
    {
        using var client = HttpClientFactory.Create();

        Assert.NotNull(client);
    }

    [Fact]
    public void CreateHandler_WithRateLimitHandler_ReturnsChainedHandlers()
    {
        var options = new HttpClientFactoryOptions
        {
            EnableRateLimitHandler = true
        };

        using var handler = HttpClientFactory.CreateHandler(options);

        Assert.IsType<RateLimitHandler>(handler);
    }

    [Fact]
    public void CreateHandler_WithoutRateLimitHandler_ReturnsBaseHandler()
    {
        var options = new HttpClientFactoryOptions
        {
            EnableRateLimitHandler = false
        };

        using var handler = HttpClientFactory.CreateHandler(options);

        Assert.IsNotType<RateLimitHandler>(handler);
    }
}

