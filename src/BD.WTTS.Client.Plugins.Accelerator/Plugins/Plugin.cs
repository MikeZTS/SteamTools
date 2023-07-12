using Avalonia.Controls;
using BD.WTTS.UI.Views.Pages;

namespace BD.WTTS.Plugins;

#if (WINDOWS || MACCATALYST || MACOS || LINUX) && !(IOS || ANDROID)
[CompositionExport(typeof(IPlugin))]
#endif
public sealed class Plugin : PluginBase<Plugin>, IPlugin
{
    const string moduleName = AssemblyInfo.Accelerator;

    public override Guid Id => Guid.Parse(AssemblyInfo.AcceleratorId);

    public sealed override string Name => Strings.CommunityFix;

    public sealed override string UniqueEnglishName => moduleName;

    public sealed override string Description => "提供一些游戏相关网站服务的加速及脚本注入功能。";

    public sealed override string Author => "Steam++ 官方";

    public sealed override string? Icon => "avares://BD.WTTS.Client.Plugins.Accelerator/UI/Assets/accelerator.ico";

    public override IEnumerable<TabItemViewModel>? GetMenuTabItems()
    {
        yield return new MenuTabItemViewModel()
        {
            ResourceKeyOrName = nameof(Strings.CommunityFix),
            PageType = typeof(MainFramePage),
            IsResourceGet = true,
            //IconKey = "SpeedHigh",
            IconKey = Icon,
        };

        //yield return new MenuTabItemViewModel()
        //{
        //    ResourceKeyOrName = nameof(Strings.ScriptConfig),
        //    PageType = typeof(ScriptPage),
        //    IsResourceGet = true,
        //    IconKey = "DuplexPortraitOneSided",
        //};
    }

    readonly TaskCompletionSource<IReverseProxyService> reverseProxyService = new();

    public override void ConfigureDemandServices(IServiceCollection services, Startup startup)
    {
        services.TryAddScriptManager();

        if (startup.HasHttpProxy)
        {
#if !DISABLE_ASPNET_CORE && (WINDOWS || MACCATALYST || MACOS || LINUX) && !(IOS || ANDROID)
            // 添加反向代理服务（主进程插件）
            services.AddSingleton(_ => reverseProxyService.Task.GetAwaiter().GetResult());
#endif
        }

        if (startup.HasServerApiClient)
        {
            // 添加仓储服务
            services.AddSingleton<IScriptRepository, ScriptRepository>();
        }
    }

    public override void ConfigureRequiredServices(IServiceCollection services, Startup startup)
    {
#if (WINDOWS || MACCATALYST || MACOS || LINUX) && !(IOS || ANDROID)
        services.AddSingleton<IProxyService>(_ => ProxyService.Current);
#endif
    }

    public override async ValueTask OnInitializeAsync()
    {
        var ipc = IPCMainProcessService.Instance;

        // 启动加速模块子进程
        await ipc.AddDaemonWithStartSubProcessAsync(moduleName, async ipc =>
        {
            var subProcessPath = SubProcessPath;
            var p = await ipc.StartSubProcessAsync(subProcessPath.ThrowIsNull(),
                isAdministrator: true);
            return p;
        });
    }

    public override async ValueTask OnPeerConnected(bool isReconnected)
    {
        if (!isReconnected)
        {
            var ipc = IPCMainProcessService.Instance;

            // 从子进程中获取 IPC 远程服务
            try
            {
                var reverseProxyService = await ipc.GetServiceAsync<IReverseProxyService>(moduleName);
                this.reverseProxyService.TrySetResult(reverseProxyService.ThrowIsNull());
            }
            catch (Exception ex)
            {
                reverseProxyService.TrySetException(ex);
            }
#if DEBUG
            //try
            //{
            //    var debugStringIPC = $"Pid: {Environment.ProcessId}, Exe: {Environment.ProcessPath}, Asm: {Assembly.GetAssembly(GetType())?.FullName}{Environment.NewLine}{reverseProxyService.Task.GetAwaiter().GetResult().GetDebugString()}";
            //    Console.WriteLine($"DebugString/IReverseProxyService: {debugStringIPC}");
            //    var debugStringIPC2 = reverseProxyService.Task.GetAwaiter().GetResult().GetDebugString2().GetAwaiter().GetResult();
            //    Console.WriteLine($"DebugString/IReverseProxyService: {debugStringIPC2}");
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine(ex);
            //}
#endif

            //if (ResourceService.IsChineseSimplified)
            //{
            await ProxyService.Current.InitializeAsync();
            //}
        }
    }

    public override void OnAddAutoMapper(IMapperConfigurationExpression cfg)
    {
        cfg.AddProfile<AcceleratorAutoMapperProfile>();
    }

    public override async ValueTask OnExit()
    {
        try
        {
            var reverseProxyService = Ioc.Get_Nullable<IReverseProxyService>();
            if (reverseProxyService != null)
            {
                await reverseProxyService.StopProxyAsync();
            }
            ProxyService.OnExitRestoreHosts();
        }
        catch (ObjectDisposedException)
        {

        }
    }

    string? subProcessPath;

    /// <summary>
    /// 获取子进程文件所在路径
    /// </summary>
    string? SubProcessPath
    {
        get
        {
            if (subProcessPath == null)
            {
                try
                {
                    subProcessPath = Assembly.GetExecutingAssembly().Location;
                    subProcessPath = Path.GetDirectoryName(subProcessPath);
                    subProcessPath.ThrowIsNull();

                    const string fileName = $"Steam++.{moduleName}";
                    var subProcessFileName = OperatingSystem.IsWindows() ? $"{fileName}{FileEx.EXE}" : fileName;
                    subProcessPath = Path.Combine(subProcessPath, subProcessFileName);

#if DEBUG // DEBUG 模式遍历项目查找模块
                    if (!File.Exists(subProcessPath))
                    {
                        subProcessPath = Path.Combine(ProjectUtils.ProjPath, "src", "BD.WTTS.Client.Plugins.Accelerator.ReverseProxy", "bin", "Debug", ProjectUtils.tfm, subProcessFileName);
                    }
#endif
                    return subProcessPath;
                }
                catch
                {
                    subProcessPath = string.Empty;
                }
            }

            return subProcessPath;
        }
    }

    /// <summary>
    /// 子进程是否存在
    /// </summary>
    /// <returns></returns>
    bool SubProcessExists()
    {
        var subProcessPath = SubProcessPath;
        return !string.IsNullOrWhiteSpace(subProcessPath) && File.Exists(subProcessPath);
    }

    public override bool ExplicitHasValue()
    {
        // 网络加速模块仅在简体中文中加载
        return ResourceService.IsChineseSimplified && SubProcessExists();
    }

    public override IEnumerable<(Action<IServiceCollection>? @delegate, bool isInvalid, string name)>? GetConfiguration(bool directoryExists)
    {
        yield return GetConfiguration<ProxySettings_>(directoryExists);
    }
}
