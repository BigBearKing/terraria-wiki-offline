namespace Terraria_Wiki.Services;

/// <summary>
/// 返回键的统一裁决者：网页侧的浮层（面板/抽屉/弹窗）在这里登记"关闭动作"，
/// 原生遮罩加载框这类不该被穿透的界面在这里登记"拦截"。
/// </summary>
public static class BackLayerCoordinator
{
    private sealed record Layer(Guid Id, int ZIndex, long Order, Action Close);

    private static readonly object SyncRoot = new();
    private static readonly Dictionary<Guid, Layer> Layers = [];

    /// <summary>
    /// 正在拦截返回键的原生界面（如遮罩加载框）。
    /// 用集合而不是布尔量：多个加载框可能同时存在（启动加载层 + 导入进度框），
    /// 后关闭的那个不能把先关闭的解锁掉。
    /// </summary>
    private static readonly HashSet<Guid> Blockers = [];
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

    /// <summary>
    /// 登记/注销"返回键拦截者"。
    /// 拦截期间返回键被完全吞掉：既不关闭任何浮层，也不导航、不退出应用。
    /// </summary>
    public static void SetBackBlocked(Guid id, bool blocked)
    {
        lock (SyncRoot)
        {
            if (blocked)
            {
                Blockers.Add(id);
            }
            else
            {
                Blockers.Remove(id);
            }
        }
    }

    /// <summary>是否有原生界面正在独占返回键（遮罩加载框显示中）。</summary>
    public static bool IsBackBlocked
    {
        get
        {
            lock (SyncRoot)
            {
                return Blockers.Count > 0;
            }
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
