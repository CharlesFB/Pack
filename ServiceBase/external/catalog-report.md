# 647 最高发布版插件扫描结果

报告生成时间（UTC）：2026-09-15T05:57:59.481619+00:00。
目录固定 commit：`cc6362d665487700b4dac5d40caad0416b74a76b`。

## 覆盖与入库结果

- 91 个目录条目，63 个 GitLab 仓库。87 个条目有非 debug 数字版本发布标签，4 个只有 debug 标签。
- 最新发布版完整文件树发现 20 处压缩包引用，涉及 13 个包条目；去重后 19 份制品（16 个 unitypackage、3 个 ZIP），共 36,941,970 字节。
- 仓库总计保留 20 份制品（包括 1 份旧版测试 ZIP），共 36,942,235 字节；18 个插件 ID（历史扫描统计）；当前目录分组见 groups.json。
- 87 个发布条目合并同包名/commit 后对应 86 个当前映射；仓库共保留 94 个包版本映射。
- mtunitgif 在两个仓库条目中指向相同包名/commit，合并映射并保留两个来源。
- 原手工清单 17 个文件全部命中；DOTween 扩展名拼写按实际文件规范化。

## 选择规则

按现有 MTP 版本约定独立实现：读取实时 tags，排除包含 debug 的标签，比较最后一段数字版本；包含 develop/original 等已打版本的命名分支。同数字版本优先标准标签。不同包共用仓库时分别选择，固定 commit 后完整分页扫描整个文件树。不按本地安装状态或黑名单缩减范围。

这与 GitLab Releases 页面排序不同，也不是只选标准命名的主分支版本。比如 mtspine 选择 mtspine-original-3.8.0，mtaddressable 选择 mtaddressable-upgrade/2022.3.62-1.21.23（比较末段 1.21.23）。

## 手工清单之外的当前文件

- `Runtime/Plugins/Animations.unitypackage`：mtuiframework。
- `Runtime/Plugins/Demigiant.unitypackage`：mtuiframework。

较旧的 testpackage3-1.0.4 中还发现 README的副本.md.zip，已留作历史构建；最高发布版 testpackage3-jump-1.0.5 无压缩包，因此该测试 ZIP 不属于当前清单。

## 分组与变体

EDM4U 两版本保留不同哈希；1.2.175 在 nativeutility 和 adjustsdk 共用同一制品。Odin Inspector 与 ConfigTool 变体分开；DOTween、DOTween Pro、Demigiant 包也不互换。

plugin.json 的未核实字段保持空值；源文件名、包 tag、commit、完整源路径保存在扫描/映射中。ZIP 安装位置和必需/可选关系保持 unconfirmed；当前所有映射 execution_ready=false。

## 制品审查项

- Facebook Unity SDK 有 19 条 Packages/com.unity.ugui 路径。
- Google Play Games 有 1 条资源缺少 .meta。
- 本轮原样下载，不重打包、不导入 Unity；Git blob、SHA-256 和归档索引校验均完成。

## 只有 debug 标签的条目

- taskinvoker（仓库 680）：`taskinvoker-debug`
- testpackage5（仓库 693）：`testpackage5-debug`
- bsdiff（仓库 784）：`bsdiff-debug`
- srcheattool（仓库 819）：`srcheattool-feature/SRDebugger-debug`

## 每个发布条目的映射

### mtspine · mtspine-original-3.8.0

仓库 603；包 `com.magictavern.spine`；commit `1ea2490c342f03080c92bf719325ba711fec155b`。

完整文件树未发现 ZIP / unitypackage。

### mtpdevtoolkit · mtpdevtoolkit-1.2.8

仓库 614；包 `com.magictavern.mtpdevtoolkit`；commit `1df14309c783c1104ee5b3840171fa90140e23b1`。

完整文件树未发现 ZIP / unitypackage。

### mtpmanager · mtpmanager-1.3.19

仓库 614；包 `com.magictavern.mtpmanager`；commit `868dacc9706cb9e3a2154dda4d23b6080b1125a3`。

完整文件树未发现 ZIP / unitypackage。

### base · base-1.0.25

仓库 618；包 `com.magictavern.frontengine.base`；commit `0b8e660268d1e7d5a4ad69df99bc3d68a453c5b1`。

完整文件树未发现 ZIP / unitypackage。

### eventmanager · eventmanager-1.0.14

仓库 627；包 `com.magictavern.eventmanager`；commit `c40cf45d664555adb1bf32dd5f4779d1dbf3afa6`。

完整文件树未发现 ZIP / unitypackage。

### lecommon · lecommon-1.0.10

仓库 636；包 `com.magictavern.lecommon`；commit `c2cc5f7b767ecbcdf9572097c8cde4fd1ec555b9`。

完整文件树未发现 ZIP / unitypackage。

### guiassetimporter · guiassetimporter-1.0.16

仓库 646；包 `com.magictavern.guiassetimporter`；commit `70b6e35f66345f25caba4260941d7173d413c890`。

- `Editor/PluginsPackage/Odin Inspector 3.1.10.0.unitypackage`；Git blob `12295185084427ae91573722ae5fa4cc7a68397c`。

### nativeutility · nativeutility-1.3.27

仓库 680；包 `com.magictavern.nativeutility`；commit `8ec63868f8d3b415ebf3d57adc9f7971ff78814d`。

- `Editor/PluginsPackage/external-dependency-manager-1.2.175.unitypackage`；Git blob `c6162d8a0ae9fc9502b8e9742a1253a63871ad5e`。

### track · track-1.1.18

仓库 682；包 `com.magictavern.track`；commit `70230c089aade9376d1c9cbd9afff9905f30f48b`。

完整文件树未发现 ZIP / unitypackage。

### tracklib · tracklib-1.0.8

仓库 682；包 `com.magictavern.tracklib`；commit `c05b401b34cb7d7fff124d67046f29660052cd06`。

完整文件树未发现 ZIP / unitypackage。

### cibuildpipeline · cibuildpipeline-1.2.22

仓库 689；包 `com.magictavern.cibuildpipeline`；commit `5bda12cc57da1cac3080254720df5f8d21fe00ac`。

- `Editor/PluginsPackage/BuildConfig.zip`；Git blob `9aa692f175b2874e5544be74ad43cdb3722ace06`。

### filedispatcher · filedispatcher-develop-0.0.6

仓库 692；包 `com.magictavern.filedispatcher`；commit `75c7caa35f41b5dfd4a23bb4a89455033851e846`。

完整文件树未发现 ZIP / unitypackage。

### filereceiver · filereceiver-develop-0.0.4

仓库 692；包 `com.magictavern.filereceiver`；commit `0c9dc7656c5ac176396d14f980654553723d5246`。

完整文件树未发现 ZIP / unitypackage。

### mtmatch · mtmatch-develop-1.0.10

仓库 692；包 `com.magictavern.mtmatch`；commit `645ff8c4096d40446e271f4cabe90126aa7e21fa`。

完整文件树未发现 ZIP / unitypackage。

### mtmatcheditor · mtmatcheditor-develop-1.0.5

仓库 692；包 `com.magictavern.matcheditor`；commit `d6276ebcbefc632fe69bab2e253b5ad3569418d6`。

完整文件树未发现 ZIP / unitypackage。

### testpackage · testpackage-1.0.12

仓库 693；包 `com.magictavern.testpackage`；commit `2d353a386047320902754fa2f6a70d14513438d4`。

完整文件树未发现 ZIP / unitypackage。

### testpackage10 · testpackage10-1.0.0

仓库 693；包 `com.magictavern.testpackage10`；commit `27d8b0fc08746c2a64c6fd9b0fc0c1b273ab787f`。

完整文件树未发现 ZIP / unitypackage。

### testpackage11 · testpackage11-1.0.0

仓库 693；包 `com.magictavern.testpackage11`；commit `1fe13069360ea7e81b72f28887be6a6c899e8faa`。

完整文件树未发现 ZIP / unitypackage。

### testpackage12 · testpackage12-1.0.2

仓库 693；包 `com.magictavern.testpackage12`；commit `d6d28bea7eaa0aff076543b721f473966a7f3da2`。

完整文件树未发现 ZIP / unitypackage。

### testpackage13 · testpackage13-1.0.1

仓库 693；包 `com.magictavern.testpackage13`；commit `cc3c69aa38ef11a6059ef6b849eae8c6f2f0ae02`。

完整文件树未发现 ZIP / unitypackage。

### testpackage2 · testpackage2-1.0.0

仓库 693；包 `com.magictavern.testpackage2`；commit `7d00f7f376fd7c6b9c01bd9457f4008e7eda9e2e`。

完整文件树未发现 ZIP / unitypackage。

### testpackage3 · testpackage3-jump-1.0.5

仓库 693；包 `com.magictavern.testpackage3`；commit `ff29de9a78c3245001c691d3d4e3f5e1b330be62`。

完整文件树未发现 ZIP / unitypackage。

### testpackage4 · testpackage4-1.0.1

仓库 693；包 `com.magictavern.testpackage4`；commit `9797980bb9d9d1b4c07571722ead0a9024742714`。

完整文件树未发现 ZIP / unitypackage。

### testpackage6 · testpackage6-1.0.0

仓库 693；包 `com.magictavern.TestPackage6`；commit `8955b8201cc14d62946bfee29aaf9ae1372a7e3d`。

完整文件树未发现 ZIP / unitypackage。

### testpackage7 · testpackage7-1.0.0

仓库 693；包 `com.magictavern.testpackage7`；commit `1a6c95b04a999b63c93e9393211b8b7ea6212645`。

完整文件树未发现 ZIP / unitypackage。

### testpackage8 · testpackage8-1.0.1

仓库 693；包 `com.magictavern.testpackage8`；commit `1c01ff8da05d71d99d3f1bcea42a7508d09f3d10`。

完整文件树未发现 ZIP / unitypackage。

### testpackage9 · testpackage9-1.0.0

仓库 693；包 `com.magictavern.testpackage9`；commit `d458c1323195390f2319ad576e980ccb50164e12`。

完整文件树未发现 ZIP / unitypackage。

### adssdk · adssdk-1.0.27

仓库 707；包 `com.magictavern.sdk.ads`；commit `9a653b2e93a880add660a28af7bb893f335f8cad`。

- `Editor/PluginsPackage/AppLovin-MAX-Unity-Plugin-8.0.1-Android-13.0.1-iOS-13.0.1.unitypackage`；Git blob `5c430acd62f9c8d23613fd9bcb82ea00298fafdd`。

### adjustsdk · adjustsdk-1.0.1

仓库 708；包 `com.magictavern.sdk.adjustsdk`；commit `3f056081d35a9fa2c0216facc143e712a2becc73`。

- `Editor/PluginsPackage/Adjust_v5.0.6.unitypackage`；Git blob `15f4b9e5e172170cd2f39d2e7d929b7fd1b6a1e9`。
- `Editor/PluginsPackage/external-dependency-manager-1.2.175.unitypackage`；Git blob `c6162d8a0ae9fc9502b8e9742a1253a63871ad5e`。

### audiomanager · audiomanager-1.1.11

仓库 726；包 `com.magictavern.audiomanager`；commit `6e98204a243748a5db0af7389d34b0a1c1ce4a2b`。

完整文件树未发现 ZIP / unitypackage。

### purchasingsdk · purchasingsdk-1.8.0

仓库 749；包 `com.magictavern.purchasingsdk`；commit `197b077ca085326bd718e598324686347437b46a`。

完整文件树未发现 ZIP / unitypackage。

### configtool · configtool-1.2.25

仓库 753；包 `com.magictavern.configtool`；commit `7e1c0f3aa0b873069985a1088968dab26943dc7f`。

完整文件树未发现 ZIP / unitypackage。

### devicemanager · devicemanager-1.0.3

仓库 759；包 `com.magictavern.devicemanager`；commit `0905e0d963246f0652e08eda7a70468484133f48`。

完整文件树未发现 ZIP / unitypackage。

### assetsmanager · assetsmanager-1.1.21

仓库 760；包 `com.magictavern.assetsmanager`；commit `009347d53a630ae907058e983e7b6754c7de40a7`。

完整文件树未发现 ZIP / unitypackage。

### mtaccount · mtaccount-1.0.21

仓库 763；包 `com.magictavern.mtaccount`；commit `2075101bbae14615073a048d4b9d1b416f8388ef`。

完整文件树未发现 ZIP / unitypackage。

### mtnetwork · mtnetwork-1.0.19

仓库 763；包 `com.magictavern.mtnetwork`；commit `97ed7784a38f29563b2f711b709d0be006e53044`。

完整文件树未发现 ZIP / unitypackage。

### authoritymanager · authoritymanager-0.0.4

仓库 770；包 `com.magictavern.authoritymanager`；commit `5d4f676e17003deec153c1a88c751080d3013f77`。

完整文件树未发现 ZIP / unitypackage。

### protobuf · protobuf-3.21.8

仓库 781；包 `com.magictavern.thirdparty.protobuf`；commit `510dbdea76f3a8cfb49519d016a2c4e2efb1be6b`。

完整文件树未发现 ZIP / unitypackage。

### systembuffer · systembuffer-1.0.0

仓库 781；包 `com.magictavern.thirdparty.systembuffer`；commit `75dac2faa7301b3fdfe2ed580945ac6196215eb9`。

完整文件树未发现 ZIP / unitypackage。

### mtaddressable · mtaddressable-upgrade/2022.3.62-1.21.23

仓库 784；包 `com.magictavern.mtaddressable`；commit `562ea47abd2a4b316660884f1adc73c6e9560767`。

完整文件树未发现 ZIP / unitypackage。

### appleauth · appleauth-1.4.3

仓库 791；包 `com.magictavern.thirdparty.applesigninunity`；commit `d5f04b397d0c3e62fce667676d009170c2293f35`。

完整文件树未发现 ZIP / unitypackage。

### socialaccountsdk · socialaccountsdk-1.0.26

仓库 791；包 `com.magictavern.sdk.socialaccount`；commit `6150ebe13c67182651df83dcfe3ecc8bd30047a3`。

- `Editor/PluginsPackage/facebook-unity-sdk-16.0.2.unitypackage`；Git blob `d923cf0e8c54ff542963dc4e064b8a99140124a5`。
- `Editor/PluginsPackage/google-signin-plugin-1.0.4.12.unitypackage`；Git blob `92b3222f73ee1e8ec8e86185599910c99a96bb16`。
- `Editor/PluginsPackage/googleplay-games.unitypackage`；Git blob `e3267a7f55896d303ad24beafc6b288f2f5da7ec`。

### mtsocket · mtsocket-develop-0.0.1

仓库 792；包 `com.magictavern.socket`；commit `442221aba685182eb85f890901e7d78786b48280`。

完整文件树未发现 ZIP / unitypackage。

### zstring · zstring-2.4.5

仓库 793；包 `com.magictavern.zstring`；commit `c3f8edd8beff8b25abfdae201bfedc3c815b14c9`。

完整文件树未发现 ZIP / unitypackage。

### unitask · unitask-2.3.1

仓库 794；包 `com.magictavern.unitask`；commit `1fa809f1cd602c4691f8fcb077a3af402ad57d17`。

完整文件树未发现 ZIP / unitypackage。

### mtlitjson · mtlitjson-develop-1.0.1

仓库 797；包 `com.magictavern.litjson`；commit `f4299fdc3052943c8683fa44d0f9e10a24707728`。

完整文件树未发现 ZIP / unitypackage。

### cheattool · cheattool-2.2.14

仓库 819；包 `com.magictavern.cheattool`；commit `ee75963e50bfe55ae74f3a2d24f3bb046a982709`。

完整文件树未发现 ZIP / unitypackage。

### sharpziplib · sharpziplib-1.3.3

仓库 820；包 `com.magictavern.thirdparty.sharpziplib`；commit `d7b2c076f0b1642b081a37b0039fa56dc317d705`。

完整文件树未发现 ZIP / unitypackage。

### notificationmanager · notificationmanager-1.0.13

仓库 841；包 `com.magictavern.notificationmanager`；commit `5e87c9b89506a965aecca0d463af90e720958db8`。

完整文件树未发现 ZIP / unitypackage。

### mtuiframework · mtuiframework-2.5.16

仓库 843；包 `com.magictavern.mtuiframework`；commit `23ed4e5f983bd2daf3f5bbe8339b65433bc5bb85`。

- `Runtime/Plugins/Animations.unitypackage`；Git blob `3f41520bb7865addac986178ba93bd657339b92c`。
- `Runtime/Plugins/Demigiant.unitypackage`；Git blob `ca8895431b09adae2958fd1bc9f14a23266c1273`。

### hotfixdownloader · hotfixdownloader-1.1.40

仓库 853；包 `com.magictavern.hotfixdownloader`；commit `bfbdf2626e28f426f6f076246d29410459aac477`。

完整文件树未发现 ZIP / unitypackage。

### hybridclr · hybridclr-8.6.0

仓库 853；包 `com.magictavern.hybridclr`；commit `5011fd7b26ae874dc1ada1f2b913afd544268c5e`。

完整文件树未发现 ZIP / unitypackage。

### hybridclrhelper · hybridclrhelper-1.0.1

仓库 853；包 `com.magictavern.hybridclrhelper`；commit `11a6857135048950bc7e9feecb34f52b9009415c`。

完整文件树未发现 ZIP / unitypackage。

### h5gamemanager · h5gamemanager-1.0.7

仓库 855；包 `com.magictavern.h5game`；commit `8744955f7f321b3f321f02d1056c3f855c2f2040`。

完整文件树未发现 ZIP / unitypackage。

### psd2ugui · psd2ugui-0.0.11

仓库 869；包 `com.magictavern.psd2ugui`；commit `2036defc200422bddb4d4da602f29fc795274226`。

完整文件树未发现 ZIP / unitypackage。

### mtu8 · mtu8-0.1.30

仓库 872；包 `com.magictavern.mtu8`；commit `05187d46fe260dba8df83b8dc872a39fcf772635`。

完整文件树未发现 ZIP / unitypackage。

### langtool · langtool-1.4.12

仓库 917；包 `com.magictavern.langtool`；commit `764b87e05bb3ff6871e02e26cf52da953db161a7`。

- `Editor/PluginsPackage/Odin Inspector_ConfigTool.unitypackage`；Git blob `05aadde00eef39fff8da2866182b6cd42ed218ba`。

### modulesystem · modulesystem-1.0.2

仓库 921；包 `com.magictavern.modulesystem`；commit `57c569df871457c3f1063260b267322785f60ba0`。

完整文件树未发现 ZIP / unitypackage。

### scriban · scriban-5.5.2

仓库 921；包 `com.magictavern.thirdparty.scriban`；commit `bee7dc182d5a46381a54e5c71c002f52670509fd`。

完整文件树未发现 ZIP / unitypackage。

### langtooltmpsupport · langtooltmpsupport-1.2.2

仓库 1102；包 `com.magictavern.langtool_tmpsupport`；commit `1eddefb5090cc52b9c86ed303bb5eec67a5e6684`。

完整文件树未发现 ZIP / unitypackage。

### rtltmpro · rtltmpro-3.4.3

仓库 1102；包 `com.magictavern.rtltmpro`；commit `0fc7667fee0276c243cef1ad1c2edad0a5525398`。

完整文件树未发现 ZIP / unitypackage。

### bezieranimationeditor · bezieranimationeditor-1.0.5

仓库 1153；包 `com.magictavern.bezieranimationeditor`；commit `3cedfeee48a16cb05234d01b17a98bfdedc0f4f5`。

完整文件树未发现 ZIP / unitypackage。

### nativecrashtracker · nativecrashtracker-1.0.4

仓库 1167；包 `com.magictavern.nativecrashtracker`；commit `da5f8efda983b7f7f9f544cc282c717ff0e1fb59`。

完整文件树未发现 ZIP / unitypackage。

### helpshiftsdk · helpshiftsdk-1.0.0

仓库 1170；包 `com.magictavern.helpshiftsdk`；commit `cf2371c16bab65a09b56b5d207fe17fb2f78639c`。

- `Editor/PluginsPackage/helpshiftX-plugin-unity-v10.2.1/helpshiftX-plugin-unity-v10.2.1.unitypackage`；Git blob `fc0f64d33b5496007710fda897c2cde478f695d2`。
- `Editor/PluginsPackage/helpshiftX-plugin-unity-v10.2.1/unity-jar-resolver/external-dependency-manager-1.2.170.unitypackage`；Git blob `964b91da4cfce8cd64b2d2156d51824a864bf486`。

### vibrationutils · vibrationutils-1.0.15

仓库 1175；包 `com.magictavern.vibrationutils`；commit `dbf1d1fb2c409eba3090bdd67ed97aa6bfe409b4`。

完整文件树未发现 ZIP / unitypackage。

### userdata · userdata-1.0.4

仓库 1258；包 `com.magictavern.userdata`；commit `6b1db972457939f99b882390984117dadee18063`。

完整文件树未发现 ZIP / unitypackage。

### minigame · minigame-0.6.11

仓库 1262；包 `com.magictavern.minigame`；commit `6e84efb0909d0ec717a448748f75036046b54cd6`。

- `Editor/PluginsPackage/DOTween.unitypackage`；Git blob `a6a02d8f8376ba08308b4124eaabf5054c7cb7c0`。

### mttimeline · mttimeline-1.0.0

仓库 1400；包 `com.magictavern.timeline`；commit `48ecc657cfb5255a0fb023dd1f1c61c84b7dc8f4`。

- `Editor/PluginsPackage/Demigiant.zip`；Git blob `d06ddb40c40fd0b1b680d4c65d4120cae55874bd`。
- `Editor/PluginsPackage/com.esotericsoftware.spine.timeline-4.1-2023-05-16.zip`；Git blob `ed37c53a12ca08867c47815d9236143c5251d389`。
- `Editor/PluginsPackage/spine-unity-4.1-2023-05-16.unitypackage`；Git blob `e5dd5d63ee4590207d6fbdb699e74a5cb05afa51`。

### amusesunityclient · amusesunityclient-1.1.8

仓库 1535；包 `com.magictavern.amusesunityclient`；commit `fa334e25c46926c6fc398fe7b2f17ef875028b54`。

完整文件树未发现 ZIP / unitypackage。

### editorbase · editorbase-1.0.3

仓库 1587；包 `com.magictavern.editorbase`；commit `51da06c9c1d33b4ebcadeb3a00668be802074788`。

完整文件树未发现 ZIP / unitypackage。

### assetfindusages · assetfindusages-develop-1.0.1

仓库 1904；包 `com.magictavern.assetfindusages`；commit `1539a6868c3272a32d340310c84c62e9af45f67f`。

完整文件树未发现 ZIP / unitypackage。

### pocosdk · pocosdk-1.0.1

仓库 1958；包 `com.magictavern.sdk.poco`；commit `f7f2f4c5bf3bf478856aaf29fd1e45c105a7ae11`。

完整文件树未发现 ZIP / unitypackage。

### anubissdk · anubissdk-1.0.1

仓库 1959；包 `com.magictavern.sdk.anubis`；commit `888048bfc21e78a8d60e6a75d5768ccc8f595eae`。

完整文件树未发现 ZIP / unitypackage。

### updatemanager · updatemanager-1.0.3

仓库 1966；包 `com.magictavern.updatemanager`；commit `b601230fea3bfe7b45593264e6f0685cba04fddc`。

完整文件树未发现 ZIP / unitypackage。

### mtunigif · mtunigif-1.0.2

仓库 2107；包 `com.magictavern.mtunigif`；commit `af70c15128c95484883fec28aa5533a9bf070eb3`。

完整文件树未发现 ZIP / unitypackage。

### mtunitgif · mtunitgif-1.0.0

仓库 2107；包 `com.magictavern.mtunitgif`；commit `21f9fb94bb36be2da937ff2ddb0805ac101450e5`。

完整文件树未发现 ZIP / unitypackage。

### dotsmatch3 · dotsmatch3-0.0.9

仓库 2251；包 `com.magictavern.dotsmatch3`；commit `403000e08f6913283b597776da4fb9b541101708`。

- `Editor/PluginsPackage/DotweenPro.unitypackage`；Git blob `767a66442190f10ed537c2a9751151c5c446227c`。

### mtunitgif · mtunitgif-1.0.0

仓库 2272；包 `com.magictavern.mtunitgif`；commit `21f9fb94bb36be2da937ff2ddb0805ac101450e5`。

完整文件树未发现 ZIP / unitypackage。

### levelautogenerator · levelautogenerator-1.0.16

仓库 2572；包 `com.magictavern.levelautogenerator`；commit `c1c4dec74c7b90e286730e510019d35b2855e7a9`。

完整文件树未发现 ZIP / unitypackage。

### mttypereigstersourcegenerator · mttypereigstersourcegenerator-1.0.0

仓库 2638；包 `com.magictavern.mttypereigstersourcegenerator`；commit `121065ebd9ecf861a0c76c4f60486155448cceee`。

完整文件树未发现 ZIP / unitypackage。

### vibration · vibration-1.0.18

仓库 2758；包 `com.magictavern.vibration`；commit `e98b1f3d333655ab3358fd0706b20df93d275b91`。

- `Editor/PluginsPackage/NiceVibrations-v-3-9-0.unitypackage`；Git blob `1f602d1ee8a938fba47c0db836f2264653a4ebfe`。

### smartvibration · smartvibration-1.0.2

仓库 2916；包 `com.magictavern.smartvibration`；commit `4965f94b446a055481e489b0618e433727ed18c3`。

完整文件树未发现 ZIP / unitypackage。

### activity · activity-1.0.3

仓库 2928；包 `com.magictavern.activity`；commit `508ce1ab317d60d24e7ff8e174adc2272099e7cf`。

完整文件树未发现 ZIP / unitypackage。

### mtunityiap · mtunityiap-1.4.0

仓库 3151；包 `com.magictavern.sdk.mtunityiap`；commit `49793a15c352634347cc5cd110510bc5ba4ccfc9`。

完整文件树未发现 ZIP / unitypackage。

### mtageguard · mtageguard-0.0.16

仓库 3273；包 `com.magictavern.ageguardsdk`；commit `de9471d1908637b216b8b0f8654ff6a3f1bede52`。

完整文件树未发现 ZIP / unitypackage。

### mtumcp · mtumcp-1.0.0

仓库 3755；包 `com.magictavern.umcp`；commit `3065419c57251cbb35a1a2097fe665e532f138c5`。

完整文件树未发现 ZIP / unitypackage。

### assetautogroup · assetautogroup-1.0.0

仓库 4124；包 `com.magictavern.assetautogroup`；commit `f3ffc4ff0f3208e6be117f0e26379cc6b7c7fc0c`。

完整文件树未发现 ZIP / unitypackage。

