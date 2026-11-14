namespace Aether.IpcBench;

/// <summary>Options for the end-to-end IPC latency harness.</summary>
public sealed class IpcBenchOptions
{
    public int Samples { get; private set; } = 1000;

    public int Warmup { get; private set; } = 100;

    public int PayloadBytes { get; private set; } = 1024;

    public static IpcBenchOptions Parse(string[] args)
    {
        var options = new IpcBenchOptions();
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--samples":
                    options.Samples = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--warmup":
                    options.Warmup = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--payload-bytes":
                    options.PayloadBytes = int.Parse(args[++index], System.Globalization.CultureInfo.InvariantCulture);
                    break;
            }
        }

        return options;
    }
}
