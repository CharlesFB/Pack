using System;
using System.Collections.Generic;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 安装器批次中断恢复进度。映射 MTPPath.INSTALLER_PROGRESS_PATH。
    /// 进度条：CompletedCount / (CompletedCount + RestRequests.Count)。
    /// </summary>
    [Serializable]
    public class InstallerProgressState
    {
        /// <summary>尚未走完的请求，按下标 0 起顺序执行。</summary>
        public List<MTPackageInstallerRequest> RestRequests = new List<MTPackageInstallerRequest>();

        /// <summary>已完整结束（UPM + 资源处理）的条数。</summary>
        public int CompletedCount;

        /// <summary>RestRequests[0] 是否已完成 UPM 请求，尚待处理宏/插件。</summary>
        public bool CurrentUpmDone;

        // ===============  控制插件选择恢复，如果用户已选择过恢复流程不会重复弹出导入框  =================
        /// <summary>当前包插件选择窗是否已经出过结果（含取消）。域重载后不再弹窗。</summary>
        public bool PluginSelectionDone;
        
        /// <summary>用户是否点了 Import（false 表示取消 / 未勾选）。</summary>
        public bool PluginSelectionConfirmed;
        
        public List<string> SelectedPluginKeys = new List<string>();
        public Dictionary<string, string> ZipExtractDirs = new Dictionary<string, string>();
        
        // ======================================================================================

        /// <summary>当前包插件导入阶段已结束（成功、取消或无需插件）。</summary>
        public bool PluginsImportDone;
    }
}
