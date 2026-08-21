using System.Collections;
using UnityEditor;
using UnityEngine;

namespace MagicTavern.FrontEngine.MTPDevToolkit
{
    public static class EditorCoroutineUtility
    {
        public static void StartCoroutine(IEnumerator routine, object owner)
        {
            EditorApplication.update += Update;
            EditorCoroutineRunner.Instance.StartCoroutine(routine);
        }

        private static void Update()
        {
            if (!EditorCoroutineRunner.Instance.IsRunning)
            {
                EditorApplication.update -= Update;
            }
        }
    }

    public class EditorCoroutineRunner
    {
        private static EditorCoroutineRunner _instance;
        public static EditorCoroutineRunner Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new EditorCoroutineRunner();
                }
                return _instance;
            }
        }

        private IEnumerator _currentCoroutine;
        public bool IsRunning => _currentCoroutine != null;

        public void StartCoroutine(IEnumerator routine)
        {
            _currentCoroutine = routine;
            EditorApplication.update += Update;
        }

        private void Update()
        {
            if (_currentCoroutine == null)
            {
                EditorApplication.update -= Update;
                return;
            }

            if (!_currentCoroutine.MoveNext())
            {
                _currentCoroutine = null;
                EditorApplication.update -= Update;
            }
        }
    }
} 