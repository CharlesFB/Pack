namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class MTPTime
    {
        public const double PROJECT_INFO_LIST_TTL = 600;    // seconds，记录工程列表缓存最小刷新时间间隔
        public const double PACKAGE_INFO_CACHE_TTL = 600;   // seconds，记录包列表缓存最小刷新时间间隔
    }
}