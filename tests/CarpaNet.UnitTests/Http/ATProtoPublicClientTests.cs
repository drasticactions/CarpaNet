using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Cbor;
using CarpaNet.Identity;
using Xunit;

namespace CarpaNet.UnitTests.Http;

public class ATProtoPublicClientTests
{
    private static ATProtoClient CreateClient(HttpClient? httpClient = null, Uri? baseUrl = null, IReadOnlyList<string>? labelerDids = null)
    {
        return ATProtoClient.Create(new ATProtoClientOptions
        {
            HttpClient = httpClient,
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext(),
            BaseUrl = baseUrl,
            LabelerDids = labelerDids
        });
    }

    [Fact]
    public void Constructor_UsesPublicAppView()
    {
        using var client = CreateClient();

        Assert.Equal(new Uri(BlueskyServices.PublicAppView), client.BaseUrl);
        Assert.False(client.IsAuthenticated);
        Assert.Null(client.AuthenticatedDid);
    }

    [Fact]
    public void Constructor_WithCustomBaseUrl_UsesProvidedUrl()
    {
        var customUrl = new Uri("https://custom.pds.example");
        using var httpClient = new HttpClient();
        using var client = CreateClient(httpClient, customUrl);

        Assert.Equal(customUrl, client.BaseUrl);
    }

    [Fact]
    public void IsAuthenticated_AlwaysFalse()
    {
        using var client = CreateClient();

        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public void AuthenticatedDid_AlwaysNull()
    {
        using var client = CreateClient();

        Assert.Null(client.AuthenticatedDid);
    }

    [Fact]
    public async Task PostAsync_ThrowsATProtoException()
    {
        using var client = CreateClient();

        await Assert.ThrowsAsync<ATProtoException>(
            () => client.PostAsync<object, object>("com.atproto.repo.createRecord", new { }));
    }

    [Fact]
    public async Task PostAsync_WithProxy_ThrowsATProtoException()
    {
        using var client = CreateClient();

        await Assert.ThrowsAsync<ATProtoException>(
            () => client.PostAsync<object, object>("chat.bsky.convo.sendMessage", "did:web:api.bsky.chat#bsky_chat", new { }));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var client = CreateClient();

        client.Dispose();
        client.Dispose(); // Should not throw
    }

    [Fact]
    public async Task GetAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var client = CreateClient();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.GetAsync<object>("app.bsky.feed.getTimeline"));
    }

    [Fact]
    public async Task GetAsync_WithMockHandler_MakesCorrectRequest()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/xrpc/app.bsky.actor.getProfile", request.RequestUri!.ToString());
            Assert.Contains("actor=alice.bsky.social", request.RequestUri.Query);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"did":"did:plc:test","handle":"alice.bsky.social"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var parameters = new Dictionary<string, string>
        {
            { "actor", "alice.bsky.social" }
        };

        var result = await client.GetAsync<TestProfileResponse>(
            "app.bsky.actor.getProfile",
            parameters);

        Assert.Equal("did:plc:test", result.Did);
        Assert.Equal("alice.bsky.social", result.Handle);
    }

    [Fact]
    public async Task GetAsync_WithErrorResponse_ThrowsATProtoException()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent(
                    """{"error":"NotFound","message":"Profile not found"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var ex = await Assert.ThrowsAsync<ATProtoException>(
            () => client.GetAsync<object>("app.bsky.actor.getProfile", new Dictionary<string, string>
            {
                { "actor", "doesnotexist.invalid" }
            }));

        Assert.Equal("NotFound", ex.ErrorCode);
        Assert.Contains("Profile not found", ex.Message);
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WithRateLimitError_ThrowsRateLimitException()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(
                    """{"error":"RateLimitExceeded","message":"Too many requests"}""",
                    Encoding.UTF8,
                    "application/json")
            };
            response.Headers.Add("RateLimit-Limit", "100");
            response.Headers.Add("RateLimit-Remaining", "0");
            response.Headers.Add("Retry-After", "30");
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var ex = await Assert.ThrowsAsync<RateLimitException>(
            () => client.GetAsync<object>("app.bsky.feed.getTimeline"));

        Assert.NotNull(ex.RateLimitInfo);
        Assert.Equal(100, ex.RateLimitInfo!.Limit);
        Assert.Equal(0, ex.RateLimitInfo.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
    }

    [Fact]
    public async Task GetAsync_WithAuthError_ThrowsAuthenticationException()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    """{"error":"AuthenticationRequired","message":"Please log in"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => client.GetAsync<object>("app.bsky.feed.getTimeline"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task GetAsync_WithBadRequestError_ThrowsValidationException()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    """{"error":"InvalidRequest","message":"Invalid parameter value"}""",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => client.GetAsync<object>("app.bsky.feed.getTimeline", new Dictionary<string, string>
            {
                { "limit", "-1" }
            }));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("InvalidRequest", ex.ErrorCode);
    }

    [Fact]
    public async Task GetAsync_WithLabelers_IncludesHeader()
    {
        string? receivedLabelerHeader = null;

        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            if (request.Headers.TryGetValues("atproto-accept-labelers", out var values))
            {
                receivedLabelerHeader = string.Join(",", values);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var labelers = new[] { "did:plc:labeler1", "did:plc:labeler2" };
        using var client = CreateClient(httpClient, labelerDids: labelers);

        await client.GetAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:plc:labeler1,did:plc:labeler2", receivedLabelerHeader);
    }

    private class TestProfileResponse
    {
        public string? Did { get; set; }
        public string? Handle { get; set; }
    }
}
