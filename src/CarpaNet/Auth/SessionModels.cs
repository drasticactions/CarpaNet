using System.Text.Json.Serialization;

namespace CarpaNet.Auth;

/// <summary>
/// Request body for com.atproto.server.createSession.
/// </summary>
public sealed class CreateSessionRequest
{
    /// <summary>
    /// Gets or sets the user identifier (handle or DID).
    /// </summary>
    [JsonPropertyName("identifier")]
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the password (or App Password).
    /// </summary>
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional auth factor token (for 2FA).
    /// </summary>
    [JsonPropertyName("authFactorToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AuthFactorToken { get; set; }
}

/// <summary>
/// Response from com.atproto.server.createSession and com.atproto.server.refreshSession.
/// </summary>
public sealed class SessionResponse
{
    /// <summary>
    /// Gets or sets the access JWT.
    /// </summary>
    [JsonPropertyName("accessJwt")]
    public string AccessJwt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the refresh JWT.
    /// </summary>
    [JsonPropertyName("refreshJwt")]
    public string RefreshJwt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's DID.
    /// </summary>
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's handle.
    /// </summary>
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's display name (if available).
    /// </summary>
    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user's email (if available).
    /// </summary>
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets whether the email is confirmed.
    /// </summary>
    [JsonPropertyName("emailConfirmed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmailConfirmed { get; set; }

    /// <summary>
    /// Gets or sets whether an auth factor is required.
    /// </summary>
    [JsonPropertyName("emailAuthFactor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmailAuthFactor { get; set; }

    /// <summary>
    /// Gets or sets the DID document (optional).
    /// </summary>
    [JsonPropertyName("didDoc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? DidDoc { get; set; }

    /// <summary>
    /// Gets or sets whether the account is active.
    /// </summary>
    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Active { get; set; }

    /// <summary>
    /// Gets or sets the status if the account is not active.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }
}

/// <summary>
/// Response from com.atproto.server.getSession.
/// </summary>
public sealed class GetSessionResponse
{
    /// <summary>
    /// Gets or sets the user's DID.
    /// </summary>
    [JsonPropertyName("did")]
    public string Did { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's handle.
    /// </summary>
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's email (if available).
    /// </summary>
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets whether the email is confirmed.
    /// </summary>
    [JsonPropertyName("emailConfirmed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmailConfirmed { get; set; }

    /// <summary>
    /// Gets or sets whether an auth factor is required.
    /// </summary>
    [JsonPropertyName("emailAuthFactor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EmailAuthFactor { get; set; }

    /// <summary>
    /// Gets or sets the DID document (optional).
    /// </summary>
    [JsonPropertyName("didDoc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? DidDoc { get; set; }

    /// <summary>
    /// Gets or sets whether the account is active.
    /// </summary>
    [JsonPropertyName("active")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Active { get; set; }

    /// <summary>
    /// Gets or sets the status if the account is not active.
    /// </summary>
    [JsonPropertyName("status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Status { get; set; }
}
