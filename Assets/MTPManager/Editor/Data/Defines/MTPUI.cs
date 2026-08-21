using System.Diagnostics.CodeAnalysis;

namespace MagicTavern.FrontEngine.MTPManager
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class MTPUI
    {
        // Markdown 渲染代码块使用的等宽字体资源路径。
        public const string MONACO_FONT_PATH = "Assets/Editor/UI/Fonts/Monaco.ttf";
        
        // Markdown 渲染结果对应的通用 USS 样式路径。
        public const string MARKDOWN_STYLE_PATH = "Assets/Editor/UI/Styles/Markdown.uss";
        // 标签页 placeholder 文字通用样式路径
        public const string PROJECT_TAB_ITEM_STYLE_PATH = "Assets/Editor/UI/Styles/GlobalStyles/TabItem.uss";
        // PackageChangelogView 样式路径。
        public const string PACKAGE_CHANGELOG_VIEW_STYLE_PATH = "Assets/Editor/UI/Styles/PackageChangelogView.uss";
        // PackageDependenciesView 样式路径
        public const string PACKAGE_DEPENDENCIES_VIEW_STYLE_PATH = "Assets/Editor/UI/Styles/PackageDependenciesView.uss";

        // Samples Tab 布局路径。
        public const string PACKAGE_SAMPLE_ITEM_VIEW_UXML_PATH =
            "Assets/Editor/UI/Layouts/MTPackageManagerWindow/PackageSampleItemView.uxml";
        // Plugins Tab 布局路径。
        public const string PACKAGE_ZIP_ITEM_VIEW_UXML_PATH =
            "Assets/Editor/UI/Layouts/MTPackageManagerWindow/PackageZipItemView.uxml";
        // lvProjectList 单条工程条目布局路径。
        public const string PROJECT_ITEM_VIEW_UXML_PATH =
            "Assets/Editor/UI/Layouts/MTPackageManagerWindow/ProjectItemView.uxml";
        
        // 下拉框按钮上下切换
        public const string DROPDOWN_DOWN_ICON_PATH = "Assets/Editor/UI/Imgs/xiala.png";
        public const string DROPDOWN_UP_ICON_PATH = "Assets/Editor/UI/Imgs/shang.png";
        
        // 包列表item的状态标签更换
        public const string PROJECT_INSTALLED_STATUS_ICON_PATH = "Assets/Editor/UI/Imgs/ok.png";
        public const string PROJECT_UPDATE_STATUS_ICON_PATH = "Assets/Editor/UI/Imgs/update.png";
        
        // 包列表item选中时的背景图片
        public const string PROJECT_SELECTED_BACKGROUND_IMG_PATH = "Assets/Editor/UI/Imgs/selected_background.png";

        // InfoView ddVersion 下拉框默认最多展示的 Tag 数量，超出的需要用户点击 "Show All" 展开。
        public const int MAX_VERSION_DROPDOWN_COUNT = 30;
    }
}