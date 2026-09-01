namespace Terraria_Wiki.Models;

public sealed class ActiveTaskInfo
{
    public required AppTask Task { get; init; }
    public DateTime StartedTime { get; init; }
    public bool CanCancel { get; init; }
    public bool CanPause => Task.CanPause;
}

public enum AppTaskAccess
{
    Shared,
    Exclusive
}
