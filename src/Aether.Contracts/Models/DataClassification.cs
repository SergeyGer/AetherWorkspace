namespace Aether.Contracts;

/// <summary>
/// Data classification labels. Higher rank = more sensitive. <see cref="Unknown"/>
/// is deliberately ranked highest: missing or ambiguous classification must
/// behave like the most restrictive label (fail-closed).
/// </summary>
public enum DataClassification
{
    Public = 0,
    Internal = 1,
    Restricted = 2,
    Unknown = 3,
}

public static class DataClassificationExtensions
{
    /// <summary>Sensitivity rank; UNKNOWN outranks every explicit label.</summary>
    public static int SensitivityRank(this DataClassification value) => value switch
    {
        DataClassification.Public => 0,
        DataClassification.Internal => 1,
        DataClassification.Restricted => 2,
        DataClassification.Unknown => 3,
        _ => 3,
    };

    /// <summary>Returns the more sensitive of two labels; undefined values fail closed to UNKNOWN.</summary>
    public static DataClassification Max(this DataClassification left, DataClassification right)
    {
        if (!Enum.IsDefined(left) || !Enum.IsDefined(right))
        {
            return DataClassification.Unknown;
        }

        return left.SensitivityRank() >= right.SensitivityRank() ? left : right;
    }

    /// <summary>Canonical wire name (for example in policy files and audit events).</summary>
    public static string ToWireName(this DataClassification value) => value switch
    {
        DataClassification.Public => "PUBLIC",
        DataClassification.Internal => "INTERNAL",
        DataClassification.Restricted => "RESTRICTED",
        DataClassification.Unknown => "UNKNOWN",
        _ => "UNKNOWN",
    };

    /// <summary>
    /// Parses a wire label. Unrecognised, missing or malformed values resolve to
    /// <see cref="DataClassification.Unknown"/> and return false.
    /// </summary>
    public static bool TryParseWire(string? value, out DataClassification classification)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "PUBLIC":
                classification = DataClassification.Public;
                return true;
            case "INTERNAL":
                classification = DataClassification.Internal;
                return true;
            case "RESTRICTED":
                classification = DataClassification.Restricted;
                return true;
            case "UNKNOWN":
                classification = DataClassification.Unknown;
                return true;
            default:
                classification = DataClassification.Unknown;
                return false;
        }
    }

    /// <summary>Parses a wire label, failing closed to UNKNOWN.</summary>
    public static DataClassification ParseOrUnknown(string? value) =>
        TryParseWire(value, out var classification) ? classification : DataClassification.Unknown;
}
