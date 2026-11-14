using Aether.Contracts.Json;

namespace Aether.Security.Policy;

/// <summary>Loads and validates a trusted local policy document (strict JSON).</summary>
public static class PolicyLoader
{
    public static PolicyDocument LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static PolicyDocument LoadFromJson(string json)
    {
        var document = AetherJson.DeserializeStrict<PolicyDocument>(json);
        return document.EnsureValid();
    }
}
