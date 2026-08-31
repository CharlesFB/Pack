using System.Diagnostics.CodeAnalysis;

namespace MagicTavern.FrontEngine.MTPManager
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class MTPUI
    {
#if PACKAGE_DEV
        private const string UI_ROOT = "Assets/MTPManager/Editor/UI/";
#else
        private const string UI_ROOT = "Packages/" + MTPGit.SELF_PACKAGE_NAME + "/Editor/UI/";
#endif

        // Markdown 渲染代码块使用的等宽字体资源路径。
        public const string MONACO_FONT_PATH = UI_ROOT + "Fonts/Monaco.ttf";
        
        // Markdown 渲染结果对应的通用 USS 样式路径。
        public const string MARKDOWN_STYLE_PATH = UI_ROOT + "Styles/Markdown.uss";
        // 标签页 placeholder 文字通用样式路径
        public const string PROJECT_TAB_ITEM_STYLE_PATH = UI_ROOT + "Styles/GlobalStyles/TabItem.uss";
        // PackageChangelogView 样式路径。
        public const string PACKAGE_CHANGELOG_VIEW_STYLE_PATH = UI_ROOT + "Styles/PackageChangelogView.uss";
        // PackageDependenciesView 样式路径
        public const string PACKAGE_DEPENDENCIES_VIEW_STYLE_PATH = UI_ROOT + "Styles/PackageDependenciesView.uss";
        // Dependencies Tab 分区布局（Is Using / Used By）
        public const string PACKAGE_DEPENDENCIES_ITEM_VIEW_UXML_PATH =
            UI_ROOT + "Layouts/MTPackageManagerWindow/DependenciesView/DependenciesItemView.uxml";

        // Samples Tab 布局路径。
        public const string PACKAGE_SAMPLE_ITEM_VIEW_UXML_PATH =
            UI_ROOT + "Layouts/MTPackageManagerWindow/PackageSampleItemView.uxml";
        // Plugins Tab 布局路径。
        public const string PACKAGE_ZIP_ITEM_VIEW_UXML_PATH =
            UI_ROOT + "Layouts/MTPackageManagerWindow/PackageZipItemView.uxml";
        // lvProjectList 单条工程条目布局路径。
        public const string PROJECT_ITEM_VIEW_UXML_PATH =
            UI_ROOT + "Layouts/MTPackageManagerWindow/ProjectItemView.uxml";
        
        // 下拉框按钮上下切换
        public const string DROPDOWN_DOWN_ICON_PATH = UI_ROOT + "Imgs/xiala.png";
        public const string DROPDOWN_UP_ICON_PATH = UI_ROOT + "Imgs/shang.png";
        
        // 包列表item的状态标签更换
        public const string PROJECT_INSTALLED_STATUS_ICON_PATH = UI_ROOT + "Imgs/ok.png";
        public const string PROJECT_UPDATE_STATUS_ICON_PATH = UI_ROOT + "Imgs/update.png";
        
        // 包列表item选中时的背景图片
        public const string PROJECT_SELECTED_BACKGROUND_IMG_PATH = UI_ROOT + "Imgs/selected_background.png";
        // UPM 导航栏 MTP 入口图标
        public const string LOGO_SMALL_PATH = UI_ROOT + "Imgs/logo_small.png";

        // InfoView ddVersion 下拉框默认最多展示的 Tag 数量，超出的需要用户点击 "Show All" 展开。
        public const int MAX_VERSION_DROPDOWN_COUNT = 30;
    }
}
