namespace Terraria_Wiki.Services;

public static class BackLayerCoordinator
{
    private sealed record Layer(Guid Id, int ZIndex, long Order, Action Close);

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<Guid, Layer> Layers = [];
    private static long _order;

    public static void Register(Guid id, int zIndex, Action close)
    {
        lock (SyncRoot)
        {
            Layers[id] = new Layer(id, zIndex, ++_order, close);
        }
    }

    public static void Unregister(Guid id)
    {
        lock (SyncRoot)
        {
            Layers.Remove(id);
        }
    }

    public static bool TryCloseTop()
    {
        Layer? layer;
        lock (SyncRoot)
        {
            layer = Layers.Values
                .OrderByDescending(item => item.ZIndex)
                .ThenByDescending(item => item.Order)
                .FirstOrDefault();

            if (layer is not null)
            {
                Layers.Remove(layer.Id);
            }
        }

        if (layer is null)
        {
            return false;
        }

        layer.Close();
        return true;
    }
}
