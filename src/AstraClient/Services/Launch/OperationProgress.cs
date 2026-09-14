namespace AstraClient.Services.Launch;

public enum LaunchStage
{
    Idle,
    ResolvingVersion,
    DownloadingClient,
    DownloadingLibraries,
    DownloadingAssets,
    ExtractingNatives,
    ResolvingJava,
    BuildingArgs,
    Launching,
    Running,
    Completed,
    Failed,
    Cancelled
}

public class OperationProgress
{
    public LaunchStage Stage   { get; set; }
    public string Message     { get; set; } = "";
    public int    Current     { get; set; }
    public int    Total       { get; set; }
    public double Percent     { get; set; }
    public long   BytesPerSec { get; set; }
    public bool   IsIndeterminate => Total <= 0;

    public static OperationProgress Of(LaunchStage stage, string message, int current = 0, int total = 0, double percent = 0)
        => new() { Stage = stage, Message = message, Current = current, Total = total, Percent = percent };

    public static OperationProgress Indeterminate(LaunchStage stage, string message)
        => new() { Stage = stage, Message = message, Total = -1, Percent = -1 };
}
