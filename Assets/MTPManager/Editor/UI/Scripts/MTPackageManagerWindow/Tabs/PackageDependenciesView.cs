using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    public class PackageDependenciesView : TabElementBase
    {
        private readonly ScrollView _container;
        private readonly VisualElement _subContainer;
        
        private VisualElement _lvTitleList;
        private VisualElement _lvPackageNames;
        private VisualElement _lvMinVersions;
        private VisualElement _lvInstallationStatus;
        
        private PackageJson _tagInfo;
        private IMTPackageInfoService MTPService => ServiceLocator.Get<IMTPackageInfoService>();

        private readonly static StyleColor notInstalledColor = new StyleColor(new Color(160, 0, 0));
        private readonly static StyleColor installedColor = new StyleColor(new Color(0, 170, 20));
        private readonly static StyleColor normalColor = new StyleColor(new Color(230, 230, 230));
        
        PackageDependenciesView()
        {
            AddStyleSheetIfExists(MTPUI.PACKAGE_DEPENDENCIES_VIEW_STYLE_PATH);
            _container = new ScrollView();
            _container.AddToClassList("package-dep-view__container");
            _subContainer = new VisualElement();
            _subContainer.AddToClassList("package-dep-view__sub-container");
            InitializeView();
            Add(_container);
        }

        private void InitializeView()
        {
            _lvTitleList = new VisualElement();
            _lvTitleList.AddToClassList("package-dep-view__title-list");
            _lvPackageNames = new VisualElement();
            _lvPackageNames.AddToClassList("package-dep-view__package-names");
            _lvMinVersions = new VisualElement();
            _lvMinVersions.AddToClassList("package-dep-view__package-versions");
            _lvInstallationStatus = new VisualElement();
            _lvInstallationStatus.AddToClassList("package-dep-view__installation-status");
            _subContainer.Add(_lvTitleList);
            _subContainer.Add(_lvPackageNames);
            _subContainer.Add(_lvMinVersions);
            _subContainer.Add(_lvInstallationStatus);
            _container.Add(_subContainer);
        }

        public void BindData(PackageJson tagInfo)
        {
            _lvTitleList.Clear();
            _lvPackageNames.Clear();
            _lvMinVersions.Clear();
            _lvInstallationStatus.Clear();
            
            _tagInfo = tagInfo;
            
            var lbIsUsing = new Label("Is Using");
            lbIsUsing.AddToClassList("package-dep-view__title-font");
            _lvTitleList.Add(lbIsUsing);
            _lvTitleList.Add(lbIsUsing);
            BuildIsUsing();
            
            var lbUsingBy = new Label("Used By");
            lbUsingBy.AddToClassList("package-dep-view__title-font");
            _lvTitleList.Add(lbUsingBy);
            BuildUsedBy();
        }

        private void BuildIsUsing()
        {
            
        }

        private void BuildUsedBy()
        {
            var infoList = MTPService.GetAllUsedBy(_tagInfo.name);
            if (infoList == null || infoList.Count <= 0)
            {
                // _lvPackageNames.Add
            }
        }
    }
}