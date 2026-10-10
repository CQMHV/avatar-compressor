using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using dev.limitex.avatar.compressor.editor;
using dev.limitex.avatar.compressor.editor.ui;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static dev.limitex.avatar.compressor.tests.LocalizationTestCatalog;

namespace dev.limitex.avatar.compressor.tests
{
    [TestFixture]
    internal sealed class LocalizationReloadTests : LocalizationTestBase
    {
        private TemporaryCatalog _temporaryCatalog;

        [TearDown]
        public void DeleteTemporaryCatalog()
        {
            try
            {
                _temporaryCatalog?.Dispose();
            }
            finally
            {
                _temporaryCatalog = null;
            }
        }

        [Test]
        public void SearchCount_PoReimportInSameLanguage_UsesUpdatedTranslation()
        {
            const string key = "Tests:searchImport";
            string assetPath = LocalizationFolder + "/en-US.po";
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            string path = Path.Combine(
                package.resolvedPath,
                assetPath.Substring(package.assetPath.Length + 1)
            );
            byte[] original = File.ReadAllBytes(path);
            string originalText = Encoding.UTF8.GetString(original);

            void ImportTranslation(string value)
            {
                File.WriteAllText(path, originalText + $"\nmsgid \"{key}\"\nmsgstr \"{value}\"\n");
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            try
            {
                ImportTranslation("before");
                LanguagePrefs.Language = "en-US";
                var search = new SearchBoxControl("updated");
                var items = new[] { key };
                Func<string, bool> matches = id =>
                    search.MatchesSearch(AvatarCompressorLocalization.Tr(id));
                Assert.AreEqual(
                    0,
                    search.CountMatches(
                        items,
                        matches,
                        (
                            AvatarCompressorLocalization.CurrentLanguage,
                            AvatarCompressorLocalization.CatalogRevision
                        )
                    )
                );

                ImportTranslation("updated");

                Assert.AreEqual("updated", AvatarCompressorLocalization.Tr(key));
                Assert.AreEqual(
                    1,
                    search.CountMatches(
                        items,
                        matches,
                        (
                            AvatarCompressorLocalization.CurrentLanguage,
                            AvatarCompressorLocalization.CatalogRevision
                        )
                    )
                );
            }
            finally
            {
                File.WriteAllBytes(path, original);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [Test]
        public void SearchCount_LocalizationReloadInSameLanguage_UsesUpdatedTranslation()
        {
            string path = CreateTempPo("en-US.po", "msgid \"kind\"\nmsgstr \"before\"\n");
            bool live = true;
            var localizer = new Localizer(
                "en-US",
                () =>
                    live
                        ? AvatarCompressorLocalization.LoadLocalizations(new[] { path })
                        : new List<(string, Func<string, string>)>()
            );

            try
            {
                LanguagePrefs.Language = "en-US";
                var search = new SearchBoxControl("updated");
                var items = new[] { "kind" };
                Func<string, bool> matches = key =>
                    search.MatchesSearch(localizer.GetLocalizedString(key));
                Assert.AreEqual(
                    0,
                    search.CountMatches(
                        items,
                        matches,
                        (
                            AvatarCompressorLocalization.CurrentLanguage,
                            AvatarCompressorLocalization.CatalogRevision
                        )
                    )
                );

                CreateTempPo("en-US.po", "msgid \"kind\"\nmsgstr \"updated\"\n");
                Localizer.ReloadLocalizations();

                Assert.AreEqual("updated", localizer.GetLocalizedString("kind"));
                Assert.AreEqual(
                    1,
                    search.CountMatches(
                        items,
                        matches,
                        (
                            AvatarCompressorLocalization.CurrentLanguage,
                            AvatarCompressorLocalization.CatalogRevision
                        )
                    )
                );
            }
            finally
            {
                live = false;
                DeleteTempAsset(path);
            }
        }

        [UnityTest]
        public IEnumerator CatalogChanges_AddedAndRecreatedLocale_RefreshesTranslationAndSearch()
        {
            var catalog = _temporaryCatalog = new TemporaryCatalog();
            LanguagePrefs.Language = catalog.Language;
            var search = new SearchBoxControl("translated");
            var items = new[] { TemporaryCatalog.Key };
            Func<string, bool> matches = key =>
                search.MatchesSearch(AvatarCompressorLocalization.Tr(key));
            Assert.AreEqual(
                0,
                search.CountMatches(
                    items,
                    matches,
                    (
                        AvatarCompressorLocalization.CurrentLanguage,
                        AvatarCompressorLocalization.CatalogRevision
                    )
                )
            );

            catalog.Import();
            yield return WaitForTranslation(TemporaryCatalog.Key, "translated");

            Assert.AreEqual(
                1,
                search.CountMatches(
                    items,
                    matches,
                    (
                        AvatarCompressorLocalization.CurrentLanguage,
                        AvatarCompressorLocalization.CatalogRevision
                    )
                )
            );

            catalog.Delete();
            yield return WaitForTranslation(TemporaryCatalog.Key, $"<{TemporaryCatalog.Key}>");
            Assert.AreEqual(
                0,
                search.CountMatches(
                    items,
                    matches,
                    (
                        AvatarCompressorLocalization.CurrentLanguage,
                        AvatarCompressorLocalization.CatalogRevision
                    )
                )
            );

            catalog.Import();
            yield return WaitForTranslation(TemporaryCatalog.Key, "translated");
            Assert.AreEqual(
                1,
                search.CountMatches(
                    items,
                    matches,
                    (
                        AvatarCompressorLocalization.CurrentLanguage,
                        AvatarCompressorLocalization.CatalogRevision
                    )
                )
            );
        }

        [UnityTest]
        public IEnumerator CatalogChanges_MovedLocale_UsesOnlyFilesInsideCatalog()
        {
            var catalog = _temporaryCatalog = new TemporaryCatalog();
            LanguagePrefs.Language = catalog.Language;
            _ = AvatarCompressorLocalization.Tr(TemporaryCatalog.Key);

            catalog.Import();
            yield return WaitForTranslation(TemporaryCatalog.Key, "translated");

            // Preserve the asset GUID so a stale lookup would still return its translation.
            catalog.MoveOutside();
            yield return WaitForTranslation(TemporaryCatalog.Key, $"<{TemporaryCatalog.Key}>");

            catalog.MoveBack();
            yield return WaitForTranslation(TemporaryCatalog.Key, "translated");
        }

        [UnityTest]
        public IEnumerator CatalogReimport_NotifiesWithUpdatedTranslationInSameLanguage()
        {
            var catalog = _temporaryCatalog = new TemporaryCatalog();
            LanguagePrefs.Language = catalog.Language;
            catalog.Import();
            yield return WaitForTranslation(TemporaryCatalog.Key, "translated");
            yield return WaitForEditorCallbacks();

            var observed = new List<string>();
            void OnChanged() => observed.Add(AvatarCompressorLocalization.Tr(TemporaryCatalog.Key));
            AvatarCompressorLocalization.Changed += OnChanged;
            try
            {
                catalog.Import("updated");
                yield return WaitForEditorCallbacks();
                CollectionAssert.AreEqual(new[] { "updated" }, observed);
                Assert.AreEqual(
                    catalog.Language.ToLowerInvariant(),
                    AvatarCompressorLocalization.CurrentLanguage
                );
            }
            finally
            {
                AvatarCompressorLocalization.Changed -= OnChanged;
            }
        }

        private static IEnumerator WaitForTranslation(string key, string expected)
        {
            yield return null;
            double deadline = EditorApplication.timeSinceStartup + 5;
            while (
                AvatarCompressorLocalization.Tr(key) != expected
                && EditorApplication.timeSinceStartup < deadline
            )
            {
                yield return null;
            }

            Assert.AreEqual(expected, AvatarCompressorLocalization.Tr(key));
        }

        private sealed class TemporaryCatalog : IDisposable
        {
            internal const string Key = "Tests:catalogLifecycle";
            internal readonly string Language;
            private readonly string _assetPath;
            private readonly string _physicalPath;
            private readonly string _outsidePath;

            internal TemporaryCatalog()
            {
                var existing = new HashSet<string>(
                    AvatarCompressorLocalization
                        .LoadLocalizations()
                        .Select(entry => entry.Language),
                    StringComparer.OrdinalIgnoreCase
                );
                Language = CultureInfo
                    .GetCultures(CultureTypes.SpecificCultures)
                    .Select(culture => culture.Name)
                    .First(language => !existing.Contains(language));
                _assetPath = LocalizationFolder + "/" + Language + ".po";
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(_assetPath);
                _physicalPath = Path.Combine(
                    package.resolvedPath,
                    _assetPath.Substring(package.assetPath.Length + 1)
                );
                _outsidePath = Path.Combine(
                    Application.dataPath,
                    "_LAC_Catalog_" + Guid.NewGuid().ToString("N") + ".po"
                );
                Assert.IsFalse(File.Exists(_physicalPath));
                Assert.IsFalse(File.Exists(_physicalPath + ".meta"));
            }

            internal void Import(string translation = "translated")
            {
                File.WriteAllText(
                    _physicalPath,
                    $"msgid \"\"\nmsgstr \"\"\n\"Language: {Language}\\n\"\n\n"
                        + $"msgid \"{Key}\"\nmsgstr \"{translation}\"\n"
                );
                AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            internal void MoveOutside() => Move(_physicalPath, _outsidePath);

            internal void MoveBack() => Move(_outsidePath, _physicalPath);

            private static void Move(string from, string to)
            {
                File.Move(from, to);
                File.Move(from + ".meta", to + ".meta");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            internal void Delete()
            {
                File.Delete(_physicalPath);
                File.Delete(_physicalPath + ".meta");
                File.Delete(_outsidePath);
                File.Delete(_outsidePath + ".meta");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            public void Dispose() => Delete();
        }

        [Test]
        public void SearchCount_LanguageChange_UsesCurrentTranslation()
        {
            const string key = "Common:label:general";
            LanguagePrefs.Language = "en-US";
            var search = new SearchBoxControl(AvatarCompressorLocalization.Tr(key));
            var items = new[] { key };
            Func<string, bool> matches = id =>
                search.MatchesSearch(AvatarCompressorLocalization.Tr(id));
            Assert.AreEqual(
                1,
                search.CountMatches(
                    items,
                    matches,
                    (
                        AvatarCompressorLocalization.CurrentLanguage,
                        AvatarCompressorLocalization.CatalogRevision
                    )
                )
            );

            LanguagePrefs.Language = "ja-JP";
            int expected = matches(key) ? 1 : 0;
            Assert.AreEqual(
                expected,
                search.CountMatches(
                    items,
                    matches,
                    (
                        AvatarCompressorLocalization.CurrentLanguage,
                        AvatarCompressorLocalization.CatalogRevision
                    )
                )
            );
        }
    }
}
