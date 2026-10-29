namespace Terraria_Wiki.Services;

/// <summary>
/// 「整个下载任务该停下了」——目前只有断网一种情况。
///
/// 为什么需要单独的异常类型：<see cref="BatchTaskScheduler{T}"/> 对普通异常的处理是
/// "当前条目记进失败列表，然后继续下一条"。断网时那样会在一两秒内把整队条目全刷进失败列表，
/// 用户看到的是"失败项几百个"，而不是"任务暂停了"。这个异常会让调度器放行到顶层，
/// 由 DataService 收口：任务保持 Paused、待续清单文件原样保留。
/// </summary>
public sealed class DownloadStoppedException : Exception
{
    public DownloadStoppedException() : base("下载已停止：网络不可用。")
    {
    }
}
