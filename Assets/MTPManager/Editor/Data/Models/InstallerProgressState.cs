using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 安装器批次中断恢复进度。映射 MTPPath.INSTALLER_PROGRESS_PATH。
    /// 进度条：CompletedCount / (CompletedCount + RestRequests.Count)。
    /// </summary>
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class InstallerProgressState
    {
        //剩余未处理请求
        public List<MTPackageInstallerRequest> RestRequests = new List<MTPackageInstallerRequest>();

        //已完整结束的请求数量
        public int CompletedCount;

        //RestRequests[0] 是否已完成 UPM 请求，尚待处理宏/插件。
        public bool CurrentUpmDone;

        // ===============  控制插件选择恢复，如果用户已选择过恢复流程不会重复弹出导入框  =================
        //当前包插件选择窗是否已经出过结果（含取消）。域重载后不再弹窗
        public bool PluginSelectionDone;
        
        public bool PluginSelectionConfirmed;
        
        public List<string> SelectedPluginKeys = new List<string>();
        public Dictionary<string, string> ZipExtractDirs = new Dictionary<string, string>();
        
        // ======================================================================================

        //当前包插件导入阶段已结束（成功、取消或无需插件）
        public bool PluginsImportDone;
    }
}
