using System;
using System.Collections;
using System.IO;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;

namespace dev.limitex.avatar.compressor.tests
{
    internal abstract class LocalizationTestBase
    {
        private string _originalLanguage;
        private string _tempFolder;

        [SetUp]
        public void SaveEditorState()
        {
            _originalLanguage = LanguagePrefs.Language;
            _tempFolder = "Assets/_LAC_Localization_" + Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void RestoreEditorState()
        {
            try
            {
                if (AssetDatabase.IsValidFolder(_tempFolder))
                    AssetDatabase.DeleteAsset(_tempFolder);
            }
            finally
            {
                LanguagePrefs.Language = _originalLanguage;
            }
        }

        protected string CreateTempPo(
            string fileName,
            string entries,
            string header = "\"Language: xx-XX\\n\"\n"
        )
        {
            if (!AssetDatabase.IsValidFolder(_tempFolder))
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(_tempFolder));

            string path = _tempFolder + "/" + fileName;
            File.WriteAllText(path, "msgid \"\"\nmsgstr \"\"\n" + header + "\n" + entries);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }

        protected static void DeleteTempAsset(string path)
        {
            AssetDatabase.DeleteAsset(path);
        }

        protected static IEnumerator WaitForEditorCallbacks()
        {
            // Catalog reloads can enqueue a notification for the following editor tick.
            for (int i = 0; i < 2; i++)
            {
                bool called = false;
                void Callback() => called = true;
                EditorApplication.delayCall += Callback;
                try
                {
                    double deadline = EditorApplication.timeSinceStartup + 5;
                    while (!called && EditorApplication.timeSinceStartup < deadline)
                        yield return null;
                    Assert.IsTrue(called, "Editor delayCall did not run.");
                }
                finally
                {
                    EditorApplication.delayCall -= Callback;
                }
            }
        }
    }
}
