using UnityEditor;

namespace MagicTavern.FrontEngine.MTPManager.Test
{
    public class RawTest
    {
        [MenuItem("MTPTest/ClearBar")]
        public static void CloseProgressBar()
        {
            EditorUtility.ClearProgressBar();
        }
    }
}