using CarpaNet.OAuth;
using Xunit;

namespace CarpaNet.UnitTests.OAuth;

/// <summary>
/// Rules ported from atproto/packages/oauth/oauth-types (buildAtprotoLoopbackClientId,
/// parseAtprotoLoopbackClientId, oauthLoopbackClientRedirectUriSchema).
/// </summary>
public class LoopbackClientIdTests
{
    [Fact]
    public void CreateLoopbackClientId_UsesLocalhostWithRedirectUri()
    {
        var clientId = OAuthClientConfig.CreateLoopbackClientId(8080);

        Assert.Equal("http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%3A8080%2Fcallback", clientId);
        Assert.Equal("http://127.0.0.1:8080/callback", OAuthClientConfig.CreateLoopbackRedirectUri(8080));

        var parsed = AtprotoLoopbackClientId.TryParse(clientId, out var error);
        Assert.Null(error);
        Assert.Equal("atproto", parsed!.Scope);
        Assert.Equal(new[] { "http://127.0.0.1:8080/callback" }, parsed.RedirectUris);
    }

    [Fact]
    public void CreateLoopbackClientId_IncludesNonDefaultScope()
    {
        var clientId = OAuthClientConfig.CreateLoopbackClientId(1234, "atproto transition:generic");

        Assert.Equal("http://localhost?scope=atproto+transition%3Ageneric&redirect_uri=http%3A%2F%2F127.0.0.1%3A1234%2Fcallback", clientId);
        var parsed = AtprotoLoopbackClientId.TryParse(clientId, out var error);
        Assert.Null(error);
        Assert.Equal("atproto transition:generic", parsed!.Scope);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void CreateLoopbackClientId_RejectsInvalidPort(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OAuthClientConfig.CreateLoopbackClientId(port));
    }

    [Fact]
    public void CreateLoopbackClientId_RejectsScopeWithoutAtproto()
    {
        Assert.Throws<ArgumentException>(() => OAuthClientConfig.CreateLoopbackClientId(8080, "transition:generic"));
    }

    [Fact]
    public void CreateLoopback_ConfiguresClientIdRedirectAndScopeConsistently()
    {
        var config = OAuthClientConfig.CreateLoopback(8080, "atproto transition:generic");

        Assert.Equal("http://127.0.0.1:8080/callback", config.RedirectUri);
        Assert.Equal("atproto transition:generic", config.Scope);
        var parsed = AtprotoLoopbackClientId.TryParse(config.ClientId, out _);
        Assert.Equal(config.Scope, parsed!.Scope);
        Assert.Equal(new[] { config.RedirectUri }, parsed.RedirectUris);
    }

    [Theory]
    [InlineData(null, null, "http://localhost")]
    [InlineData("atproto", null, "http://localhost")]
    [InlineData("atproto transition:generic", null, "http://localhost?scope=atproto+transition%3Ageneric")]
    [InlineData(null, "defaults", "http://localhost")]
    [InlineData(null, "http://127.0.0.1:1234/cb", "http://localhost?redirect_uri=http%3A%2F%2F127.0.0.1%3A1234%2Fcb")]
    [InlineData(null, "http://[::1]:1234/cb", "http://localhost?redirect_uri=http%3A%2F%2F%5B%3A%3A1%5D%3A1234%2Fcb")]
    public void Build(string? scope, string? redirect, string expected)
    {
        IEnumerable<string>? redirects = redirect switch
        {
            null => null,
            "defaults" => new[] { "http://[::1]/", "http://127.0.0.1/" },
            _ => new[] { redirect },
        };

        Assert.Equal(expected, AtprotoLoopbackClientId.Build(scope, redirects));
    }

    [Theory]
    [InlineData("transition:generic", null)]
    [InlineData("atproto  transition:generic", null)]
    [InlineData(null, "")]
    [InlineData(null, "http://localhost:8080/callback")]
    [InlineData(null, "https://127.0.0.1/callback")]
    [InlineData(null, "http://example.com/callback")]
    public void Build_RejectsInvalidInput(string? scope, string? redirect)
    {
        IEnumerable<string>? redirects = redirect switch
        {
            null => null,
            "" => Array.Empty<string>(),
            _ => new[] { redirect },
        };

        Assert.Throws<ArgumentException>(() => AtprotoLoopbackClientId.Build(scope, redirects));
    }

    [Theory]
    [InlineData("http://localhost", null)]
    [InlineData("http://localhost/", null)]
    [InlineData("http://localhost?scope=atproto", null)]
    [InlineData("http://localhost/?redirect_uri=http%3A%2F%2F127.0.0.1%3A8080%2Fcallback&redirect_uri=http%3A%2F%2F%5B%3A%3A1%5D%2F", null)]
    [InlineData("http://127.0.0.1", "Value must start with \"http://localhost\"")]
    [InlineData("http://127.0.0.1:8080/?", "Value must start with \"http://localhost\"")]
    [InlineData("https://localhost", "Value must start with \"http://localhost\"")]
    [InlineData("http://localhost#x", "Value must not contain a hash component")]
    [InlineData("http://localhost:8080", "Value must not contain a path component")]
    [InlineData("http://localhost/path", "Value must not contain a path component")]
    [InlineData("http://localhost?foo=bar", "Unexpected query parameter \"foo\"")]
    [InlineData("http://localhost?state=x", "Unexpected query parameter \"state\"")]
    [InlineData("http://localhost?scope=atproto&scope=atproto", "Duplicate \"scope\" query parameter")]
    [InlineData("http://localhost?scope=atproto%20%20x", "Invalid \"scope\" query parameter: Invalid OAuth scope")]
    [InlineData("http://localhost?scope=transition%3Ageneric", "ATProto Loopback ClientID must include \"atproto\" scope")]
    [InlineData("http://localhost?redirect_uri=http%3A%2F%2Flocalhost%2Fcb", "Invalid \"redirect_uri\" query parameter: Use of \"localhost\" hostname is not allowed (RFC 8252), use a loopback IP such as \"127.0.0.1\" instead")]
    [InlineData("http://localhost?redirect_uri=https%3A%2F%2F127.0.0.1%2Fcb", "Invalid \"redirect_uri\" query parameter: URL must use the \"http:\" protocol")]
    [InlineData("http://localhost?redirect_uri=http%3A%2F%2Fexample.com%2Fcb", "Invalid \"redirect_uri\" query parameter: URL must use \"localhost\", \"127.0.0.1\" or \"[::1]\" as hostname")]
    public void TryParse(string clientId, string? expectedError)
    {
        var result = AtprotoLoopbackClientId.TryParse(clientId, out var error);

        Assert.Equal(expectedError, error);
        Assert.Equal(expectedError is null, result is not null);
        Assert.Equal(expectedError is null, AtprotoLoopbackClientId.IsLoopbackClientId(clientId));
    }

    [Fact]
    public void TryParse_FillsInDefaults()
    {
        var result = AtprotoLoopbackClientId.TryParse("http://localhost", out _);

        Assert.Equal("atproto", result!.Scope);
        Assert.Equal(new[] { "http://127.0.0.1/", "http://[::1]/" }, result.RedirectUris);
    }
}
