namespace CarpaNet.OAuth.Crypto;

/// <summary>
/// EC public key JWK for DPoP thumbprint (RFC 7638).
/// Properties are declared in alphabetical order to produce
/// correct lexicographic serialization for JWK thumbprint computation.
/// </summary>
public sealed class EcJwk
{
    public string Crv { get; set; } = string.Empty;
    public string Kty { get; set; } = string.Empty;
    public string X { get; set; } = string.Empty;
    public string Y { get; set; } = string.Empty;
}

/// <summary>
/// JWT header for DPoP proofs (RFC 9449).
/// </summary>
public sealed class DPoPProofHeader
{
    public string Alg { get; set; } = string.Empty;
    public string Typ { get; set; } = string.Empty;
    public EcJwk Jwk { get; set; } = new();
}

/// <summary>
/// JWT payload for DPoP proofs (RFC 9449).
/// </summary>
public sealed class DPoPProofPayload
{
    public long Iat { get; set; }
    public string Jti { get; set; } = string.Empty;
    public string Htm { get; set; } = string.Empty;
    public string Htu { get; set; } = string.Empty;
    public string? Nonce { get; set; }
    public string? Ath { get; set; }
}

/// <summary>
/// JWT header for client assertions (RFC 7523).
/// </summary>
public sealed class JwtHeader
{
    public string Alg { get; set; } = string.Empty;
    public string Typ { get; set; } = string.Empty;
    public string? Kid { get; set; }
}

/// <summary>
/// JWT payload for client assertions (RFC 7523).
/// </summary>
public sealed class ClientAssertionPayload
{
    public string Iss { get; set; } = string.Empty;
    public string Sub { get; set; } = string.Empty;
    public string Aud { get; set; } = string.Empty;
    public string Jti { get; set; } = string.Empty;
    public long Iat { get; set; }
    public long Exp { get; set; }
}
