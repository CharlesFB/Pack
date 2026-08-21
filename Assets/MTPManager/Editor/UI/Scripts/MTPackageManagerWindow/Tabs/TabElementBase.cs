using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 管理标签页复用方法
    /// </summary>
    public class TabElementBase : VisualElement
    {
        public void AddStyleSheetIfExists(string path)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }
        }

        public Label LabelWithClass(string text, string className, StyleColor color)
        {
            var lb = new Label(text);
            lb.style.color = color;
            lb.AddToClassList(className);
            return lb;
        }
    }
}