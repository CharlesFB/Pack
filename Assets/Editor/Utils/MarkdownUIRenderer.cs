// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Description: 将 Markdown 文本解析为 UI Toolkit VisualElement 树，
// 供任意需要展示 Markdown 内容的 View（Changelog / License / Documentation 等）复用。
// ==================================================

using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    /// <summary>
    /// 无状态 Markdown 渲染器：输入 Markdown 文本与目标容器，输出对应的 VisualElement 树。
    /// 不持有任何数据绑定/生命周期状态，View 层负责决定何时调用、内容为空时如何兜底展示。
    /// </summary>
    internal static class MarkdownUIRenderer
    {
        private static Font s_codeFont;

        private static Font CodeFont
        {
            get
            {
                if (s_codeFont == null)
                {
                    s_codeFont = AssetDatabase.LoadAssetAtPath<Font>(MTPUI.MONACO_FONT_PATH);
                }

                return s_codeFont;
            }
        }

        /// <summary>
        /// 解析 markdown 并将生成的元素追加到 container 下。
        /// </summary>
        internal static void RenderInto(VisualElement container, string markdown)
        {
            if (container == null || string.IsNullOrEmpty(markdown))
            {
                return;
            }

            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            bool inCodeBlock = false;
            VisualElement codeBlockElement = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("```"))
                {
                    inCodeBlock = !inCodeBlock;

                    if (inCodeBlock)
                    {
                        codeBlockElement = new VisualElement();
                        codeBlockElement.AddToClassList("md-code-block");
                        container.Add(codeBlockElement);
                    }

                    continue;
                }

                if (inCodeBlock)
                {
                    var codeLabel = new Label(line) { style = { whiteSpace = WhiteSpace.Normal } };
                    codeLabel.AddToClassList("md-code-line");
                    if (CodeFont != null)
                    {
                        codeLabel.style.unityFont = CodeFont;
                    }

                    codeBlockElement.Add(codeLabel);
                    continue;
                }

                if (line.StartsWith("# "))
                {
                    var heading = new Label(ParseInlineMarkdown(line.Substring(2)));
                    heading.AddToClassList("md-h1");
                    container.Add(heading);
                }
                else if (line.StartsWith("## "))
                {
                    var heading = new Label(ParseInlineMarkdown(line.Substring(3)));
                    heading.AddToClassList("md-h2");
                    container.Add(heading);
                }
                else if (line.StartsWith("### "))
                {
                    var heading = new Label(ParseInlineMarkdown(line.Substring(4)));
                    heading.AddToClassList("md-h3");
                    container.Add(heading);
                }
                else if (line.StartsWith("#### "))
                {
                    var heading = new Label(ParseInlineMarkdown(line.Substring(4)));
                    heading.AddToClassList("md-h4");
                    container.Add(heading);
                }
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    var listItem = new VisualElement();
                    listItem.AddToClassList("md-list-item");

                    var bullet = new Label("•");
                    bullet.AddToClassList("md-list-bullet");

                    var content = CreateStyledText(line.Substring(2));
                    content.style.flexGrow = 1;

                    listItem.Add(bullet);
                    listItem.Add(content);
                    container.Add(listItem);
                }
                else if (Regex.IsMatch(line, @"^\d+\.\s"))
                {
                    var match = Regex.Match(line, @"^(\d+)\.\s(.*)$");
                    if (match.Success)
                    {
                        var number = match.Groups[1].Value;
                        var content = match.Groups[2].Value;

                        var listItem = new VisualElement();
                        listItem.AddToClassList("md-list-item");

                        var numberLabel = new Label(number + ".");
                        numberLabel.AddToClassList("md-list-number");

                        var contentElement = CreateStyledText(content);
                        contentElement.style.flexGrow = 1;

                        listItem.Add(numberLabel);
                        listItem.Add(contentElement);
                        container.Add(listItem);
                    }
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    var paragraph = CreateStyledText(line);
                    paragraph.AddToClassList("md-paragraph");
                    container.Add(paragraph);
                }
                else
                {
                    var spacer = new VisualElement();
                    spacer.AddToClassList("md-spacer");
                    container.Add(spacer);
                }
            }
        }

        /// <summary>
        /// 目前仅做占位透传，为后续扩展行内链接/图片等语法预留入口。
        /// </summary>
        private static string ParseInlineMarkdown(string text)
        {
            return text;
        }

        /// <summary>
        /// 解析一行内的 *斜体*、**粗体**、***粗斜体*** 内联样式，拆分成多个 Label 拼接展示。
        /// </summary>
        private static VisualElement CreateStyledText(string text)
        {
            var container = new VisualElement();
            container.AddToClassList("md-styled-text");

            var currentIndex = 0;
            var textLength = text.Length;

            while (currentIndex < textLength)
            {
                var nextAsterisk = text.IndexOf('*', currentIndex);

                if (nextAsterisk == -1 || nextAsterisk >= textLength - 1)
                {
                    if (currentIndex < textLength)
                    {
                        var remainingText = text.Substring(currentIndex);
                        container.Add(new Label(remainingText) { style = { whiteSpace = WhiteSpace.Normal } });
                    }

                    break;
                }

                if (nextAsterisk > currentIndex)
                {
                    var normalText = text.Substring(currentIndex, nextAsterisk - currentIndex);
                    container.Add(new Label(normalText) { style = { whiteSpace = WhiteSpace.Normal } });
                }

                var styleType = 0; // 1 = 斜体, 2 = 粗体, 3 = 粗斜体
                var startPos = nextAsterisk;

                while (startPos < textLength && text[startPos] == '*')
                {
                    styleType++;
                    startPos++;

                    if (styleType > 3)
                    {
                        break;
                    }
                }

                if (styleType > 3 || startPos >= textLength)
                {
                    var asterisks = new string('*', styleType);
                    container.Add(new Label(asterisks) { style = { whiteSpace = WhiteSpace.Normal } });
                    currentIndex = startPos;
                    continue;
                }

                var searchStartPos = startPos;
                var endPos = -1;
                var tempPos = searchStartPos;

                while (tempPos < textLength)
                {
                    var nextPos = text.IndexOf('*', tempPos);
                    if (nextPos == -1)
                    {
                        break;
                    }

                    var consecutiveCount = 0;
                    while (nextPos + consecutiveCount < textLength && text[nextPos + consecutiveCount] == '*')
                    {
                        consecutiveCount++;
                    }

                    if (consecutiveCount == styleType)
                    {
                        endPos = nextPos;
                        break;
                    }

                    tempPos = nextPos + 1;
                }

                if (endPos == -1)
                {
                    var asterisks = new string('*', styleType);
                    container.Add(new Label(asterisks) { style = { whiteSpace = WhiteSpace.Normal } });
                    currentIndex = startPos;
                    continue;
                }

                var styledContent = text.Substring(startPos, endPos - startPos);
                var styledLabel = new Label(styledContent) { style = { whiteSpace = WhiteSpace.Normal } };

                switch (styleType)
                {
                    case 1:
                        styledLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                        break;
                    case 2:
                        styledLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                        break;
                    case 3:
                        styledLabel.style.unityFontStyleAndWeight = FontStyle.BoldAndItalic;
                        break;
                }

                container.Add(styledLabel);

                currentIndex = endPos + styleType;
            }

            return container;
        }
    }
}
