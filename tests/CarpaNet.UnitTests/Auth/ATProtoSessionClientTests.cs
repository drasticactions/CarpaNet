using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CarpaNet;
using CarpaNet.Auth;
using CarpaNet.Cbor;
using Xunit;

namespace CarpaNet.UnitTests.Auth;

public class ATProtoSessionClientTests
{
    // Sample JWT with exp claim (expires 2030-01-01 00:00:00 UTC)
    private const string SampleJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJkaWQ6cGxjOnRlc3QiLCJleHAiOjE4OTM0NTYwMDB9.signature";

    private static ATProtoClient CreateClient(HttpClient? httpClient = null)
    {
        return ATProtoClient.Create(new ATProtoClientOptions
        {
            HttpClient = httpClient,
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext()
        });
    }

    [Fact]
    public void Constructor_Default_NotAuthenticated()
    {
        using var client = CreateClient();

        Assert.False(client.IsAuthenticated);
        Assert.Null(client.AuthenticatedDid);
        Assert.Null(client.Handle);
    }

    [Fact]
    public async Task LoginAsync_WithMockHandler_AuthenticatesClient()
    {
        var requestCount = 0;
        var handler = new MockHttpMessageHandler((request, ct) =>
        {
            requestCount++;
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
        using var client = CreateClient(httpClient);

        await client.LoginAsync("alice.bsky.social", "app-password");

        Assert.True(client.IsAuthenticated);
        Assert.Equal("did:plc:test", client.AuthenticatedDid);
        Assert.Equal("alice.bsky.social", client.Handle);
    }

    [Fact]
    public async Task GetAsync_AfterLogin_IncludesAuthHeader()
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
        using var client = CreateClient(httpClient);

        await client.LoginAsync("alice.bsky.social", "app-password");
        await client.GetAsync<object>("app.bsky.feed.getTimeline");

        Assert.NotNull(authHeader);
        Assert.StartsWith("Bearer ", authHeader);
        Assert.Contains(SampleJwt, authHeader);
    }

    [Fact]
    public async Task PostAsync_AfterLogin_SendsRequestWithAuth()
    {
        string? authHeader = null;
        string? requestBody = null;
        var handler = new MockHttpMessageHandler(async (request, ct) =>
        {
            if (request.RequestUri!.ToString().Contains("createSession"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
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
                };
            }

            if (request.Headers.Authorization != null)
            {
                authHeader = request.Headers.Authorization.ToString();
            }

            if (request.Content != null)
            {
                requestBody = await request.Content.ReadAsStringAsync();
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    @"{""uri"": ""at://did:plc:test/app.bsky.feed.post/123"", ""cid"": ""bafytest""}",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        await client.LoginAsync("alice.bsky.social", "app-password");
        var result = await client.PostAsync<object, TestCreateRecordResponse>(
            "com.atproto.repo.createRecord",
            new { repo = "did:plc:test", collection = "app.bsky.feed.post" });

        Assert.NotNull(authHeader);
        Assert.StartsWith("Bearer ", authHeader);
        Assert.NotNull(requestBody);
        Assert.NotNull(result);
    }

    [Fact]
    public void RestoreSession_SetsAuthenticatedState()
    {
        using var client = CreateClient();
        var pdsUrl = new Uri("https://pds.example.com");

        client.RestoreSession(SampleJwt, "refresh-token", "did:plc:test", "alice.bsky.social", pdsUrl);

        Assert.True(client.IsAuthenticated);
        Assert.Equal("did:plc:test", client.AuthenticatedDid);
        Assert.Equal("alice.bsky.social", client.Handle);
    }

    [Fact]
    public async Task LogoutAsync_ClearsSession()
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

            // deleteSession endpoint
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);

        await client.LoginAsync("alice.bsky.social", "app-password");
        Assert.True(client.IsAuthenticated);

        await client.LogoutAsync();

        Assert.False(client.IsAuthenticated);
        Assert.Null(client.AuthenticatedDid);
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

    private class TestCreateRecordResponse
    {
        public string? Uri { get; set; }
        public string? Cid { get; set; }
    }
}

/// <summary>
/// Integration tests for ATProtoClient session lifecycle.
/// Set BLUESKY_TEST_HANDLE and BLUESKY_TEST_PASSWORD environment variables to run.
/// </summary>
public class ATProtoSessionClientIntegrationTests
{
    private readonly string? _testHandle;
    private readonly string? _testPassword;

    public ATProtoSessionClientIntegrationTests()
    {
        _testHandle = Environment.GetEnvironmentVariable("BLUESKY_TEST_HANDLE");
        _testPassword = Environment.GetEnvironmentVariable("BLUESKY_TEST_PASSWORD");
    }

    private bool CanRunIntegrationTests => !string.IsNullOrEmpty(_testHandle) && !string.IsNullOrEmpty(_testPassword);

    private static ATProtoClient CreateClient(HttpClient? httpClient = null)
    {
        return ATProtoClient.Create(new ATProtoClientOptions
        {
            HttpClient = httpClient,
            JsonOptions = TestHelpers.CreateJsonOptions(),
            CborContext = TestHelpers.CreateCborContext()
        });
    }

    [Fact]
    public async Task FullWorkflow_LoginAndFetchProfile()
    {
        if (!CanRunIntegrationTests)
        {
            return;
        }

        using var client = CreateClient();

        // Login
        var session = await client.LoginAsync(_testHandle!, _testPassword!);
        Assert.True(client.IsAuthenticated);
        Assert.NotEmpty(session.Did);

        // Fetch own profile
        var parameters = new Dictionary<string, string>
        {
            { "actor", session.Did }
        };

        var profile = await client.GetAsync<TestProfileResponse>("app.bsky.actor.getProfile", parameters);

        Assert.NotNull(profile);
        Assert.Equal(session.Did, profile.Did);
        Assert.Equal(session.Handle, profile.Handle);
    }

    [Fact]
    public async Task GetTimeline_ReturnsResults()
    {
        if (!CanRunIntegrationTests)
        {
            return;
        }

        using var client = CreateClient();

        await client.LoginAsync(_testHandle!, _testPassword!);

        var parameters = new Dictionary<string, string>
        {
            { "limit", "5" }
        };

        var timeline = await client.GetAsync<TestTimelineResponse>("app.bsky.feed.getTimeline", parameters);

        Assert.NotNull(timeline);
        Assert.NotNull(timeline.Feed);
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
