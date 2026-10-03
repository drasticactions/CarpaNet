using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CarpaNet.Auth;
using CarpaNet.UnitTests.Http;
using Xunit;

namespace CarpaNet.UnitTests.Auth;

/// <summary>
/// Tests for <see cref="INotifySessionInvalidated"/> on <see cref="SessionTokenProvider"/> and for
/// <see cref="SessionTokenProvider.RefreshAsync"/> refreshing even while the token is still valid.
/// </summary>
public class SessionInvalidationTests
{
    private const string Did = "did:plc:user";
    private static readonly Uri Pds = new("https://pds.user.example");

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "ExpiredToken")]
    [InlineData(HttpStatusCode.Unauthorized, "InvalidToken")]
    public async Task RejectedRefresh_RaisesSessionInvalidatedOnce_AndClearsTokens(HttpStatusCode status, string error)
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json($"{{\"error\":\"{error}\",\"message\":\"x\"}}", status));
        using var provider = CreateProvider(handler);
        var events = new List<SessionInvalidatedEventArgs>();
        provider.SessionInvalidated += (_, e) => events.Add(e);

        await Assert.ThrowsAnyAsync<ATProtoException>(() => provider.RefreshAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());

        var raised = Assert.Single(events);
        Assert.Equal(Did, raised.Did);
        Assert.Equal(error, raised.Reason);
        Assert.Null(provider.AccessJwt);
        Assert.Null(provider.RefreshJwt);
        Assert.Equal(Did, provider.CurrentDid);
        Assert.False(provider.HasValidToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData((HttpStatusCode)429)]
    public async Task TemporaryFailure_DoesNotInvalidate(HttpStatusCode status)
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Status(status));
        using var provider = CreateProvider(handler);
        var raised = false;
        provider.SessionInvalidated += (_, _) => raised = true;

        await Assert.ThrowsAnyAsync<ATProtoException>(() => provider.RefreshAsync());

        Assert.False(raised);
        Assert.NotNull(provider.RefreshJwt);
    }

    [Fact]
    public async Task RestoreSession_ResetsInvalidation()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json("{\"error\":\"ExpiredToken\"}", HttpStatusCode.BadRequest));
        using var provider = CreateProvider(handler);
        var count = 0;
        provider.SessionInvalidated += (_, _) => count++;

        await Assert.ThrowsAnyAsync<ATProtoException>(() => provider.RefreshAsync());
        Restore(provider);
        await Assert.ThrowsAnyAsync<ATProtoException>(() => provider.RefreshAsync());

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task RefreshAsync_WhileTokenStillValid_StillRefreshes()
    {
        var fresh = XrpcRequestPipelineTests.CreateJwt(Did, DateTimeOffset.UtcNow.AddHours(3), "fresh");
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json(XrpcRequestPipelineTests.SessionJson(fresh)));
        using var provider = CreateProvider(handler);
        Assert.True(provider.HasValidToken);

        await provider.RefreshAsync();

        Assert.Single(handler.Requests);
        Assert.Equal(fresh, provider.AccessJwt);
    }

    [Fact]
    public async Task ConcurrentRefreshes_HitTheServerOnce()
    {
        var gate = new TaskCompletionSource<bool>();
        var fresh = XrpcRequestPipelineTests.CreateJwt(Did, DateTimeOffset.UtcNow.AddHours(3), "fresh");
        var handler = new AsyncHandler(async () =>
        {
            await gate.Task;
            return XrpcRequestPipelineTests.Json(XrpcRequestPipelineTests.SessionJson(fresh));
        });
        using var provider = new SessionTokenProvider(new HttpClient(handler));
        Restore(provider);

        var refreshes = Enumerable.Range(0, 5).Select(_ => provider.RefreshAsync()).ToArray();
        gate.SetResult(true);
        await Task.WhenAll(refreshes);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ATProtoClient_RejectedRefreshOn401_ReturnsOriginalError()
    {
        var handler = new RecordingHandler(r => r.Uri.AbsolutePath.EndsWith("refreshSession", StringComparison.Ordinal)
            ? XrpcRequestPipelineTests.Json("{\"error\":\"ExpiredToken\"}", HttpStatusCode.BadRequest)
            : XrpcRequestPipelineTests.Json("{\"error\":\"InvalidToken\"}", HttpStatusCode.Unauthorized));
        using var client = XrpcRequestPipelineTests.CreateSessionClient(handler, out _);
        var provider = (INotifySessionInvalidated)client.TokenProvider!;
        var raised = false;
        provider.SessionInvalidated += (_, _) => raised = true;

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => client.GetAsync<JsonElement>("com.example.get"));

        Assert.Equal("InvalidToken", ex.ErrorCode);
        Assert.True(raised);
    }

    private static SessionTokenProvider CreateProvider(RecordingHandler handler)
    {
        var provider = new SessionTokenProvider(new HttpClient(handler));
        Restore(provider);
        return provider;
    }

    private static void Restore(SessionTokenProvider provider)
    {
        provider.RestoreSession(
            XrpcRequestPipelineTests.CreateJwt(Did, DateTimeOffset.UtcNow.AddHours(2), "access"),
            XrpcRequestPipelineTests.CreateJwt(Did, DateTimeOffset.UtcNow.AddDays(30), "refresh"),
            Did,
            "user.example",
            Pds);
    }

    private sealed class AsyncHandler : HttpMessageHandler
    {
        private readonly Func<Task<HttpResponseMessage>> _respond;
        private int _calls;

        public AsyncHandler(Func<Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public int Calls => _calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            System.Threading.Interlocked.Increment(ref _calls);
            return _respond();
        }
    }
}
