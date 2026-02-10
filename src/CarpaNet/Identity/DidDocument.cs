using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CarpaNet.Identity;

/// <summary>
/// Represents a DID Document as returned by DID resolution.
/// Contains the handle, signing key, and PDS service endpoint for an account.
/// </summary>
public sealed class DidDocument
{
    /// <summary>
    /// The DID identifier (e.g., "did:plc:z72i7hdynmk6r22z27h6tvur").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Alternative identifiers for this DID, including the handle as "at://{handle}".
    /// </summary>
    [JsonPropertyName("alsoKnownAs")]
    public List<string> AlsoKnownAs { get; set; } = new();

    /// <summary>
    /// Verification methods (public keys) for this DID.
    /// </summary>
    [JsonPropertyName("verificationMethod")]
    public List<VerificationMethod> VerificationMethod { get; set; } = new();

    /// <summary>
    /// Services associated with this DID, including the PDS endpoint.
    /// </summary>
    [JsonPropertyName("service")]
    public List<DidService> Service { get; set; } = new();

    /// <summary>
    /// Gets the primary handle from alsoKnownAs (first at:// URI).
    /// </summary>
    public string? Handle
    {
        get
        {
            foreach (var aka in AlsoKnownAs)
            {
                if (aka.StartsWith("at://", StringComparison.OrdinalIgnoreCase))
                {
                    return aka.Substring(5); // Remove "at://" prefix
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the atproto signing key from verificationMethod.
    /// </summary>
    public VerificationMethod? AtprotoSigningKey
    {
        get
        {
            foreach (var vm in VerificationMethod)
            {
                // Current format: id ends with #atproto
                if (vm.Id?.EndsWith("#atproto", StringComparison.Ordinal) == true)
                {
                    return vm;
                }
                // Legacy format: id is just "#atproto"
                if (vm.Id == "#atproto")
                {
                    return vm;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the PDS service endpoint.
    /// </summary>
    public DidService? PdsService
    {
        get
        {
            foreach (var svc in Service)
            {
                if (svc.Type == "AtprotoPersonalDataServer")
                {
                    return svc;
                }
                // Also check id for #atproto_pds
                if (svc.Id?.EndsWith("#atproto_pds", StringComparison.Ordinal) == true)
                {
                    return svc;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Gets the PDS endpoint URL.
    /// </summary>
    public string? PdsEndpoint => PdsService?.ServiceEndpoint;

    /// <summary>
    /// Gets the public key in multibase format.
    /// </summary>
    public string? PublicKeyMultibase => AtprotoSigningKey?.PublicKeyMultibase;

    /// <summary>
    /// Parses a DID document from JSON.
    /// </summary>
    public static DidDocument FromJson(string json)
    {
#if NET8_0_OR_GREATER
        return JsonSerializer.Deserialize(json, DidDocumentJsonContext.Default.DidDocument)
            ?? throw new InvalidOperationException("Failed to parse DID document");
#else
        return JsonSerializer.Deserialize<DidDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse DID document");
#endif
    }

    /// <summary>
    /// Serializes the DID document to JSON.
    /// </summary>
    public string ToJson()
    {
#if NET8_0_OR_GREATER
        return JsonSerializer.Serialize(this, DidDocumentJsonContext.Default.DidDocument);
#else
        return JsonSerializer.Serialize(this, JsonOptions);
#endif
    }

#if !NET8_0_OR_GREATER
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
#endif
}

/// <summary>
/// Represents a verification method (public key) in a DID document.
/// </summary>
public sealed class VerificationMethod
{
    /// <summary>
    /// The ID of this verification method (e.g., "did:plc:...#atproto").
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// The type of verification method.
    /// Current: "Multikey"
    /// Legacy: "EcdsaSecp256r1VerificationKey2019" (P-256) or "EcdsaSecp256k1VerificationKey2019" (K-256)
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The controller of this key (should match the DID).
    /// </summary>
    [JsonPropertyName("controller")]
    public string? Controller { get; set; }

    /// <summary>
    /// The public key in multibase format.
    /// </summary>
    [JsonPropertyName("publicKeyMultibase")]
    public string? PublicKeyMultibase { get; set; }

    /// <summary>
    /// Returns true if this is the current Multikey format.
    /// </summary>
    public bool IsMultikey => Type == "Multikey";

    /// <summary>
    /// Returns true if this is a legacy key format.
    /// </summary>
    public bool IsLegacy => Type == "EcdsaSecp256r1VerificationKey2019" ||
                            Type == "EcdsaSecp256k1VerificationKey2019";

    /// <summary>
    /// Returns the curve type for legacy keys.
    /// </summary>
    public string? LegacyCurve => Type switch
    {
        "EcdsaSecp256r1VerificationKey2019" => "P-256",
        "EcdsaSecp256k1VerificationKey2019" => "secp256k1",
        _ => null
    };
}

/// <summary>
/// Represents a service in a DID document.
/// </summary>
public sealed class DidService
{
    /// <summary>
    /// The ID of this service (e.g., "#atproto_pds").
    /// </summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// The type of service (e.g., "AtprotoPersonalDataServer").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The service endpoint URL.
    /// </summary>
    [JsonPropertyName("serviceEndpoint")]
    public string? ServiceEndpoint { get; set; }
}
