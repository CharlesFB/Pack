namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 统一注册服务初始化单例，只包含一个Initialize静态方法
    /// </summary>
    public sealed class ServiceManager
    {
        private static ServiceManager s_instance;
        public static ServiceManager Instance => s_instance ??= new ServiceManager();
        ServiceManager()
        {
        }

        public static void Initialize()
        {
            // 服务注册前(如果这是域重载后执行的)先清理掉之前注册的服务
            ServiceLocator.Clear();
            
            // 添加注册服务接口与实现
            // Sample : ServiceLocator.Register<IBusinessService>(new BusinessManager);
            ServiceLocator.Register<IGitLabService>(new GitLabService()); // 很多服务需要 Git 服务提供拉取业务，先注册
            ServiceLocator.Register<IPackageJsonService>(new PackageJsonService());
            ServiceLocator.Register<IProjectInfoService>(new ProjectInfoService());
            ServiceLocator.Register<IManifestJsonService>(new ManifestJsonService());
            ServiceLocator.Register<IPackagesLockService>(new PackagesLockService());
            ServiceLocator.Register<IPluginAssetService>(new PluginAssetService());
            ServiceLocator.Register<IMTPackageInfoService>(new MTPackageInfoService());
            ServiceLocator.Register<IRootPackageService>(new RootPackageService());
            ServiceLocator.Register<ILocalPackageService>(new LocalPackageService());
            ServiceLocator.Register<IPluginPackageService>(new PluginPackageService());
            ServiceLocator.Register<IPackageInstallerService>(new PackageInstallerService());
            ServiceLocator.Register<IPackageSyncService>(new PackageSyncService());
        }
    }
}
