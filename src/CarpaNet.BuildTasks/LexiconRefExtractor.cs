// Licensed under the MIT License.

#nullable enable

using System.Text.Json;

namespace CarpaNet.BuildTasks;

/// <summary>
/// Extracts external NSID references from lexicon JSON documents.
/// Uses raw JSON walking — no model deserialization.
/// </summary>
internal static class LexiconRefExtractor
{
    /// <summary>
    /// Extracts all externally-referenced NSIDs from a lexicon JSON string.
    /// </summary>
    public static HashSet<string> ExtractReferencedNsids(string json)
    {
        var selfNsid = ExtractNsid(json);
        var result = new HashSet<string>(StringComparer.Ordinal);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("defs", out var defs) && defs.ValueKind == JsonValueKind.Object)
        {
            WalkElement(defs, selfNsid, result);
        }

        return result;
    }

    /// <summary>
    /// Extracts all externally-referenced NSIDs from a lexicon file on disk.
    /// </summary>
    public static HashSet<string> ExtractReferencedNsidsFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return ExtractReferencedNsids(json);
    }

    /// <summary>
    /// Reads the top-level "id" field from a lexicon JSON string.
    /// </summary>
    public static string? ExtractNsid(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String)
        {
            return idProp.GetString();
        }

        return null;
    }

    /// <summary>
    /// Reads the top-level "id" field from a lexicon file on disk.
    /// </summary>
    public static string? ExtractNsidFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return ExtractNsid(json);
    }

    private static void WalkElement(JsonElement element, string? selfNsid, HashSet<string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WalkObject(element, selfNsid, result);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkElement(item, selfNsid, result);
                }

                break;
        }
    }

    private static void WalkObject(JsonElement obj, string? selfNsid, HashSet<string> result)
    {
        // Check for {"type": "ref", "ref": "nsid#type"}
        if (obj.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            var typeStr = typeProp.GetString();
            if (typeStr == "ref" && obj.TryGetProperty("ref", out var refProp) && refProp.ValueKind == JsonValueKind.String)
            {
                TryAddRef(refProp.GetString(), selfNsid, result);
            }
            else if (typeStr == "union" && obj.TryGetProperty("refs", out var refsProp) && refsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in refsProp.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String)
                    {
                        TryAddRef(r.GetString(), selfNsid, result);
                    }
                }
            }
        }

        // Recurse into all properties
        foreach (var prop in obj.EnumerateObject())
        {
            WalkElement(prop.Value, selfNsid, result);
        }
    }

    private static void TryAddRef(string? refString, string? selfNsid, HashSet<string> result)
    {
        if (string.IsNullOrEmpty(refString))
            return;

        // Skip local refs (e.g. "#localType")
        if (refString.StartsWith("#", StringComparison.Ordinal))
            return;

        // Strip fragment (e.g. "app.bsky.actor.defs#profileView" → "app.bsky.actor.defs")
        var hashIndex = refString.IndexOf('#');
        var nsid = hashIndex >= 0 ? refString.Substring(0, hashIndex) : refString;

        // Skip self-references
        if (selfNsid != null && string.Equals(nsid, selfNsid, StringComparison.Ordinal))
            return;

        // Validate NSID format
        if (NsidAuthority.IsValidNsid(nsid))
        {
            result.Add(nsid);
        }
    }
}
