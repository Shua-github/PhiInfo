using System;
using System.IO;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using global.PhiInfo.HttpServer.Type;

namespace PhiInfo.Android;

public class AndroidHttpServer(string apkPath, Stream cldbStream, string version) : HttpServer(apkPath, cldbStream)
{
    private const string Tag = "PhiInfoHttpServer";

    protected override void Log(string msg)
    {
        global::Android.Util.Log.Info(Tag, msg);
    }

    protected override AppInfo GetAppInfo()
    {
        return new AppInfo(version, "Android");
    }
}

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeSpecialUse)]
public class HttpServerService : Service
{
    private const string Tag = "PhiInfoHttpService";
    private const int NotificationId = 41669;
    private const string ChannelId = "phiinfo_channel";
    private const string TargetPkg = "com.PigeonGames.Phigros";

    private AndroidHttpServer? _server;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var notification = new Notification.Builder(this, ChannelId)
            .SetContentTitle("PhiInfo Server")
            .SetContentText("服务正在监听 http://127.0.0.1:41669/")
            .SetSmallIcon(global::Android.Resource.Drawable.SymDefAppIcon)
            .SetOngoing(true)
            .Build();

        if (OperatingSystem.IsAndroidVersionAtLeast(34))
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else
            StartForeground(NotificationId, notification);

        StartServer();

        return StartCommandResult.Sticky;
    }

    private void StartServer()
    {
        try
        {
            if (_server != null) return;

            var appInfo = PackageManager?.GetApplicationInfo(TargetPkg, 0);
            var apkPath = appInfo?.SourceDir ?? throw new Exception("apk路径为null");
            if (PackageName is null) throw new Exception("包名为null");

            var versionName = PackageManager?.GetPackageInfo(PackageName, 0)?.VersionName ?? "Unknown";

            var cldbStream = Assets?.Open("classdata.tpk") ?? throw new Exception("cldb资源找不到");

            _server = new AndroidHttpServer(apkPath, cldbStream, versionName);

            _ = _server.Start(41669, "127.0.0.1");

            Log.Info(Tag, "HTTP Server started successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Failed to start HTTP server: {ex.Message}");
        }
    }

    public override void OnDestroy()
    {
        _server?.Dispose();
        _server = null;
        Log.Info(Tag, "HTTP Server stopped and disposed.");
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return null;
    }

    private void CreateNotificationChannel()
    {
        var channel = new NotificationChannel(ChannelId, "HTTP Server 状态", NotificationImportance.Low)
        {
            Description = "显示 PhiInfo 本地 HTTP 服务器的运行状态"
        };
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(channel);
    }
}