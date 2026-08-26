using System.Threading.Tasks;
using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 等到 AssetDatabase 导入 / 脚本编译都闲下来。
    /// </summary>
    internal static class EditorBusyWait
    {
        private const int IDLE_STREAK_NEEDED = 3;

        public static bool IsBusy()
        {
            return EditorApplication.isCompiling
                   || EditorApplication.isUpdating;
        }

        public static async Task WaitUntilIdleAsync()
        {
            int elapsed = 0;
            int idleStreak = 0;
            while (elapsed < MTPTime.INSTALLATION_TIMEOUT)
            {
                if (IsBusy())
                {
                    idleStreak = 0;
                }
                else if (++idleStreak >= IDLE_STREAK_NEEDED)
                {
                    return;
                }

                await Task.Delay(MTPTime.INSTALLATION_CHECK_TIME);
                elapsed += MTPTime.INSTALLATION_CHECK_TIME;
            }

            LM.LogWarning("[EditorBusyWait] WaitUntilIdle 超时，编辑器仍在导入或编译");
        }

        /// <summary>
        /// 循环等待 Task 完成，不直接 await
        /// 用户交互（插件选择窗）无超时。
        /// </summary>
        public static async Task WaitUntilTaskAsync(Task task)
        {
            if (task == null || task.IsCompleted)
            {
                return;
            }

            while (!task.IsCompleted)
            {
                await Task.Delay(MTPTime.INSTALLATION_CHECK_TIME);
            }
        }
    }
}
