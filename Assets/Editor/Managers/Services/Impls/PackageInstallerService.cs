using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    using MTPRequest = IPackageInstallerService.MTPackageInstallerRequest;
    using LM = LogManager;
    
    public class PackageInstallerService : IPackageInstallerService
    {
        public List<MTPRequest> LoadInstallerRequest(List<MTPackageInfo> addList,
            List<MTPackageInfo> removeList)
        {
            return new List<MTPRequest>();
        }
        
        public async Task StartHandlingInstallerRequest(List<MTPRequest> requests)
        {
            if (requests == null || requests.Count == 0)
            {
                await Task.CompletedTask;
                return;
            }
            int totalCount = requests.Count;
            int finishedCount = 0;
            
            // 执行编译锁，并 Finally 解锁
            EditorApplication.LockReloadAssemblies();
            try
            {
                // 执行 UPM 请求
                foreach (var request in requests)
                {
                    EditorUtility.DisplayProgressBar(
                        title: $"MTP Installer",
                        info: $"Now handling No.{finishedCount+1} of {totalCount} requests in total...",
                        progress: (float)finishedCount / totalCount);
                    
                    // 如果为 Add 请求，则先添加assets
                    if (request.RequestType == RequestType.Add)
                    {
                        // 处理插件导入
                        // 1. 判断是否有导入冲突，有则提示用户
                        // 2. 缓存插件包，弹出import对话框，询问用户选择插件导入
                        // 3. 执行导入api
                        // 4. 字典存储 pluginPath -> pathnames，zip包的pathnames是拼接父目录后的结果，并持久化记录
                    }
                    
                    // 根据 Add / Remove 请求类型，分发给UPM不同的request
                    int timeoutCount = 0;
                    bool timedOut = false;
                    var req = request.StartRequest();
                    while (!req.IsCompleted)
                    {
                        if (timeoutCount >= MTPTime.INSTALLATION_TIMEOUT)
                        {
                            // 安装超时
                            timedOut = true;
                            break;
                        }
                        await Task.Delay(MTPTime.INSTALLATION_CHECK_TIME);
                        timeoutCount += MTPTime.INSTALLATION_CHECK_TIME;
                    }

                    request.RequestState = !timedOut && req.Status == StatusCode.Success
                        ? RequestState.Success
                        : RequestState.Failure;

                    // 安装错误提示
                    if (request.RequestState == RequestState.Failure)
                    {
                        var reason = timedOut ? "超时" : req.Error?.message ?? "未知错误";
                        LM.LogError(
                            $"[PackageInstallerService] {request.RequestType} 请求失败: " +
                            $"{request.RequestPackageInfo?.PackageName}, 原因: {reason}");
                    }
                    
                    // 如果为 Remove 请求
                    if (request.RequestType == RequestType.Remove)
                    {
                        // 处理插件卸载
                        // 1. 检查卸载冲突（已有api），筛选可安全卸载的条目，展示对话框给用户；同步展示本次卸载会影响到的资源文件父目录、卸载资源数目信息
                        // 2. 对话框确认后开始卸载资源，根据资源字典，调用api（api应当都是用资源锁合批过的）移除
                        // 3. 移除字典内对应的插件资源。
                        // 4. 同步本地持久化信息（落盘
                    }
                    
                    finishedCount++;
                    EditorUtility.ClearProgressBar(); // 清理当前一轮的安装进度
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar(); // 清理本次try内可能产生的进度条
                EditorApplication.UnlockReloadAssemblies(); // 解锁编译锁
            }
        }
    }
}