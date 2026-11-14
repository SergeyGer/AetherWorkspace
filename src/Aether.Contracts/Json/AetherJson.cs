using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Contracts.Errors;

namespace Aether.Contracts.Json;

/// <summary>
/// Centralised JSON settings. Contract documents are parsed in "strict" mode:
/// unknown members are rejected so that an untrusted producer cannot smuggle
/// unsupported directives into a typed contract.
/// </summary>
public static class AetherJson
{
    public const int MaxDocumentDepth = 32;

    public static JsonSerializerOptions Strict { get; } = CreateStrict();

    public static JsonSerializerOptions Indented { get; } = CreateIndented();

    private static JsonSerializerOptions CreateStrict()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            MaxDepth = MaxDocumentDepth,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }

    private static JsonSerializerOptions CreateIndented()
    {
        var options = new JsonSerializerOptions(Strict) { WriteIndented = true };
        return options;
    }

    /// <summary>
    /// Strictly deserialises a contract document. Any parse failure (including
    /// unknown members) is converted into a structured contract error.
    /// </summary>
    public static T DeserializeStrict<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "Contract document is empty.");
        }

        try
        {
            var value = JsonSerializer.Deserialize<T>(json, Strict);
            if (value is null)
            {
                throw new AetherContractException(AetherErrorCodes.PlanInvalidJson, "Contract document resolved to null.");
            }

            return value;
        }
        catch (JsonException exception)
        {
            throw new AetherContractException(
                AetherErrorCodes.PlanInvalidJson,
                $"Contract document is not valid for {typeof(T).Name}: {exception.Message}");
        }
    }

    public static bool TryDeserializeStrict<T>(string json, [NotNullWhen(true)] out T? value, out string? errorCode, out string? message)
    {
        try
        {
            value = DeserializeStrict<T>(json)!;
            errorCode = null;
            message = null;
            return true;
        }
        catch (AetherContractException exception)
        {
            value = default;
            errorCode = exception.Code;
            message = exception.Message;
            return false;
        }
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Strict);

    public static string SerializeIndented<T>(T value) => JsonSerializer.Serialize(value, Indented);
}
