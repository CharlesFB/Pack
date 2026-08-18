using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class MTPGit
    {
        public const string DEFAULT_URL = "https://gitlab.mte.io/";
        public const string DEFAULT_TOKEN = "MySP-fys9zb6XQAjxD6Q";
        public const string DEFAULT_GROUP = "502";
        public static IReadOnlyList<string> DefaultBlacklist { get; } = new List<string>
        {
            "693"
        };

        public const string GIT_PROJECT_INFO_LIST_PATH = "upm-list.json";           //远端工程列表信息存放路径
        public const string GIT_PACKAGE_INFO_CACHE_PATH = "upm-cache.json";         //远端工程全部包信息存放路径
        public const int SOURCE_PROJECT_ID = 647;                                   //远端工程ID（管理工程id）
    }
}