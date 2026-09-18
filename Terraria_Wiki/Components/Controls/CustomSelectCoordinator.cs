namespace Terraria_Wiki.Components.Controls;

internal static class CustomSelectCoordinator
{
    public static event Action<Guid>? SelectOpened;

    public static void NotifyOpened(Guid instanceId) => SelectOpened?.Invoke(instanceId);

}
