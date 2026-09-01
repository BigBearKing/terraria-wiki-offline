namespace Terraria_Wiki.Models;

public sealed class AppTaskOptions
{
    public bool ShowSuccess { get; init; }
    public bool ShowError { get; init; } = true;
    public bool ShowBusy { get; init; } = true;
    public bool Rethrow { get; init; }
    public Func<Exception, Task>? OnError { get; init; }
    public Func<Task>? OnCancelled { get; init; }
}
