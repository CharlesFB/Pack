using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// Dependencies Tab：四列手动网格。
    /// 每一列是独立的纵向容器，每一行必须向四列各塞一个等高 Label；
    /// 列宽由 Yoga 按该列最宽子项撑开（列 flex-grow/shrink 均为 0）。
    /// </summary>
    public class PackageDependenciesView : TabElementBase
    {
        private const string Placeholder = " ";

        private readonly VisualElement _lvTitleList;
        private readonly VisualElement _lvPackageNames;
        private readonly VisualElement _lvVersions;
        private readonly VisualElement _lvInstallationStatus;

        private static readonly StyleColor NotInstalledColor = new StyleColor(new Color32(160, 0, 0, 255));
        private static readonly StyleColor InstalledColor = new StyleColor(new Color32(0, 170, 20, 255));
        private static readonly StyleColor UpdateColor = new StyleColor(new Color32(230, 130, 0, 255));
        private static readonly StyleColor NormalColor = new StyleColor(new Color32(230, 230, 230, 255));

        private static IMTPackageInfoService MTPService => ServiceLocator.Get<IMTPackageInfoService>();
        private static IPackageJsonService PackageJsonService => ServiceLocator.Get<IPackageJsonService>();

        public PackageDependenciesView()
        {
            style.flexGrow = 1;
            AddStyleSheetIfExists(MTPUI.PACKAGE_DEPENDENCIES_VIEW_STYLE_PATH);
            AddStyleSheetIfExists(MTPUI.PROJECT_TAB_ITEM_STYLE_PATH);

            var container = new ScrollView();
            container.AddToClassList("package-dep-view__container");

            var grid = new VisualElement();
            grid.AddToClassList("package-dep-view__sub-container");

            _lvTitleList = CreateColumn("package-dep-view__title-list");
            _lvPackageNames = CreateColumn("package-dep-view__package-names");
            _lvVersions = CreateColumn("package-dep-view__min-versions");
            _lvInstallationStatus = CreateColumn("package-dep-view__installation-status");

            grid.Add(_lvTitleList);
            grid.Add(_lvPackageNames);
            grid.Add(_lvVersions);
            grid.Add(_lvInstallationStatus);
            container.Add(grid);
            Add(container);
        }

        public void BindData(PackageJson tagInfo)
        {
            _lvTitleList.Clear();
            _lvPackageNames.Clear();
            _lvVersions.Clear();
            _lvInstallationStatus.Clear();

            BindIsUsing(tagInfo);
            BindUsedBy(tagInfo);
        }

        private void BindIsUsing(PackageJson tagInfo)
        {
            var infoList = PackageJsonService.GetAllIsUsing(tagInfo);
            if (infoList == null || infoList.Count == 0)
            {
                AddGridRow("Is Using", isSectionTitle: true, "(None)", Placeholder, Placeholder, NormalColor);
                return;
            }

            bool isFirst = true;
            foreach (var info in infoList)
            {
                if (info == null || string.IsNullOrEmpty(info.name))
                {
                    continue;
                }

                ResolveInstallStatus(info, out var statusText, out var statusColor);
                AddGridRow(
                    isFirst ? "Is Using" : Placeholder,
                    isSectionTitle: isFirst,
                    info.name,
                    info.version ?? Placeholder,
                    statusText,
                    statusColor);
                isFirst = false;
            }

            if (isFirst)
            {
                AddGridRow("Is Using", isSectionTitle: true, "(None)", Placeholder, Placeholder, NormalColor);
            }
        }

        private void BindUsedBy(PackageJson tagInfo)
        {
            if (string.IsNullOrEmpty(tagInfo?.name))
            {
                AddGridRow("Used By", isSectionTitle: true, "(None)", Placeholder, Placeholder, NormalColor);
                return;
            }

            var infoList = MTPService.GetAllUsedBy(tagInfo.name);
            if (infoList == null || infoList.Count == 0)
            {
                AddGridRow("Used By", isSectionTitle: true, "(None)", Placeholder, Placeholder, NormalColor);
                return;
            }

            bool isFirst = true;
            foreach (var info in infoList)
            {
                if (info == null || string.IsNullOrEmpty(info.name))
                {
                    continue;
                }

                AddGridRow(
                    isFirst ? "Used By" : Placeholder,
                    isSectionTitle: isFirst,
                    info.name,
                    info.version ?? Placeholder,
                    Placeholder,
                    NormalColor);
                isFirst = false;
            }

            if (isFirst)
            {
                AddGridRow("Used By", isSectionTitle: true, "(None)", Placeholder, Placeholder, NormalColor);
            }
        }

        /// <summary>
        /// 一行 = 四列各一个 Label。缺格会对不齐，空白也必须用占位 Label。
        /// </summary>
        private void AddGridRow(
            string title,
            bool isSectionTitle,
            string packageName,
            string version,
            string status,
            StyleColor statusColor)
        {
            var titleClass = isSectionTitle
                ? "package-dep-view__title-font"
                : "package-dep-view__title-placeholder";

            _lvTitleList.Add(Cell(title, titleClass, NormalColor));
            _lvPackageNames.Add(Cell(packageName, "package-dep-view__common-font", NormalColor));
            _lvVersions.Add(Cell(version, "package-dep-view__version-font", NormalColor));
            _lvInstallationStatus.Add(Cell(status, "package-dep-view__status-font", statusColor));
        }

        private void ResolveInstallStatus(PackageJson info, out string statusText, out StyleColor statusColor)
        {
            var installedPackage = MTPService.GetPackage(info.name);
            if (installedPackage == null)
            {
                statusText = "(not installed)";
                statusColor = NotInstalledColor;
                return;
            }

            var installedJson = PackageJsonService.Get(
                installedPackage.ProjectId.ToString(), installedPackage.Tag);
            var installedVersion = !string.IsNullOrEmpty(installedJson?.version)
                ? installedJson.version
                : VersionUtils.GetVersionSegment(installedPackage.Tag);

            bool needUpdate = false;
            if (Version.TryParse(installedVersion, out var installedVer)
                && Version.TryParse(info.version, out var requiredVer))
            {
                needUpdate = installedVer < requiredVer;
            }

            statusText = string.IsNullOrEmpty(installedVersion)
                ? "(installed)"
                : $"({installedVersion} installed)";
            statusColor = needUpdate ? UpdateColor : InstalledColor;
        }

        private Label Cell(string text, string className, StyleColor color)
        {
            var lb = LabelWithClass(string.IsNullOrEmpty(text) ? Placeholder : text, className, color);
            lb.AddToClassList("package-dep-view__cell");
            return lb;
        }

        private static VisualElement CreateColumn(string className)
        {
            var column = new VisualElement();
            column.AddToClassList(className);
            return column;
        }
    }
}
