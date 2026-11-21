using Foundation;
using Terraria_Wiki.Services;

namespace Terraria_Wiki;

public static class IosFilePicker
{
    private static NSUrl? _pendingUrl;

    public static void HandleExternalOpenUrl(NSUrl? url)
    {
        if (url is null || !url.IsFileUrl)
            return;

        var path = url.Path;
        if (string.IsNullOrEmpty(path) ||
            !path.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingUrl = url;
        _ = ImportWhenReadyAsync();
    }

    public static Func<Task<Stream>>? ConsumePendingStreamOpener()
    {
        var url = _pendingUrl;
        _pendingUrl = null;
        if (url is null)
            return null;

        return () => Task.FromResult<Stream>(new SecurityScopedFileStream(url));
    }

    private static async Task ImportWhenReadyAsync()
    {
        try
        {
            if (Microsoft.Maui.Controls.Application.Current is App app)
                await app.InitializationTask;

            for (int i = 0; i < 50; i++)
            {
                if ((Microsoft.Maui.Controls.Application.Current?.Windows.Count ?? 0) > 0)
                    break;
                await Task.Delay(100);
            }

            var services = IPlatformApplication.Current?.Services;
            if (services?.GetService(typeof(PackageService)) is not PackageService service)
                return;

            await service.ImportDataAsync();
        }
        catch (Exception ex)
        {
            App.LogManager?.Error("处理 iOS 外部打开的导入包失败", ex);
        }
    }

    private sealed class SecurityScopedFileStream : FileStream
    {
        private readonly NSUrl _url;
        private readonly bool _hasScope;

        public SecurityScopedFileStream(NSUrl url)
            : base(OpenScopedPath(url, out var hasScope), FileMode.Open, FileAccess.Read, FileShare.Read)
        {
            _url = url;
            _hasScope = hasScope;
        }

        private static string OpenScopedPath(NSUrl url, out bool hasScope)
        {
            hasScope = url.StartAccessingSecurityScopedResource();
            return url.Path ?? throw new FileNotFoundException("无法读取外部导入文件路径。");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && _hasScope)
                _url.StopAccessingSecurityScopedResource();
        }
    }
}
