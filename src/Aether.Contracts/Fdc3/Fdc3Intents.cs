namespace Aether.Contracts.Fdc3;

/// <summary>
/// Bounded FDC3 intent subset implemented by this build. Unknown intents are rejected;
/// this is not a general FDC3 desktop-agent intent implementation.
/// </summary>
public static class Fdc3Intents
{
    public const string ViewInstrument = "ViewInstrument";
    public const string ViewAnalysis = "ViewAnalysis";
    public const string ViewChart = "ViewChart";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        ViewInstrument,
        ViewAnalysis,
        ViewChart,
    };

    /// <summary>Required context type for a supported intent (all current intents take an instrument).</summary>
    public static bool TryGetRequiredContextType(string? intent, out string contextType)
    {
        if (intent is ViewInstrument or ViewAnalysis or ViewChart)
        {
            contextType = Fdc3ContextTypes.Instrument;
            return true;
        }

        contextType = "";
        return false;
    }
}

/// <summary>FDC3-aligned context types supported by the bounded subset.</summary>
public static class Fdc3ContextTypes
{
    public const string Instrument = "fdc3.instrument";
    public const string Organization = "fdc3.organization";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        Instrument,
        Organization,
    };
}
