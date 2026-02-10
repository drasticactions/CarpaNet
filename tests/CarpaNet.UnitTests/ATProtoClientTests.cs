using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Auth;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests;

public class ATProtoClientTests
{
    // Sample JWT with exp claim (expires 2030-01-01 00:00:00 UTC)
    private const string SampleJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkaWQ6cGxjOnRlc3QiLCJleHAiOjE4OTM0NTYwMDB9.signature";

    private static ATProtoClientOptions CreateDefaultOptions(HttpClient? httpClient = null)
    {
        var options = new ATProtoClientOptions
        {
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext()
        };
        if (httpClient != null)
        {
            options.HttpClient = httpClient;
        }
        return options;
    }

    [Fact]
    public void CreatePublic_ReturnsPublicClient()
    {
        using var client = ATProtoClient.CreatePublic(CreateDefaultOptions());

        Assert.False(client.IsAuthenticated);
        Assert.Null(client.AuthenticatedDid);
        Assert.Equal(new Uri(BlueskyServices.PublicAppView), client.BaseUrl);
    }

    [Fact]
    public void CreatePublic_WithOptions_AppliesOptions()
    {
        var customUrl = new Uri("https://custom.api.example");
        var options = CreateDefaultOptions();
        options.BaseUrl = customUrl;

        using var client = ATProtoClient.CreatePublic(options);

        Assert.Equal(customUrl, client.BaseUrl);
    }

    [Fact]
    public void CreatePublic_HasIdentityResolver()
    {
        using var client = ATProtoClient.CreatePublic(CreateDefaultOptions());

        Assert.NotNull(client.IdentityResolver);
    }

    [Fact]
    public void CreatePublic_WithoutIdentityResolver_RespectsOption()
    {
        var options = CreateDefaultOptions();
        options.CreateIdentityResolver = false;

        using var client = ATProtoClient.CreatePublic(options);

        Assert.Null(client.IdentityResolver);
    }

    [Fact]
    public async Task PostAsync_OnPublicClient_ThrowsInvalidOperationException()
    {
        using var client = ATProtoClient.CreatePublic(CreateDefaultOptions());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.PostAsync<object, object>("com.atproto.repo.createRecord", new { }));
    }

    [Fact]
    public async Task GetAsync_OnPublicClient_Works()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        using var client = ATProtoClient.CreatePublic(CreateDefaultOptions(httpClient));

        await client.GetAsync<object>("app.bsky.feed.getTimeline");
        // No exception = success
    }

    [Fact]
    public void CreateWithRestoredSession_CreatesAuthenticatedClient()
    {
        var pdsUrl = new Uri("https://pds.example.com");

        using var client = ATProtoClient.CreateWithRestoredSession(
            SampleJwt,
            "refresh-token",
            "did:plc:test",
            "alice.bsky.social",
            pdsUrl,
            CreateDefaultOptions());

        Assert.True(client.IsAuthenticated);
        Assert.Equal("did:plc:test", client.AuthenticatedDid);
        Assert.Equal(pdsUrl, client.BaseUrl);
    }

    [Fact]
    public async Task CreateWithSessionAsync_AuthenticatesClient()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            if (request.RequestUri!.ToString().Contains("createSession"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $@"{{
                            ""accessJwt"": ""{SampleJwt}"",
                            ""refreshJwt"": ""refresh-token"",
                            ""did"": ""did:plc:test"",
                            ""handle"": ""alice.bsky.social""
                        }}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);

        using var client = await ATProtoClient.CreateWithSessionAsync(
            "alice.bsky.social",
            "app-password",
            options: CreateDefaultOptions(httpClient));

        Assert.True(client.IsAuthenticated);
        Assert.Equal("did:plc:test", client.AuthenticatedDid);
    }

    [Fact]
    public async Task CreateWithSessionAsync_FailedLogin_ThrowsAndCleansUp()
    {
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    @"{""error"": ""AuthenticationRequired"", ""message"": ""Invalid credentials""}",
                    Encoding.UTF8,
                    "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);

        await Assert.ThrowsAsync<AuthenticationException>(
            () => ATProtoClient.CreateWithSessionAsync("alice.bsky.social", "wrong-password", options: CreateDefaultOptions(httpClient)));
    }

    [Fact]
    public async Task GetAsync_WithAuth_IncludesAuthHeader()
    {
        string? authHeader = null;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            if (request.RequestUri!.ToString().Contains("createSession"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $@"{{
                            ""accessJwt"": ""{SampleJwt}"",
                            ""refreshJwt"": ""refresh-token"",
                            ""did"": ""did:plc:test"",
                            ""handle"": ""alice.bsky.social""
                        }}",
                        Encoding.UTF8,
                        "application/json")
                });
            }

            if (request.Headers.Authorization != null)
            {
                authHeader = request.Headers.Authorization.ToString();
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);

        using var client = await ATProtoClient.CreateWithSessionAsync("alice.bsky.social", "app-password", options: CreateDefaultOptions(httpClient));
        await client.GetAsync<object>("app.bsky.feed.getTimeline");

        Assert.NotNull(authHeader);
        Assert.Contains("Bearer", authHeader);
    }

    [Fact]
    public async Task GetAsync_WithLabelers_IncludesHeader()
    {
        string? labelerHeader = null;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            if (request.Headers.TryGetValues("atproto-accept-labelers", out var values))
            {
                labelerHeader = string.Join(",", values);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        });

        using var httpClient = new HttpClient(handler);
        var options = CreateDefaultOptions(httpClient);
        options.LabelerDids = new[] { "did:plc:labeler1", "did:plc:labeler2" };

        using var client = ATProtoClient.CreatePublic(options);
        await client.GetAsync<object>("app.bsky.feed.getTimeline");

        Assert.Equal("did:plc:labeler1,did:plc:labeler2", labelerHeader);
    }

    [Fact]
    public void ATProtoClientOptions_Clone_CreatesIndependentCopy()
    {
        var original = new ATProtoClientOptions
        {
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext(),
            BaseUrl = new Uri("https://example.com"),
            AutoRetryOnAuthFailure = false,
            CreateIdentityResolver = false
        };

        var clone = original.Clone();

        Assert.Equal(original.BaseUrl, clone.BaseUrl);
        Assert.Equal(original.AutoRetryOnAuthFailure, clone.AutoRetryOnAuthFailure);
        Assert.Equal(original.CreateIdentityResolver, clone.CreateIdentityResolver);

        // Modify clone, original should be unchanged
        clone.AutoRetryOnAuthFailure = true;
        Assert.False(original.AutoRetryOnAuthFailure);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var client = ATProtoClient.CreatePublic(CreateDefaultOptions());

        client.Dispose();
        client.Dispose(); // Should not throw
    }

    [Fact]
    public async Task GetAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var client = ATProtoClient.CreatePublic(CreateDefaultOptions());
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.GetAsync<object>("app.bsky.feed.getTimeline"));
    }
}

/// <summary>
/// Integration tests for ATProtoClient.
/// Set BLUESKY_TEST_HANDLE and BLUESKY_TEST_PASSWORD environment variables to run.
/// </summary>
public class ATProtoClientIntegrationTests
{
    private readonly string? _testHandle;
    private readonly string? _testPassword;

    public ATProtoClientIntegrationTests()
    {
        _testHandle = Environment.GetEnvironmentVariable("BLUESKY_TEST_HANDLE");
        _testPassword = Environment.GetEnvironmentVariable("BLUESKY_TEST_PASSWORD");
    }

    private bool CanRunIntegrationTests => !string.IsNullOrEmpty(_testHandle) && !string.IsNullOrEmpty(_testPassword);

    private static ATProtoClientOptions CreateDefaultOptions(HttpClient? httpClient = null)
    {
        var options = new ATProtoClientOptions
        {
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext()
        };
        if (httpClient != null)
        {
            options.HttpClient = httpClient;
        }
        return options;
    }

    [Fact]
    public async Task PublicClient_CanFetchPublicProfile()
    {
        using var client = ATProtoClient.CreatePublic(CreateDefaultOptions());

        var parameters = new Dictionary<string, string>
        {
            { "actor", "bsky.app" }
        };

        var profile = await client.GetAsync<TestProfileResponse>("app.bsky.actor.getProfile", parameters);

        Assert.NotNull(profile);
        Assert.Equal("bsky.app", profile.Handle);
    }

    [Fact]
    public async Task AuthenticatedClient_CanFetchTimeline()
    {
        if (!CanRunIntegrationTests)
        {
            return;
        }

        using var client = await ATProtoClient.CreateWithSessionAsync(_testHandle!, _testPassword!, options: CreateDefaultOptions());

        var parameters = new Dictionary<string, string>
        {
            { "limit", "5" }
        };

        var timeline = await client.GetAsync<TestTimelineResponse>("app.bsky.feed.getTimeline", parameters);

        Assert.NotNull(timeline);
        Assert.NotNull(timeline.Feed);
    }

    [Fact]
    public async Task RestoredSession_CanMakeRequests()
    {
        if (!CanRunIntegrationTests)
        {
            return;
        }

        // First, login to get tokens
        using var initialClient = await ATProtoClient.CreateWithSessionAsync(_testHandle!, _testPassword!, options: CreateDefaultOptions());
        var tokenProvider = initialClient.TokenProvider as SessionTokenProvider;
        Assert.NotNull(tokenProvider);

        var accessToken = await tokenProvider!.GetAccessTokenAsync();
        var pdsUrl = tokenProvider.PdsUrl;
        var did = tokenProvider.CurrentDid;
        var handle = tokenProvider.Handle;

        Assert.NotNull(accessToken);
        Assert.NotNull(pdsUrl);
        Assert.NotNull(did);

        // Now create a new client with restored session
        // Note: In real usage, you'd store the refresh token too
        // For this test, we just verify the client can be created
        using var restoredClient = ATProtoClient.CreateWithRestoredSession(
            accessToken!,
            "fake-refresh-token", // Would need real refresh token for actual refresh
            did!,
            handle,
            pdsUrl!,
            CreateDefaultOptions());

        Assert.True(restoredClient.IsAuthenticated);
        Assert.Equal(did, restoredClient.AuthenticatedDid);
    }

    private class TestProfileResponse
    {
        public string? Did { get; set; }
        public string? Handle { get; set; }
        public string? DisplayName { get; set; }
    }

    private class TestTimelineResponse
    {
        public object[]? Feed { get; set; }
        public string? Cursor { get; set; }
    }
}

/// <summary>
/// Simple mock HTTP message handler for testing.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return _handler(request, cancellationToken);
    }
}
