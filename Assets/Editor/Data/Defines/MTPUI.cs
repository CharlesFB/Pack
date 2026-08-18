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

        // PackageChangelogView 自身容器/滚动区/空状态样式路径。
        public const string PACKAGE_CHANGELOG_VIEW_STYLE_PATH = "Assets/Editor/UI/Styles/PackageChangelogView.uss";

        // Samples Tab 布局路径。
        public const string PACKAGE_SAMPLE_ITEM_VIEW_UXML_PATH =
            "Assets/Editor/UI/Layouts/MTPackageManagerWindow/PackageSampleItemView.uxml";
        // Plugins Tab 布局路径。
        public const string PACKAGE_ZIP_ITEM_VIEW_UXML_PATH =
            "Assets/Editor/UI/Layouts/MTPackageManagerWindow/PackageZipItemView.uxml";
        
        // 下拉框按钮上下切换
        public const string DROPDOWN_DOWN_ICON_PATH = "Assets/Editor/UI/Imgs/xiala.png";
        public const string DROPDOWN_UP_ICON_PATH = "Assets/Editor/UI/Imgs/shang.png";
    }
}