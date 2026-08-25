using System.Threading.Tasks;
using UnityEditor;
using LM = MagicTavern.FrontEngine.MTPManager.LogManager;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 等到 AssetDatabase 导入 / 脚本编译都闲下来。
    /// ImportPackage、改宏之后 Unity 往往过一两拍才把 isUpdating/isCompiling 置位，
    /// 所以要连续若干次检查都空闲才算完成，避免和下一拍 UPM 抢跑。
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
        /// 循环等待 Task 完成，不直接 await，避免续跑进编译泵 / 模态循环。
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
