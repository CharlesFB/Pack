// ==================================================
// Copyright (c) Magic Tavern. All rights reserved.
// @Author: 陈梓霄
// @Maintainer: 陈梓霄
// @Description: Button + GenericMenu 下拉，替代 2019 没有的 DropdownField
// ==================================================

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MagicTavern.FrontEngine.MTPManager
{
    internal static class DropdownMenuUtils
    {
        public static void Show(Button button, IList<string> choices, Action<string> onSelected)
        {
            if (button == null || choices == null || choices.Count == 0)
            {
                return;
            }

            string current = button.text ?? string.Empty;
            var menu = new GenericMenu();
            for (int i = 0; i < choices.Count; i++)
            {
                string choice = choices[i];
                if (string.IsNullOrEmpty(choice))
                {
                    continue;
                }

                bool on = string.Equals(current, choice, StringComparison.Ordinal);
                menu.AddItem(new GUIContent(choice), on, () =>
                {
                    onSelected?.Invoke(choice);
                });
            }

            menu.DropDown(button.worldBound);
        }
    }
}
