using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using dev.limitex.avatar.compressor.editor;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using static dev.limitex.avatar.compressor.tests.LocalizationTestCatalog;

namespace dev.limitex.avatar.compressor.tests
{
    [TestFixture]
    internal sealed class AvatarCompressorLocalizationTests : LocalizationTestBase
    {
        [Test]
        public void LocalizationFolder_ExistsInPackage()
        {
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(LocalizationFolder),
                $"'{LocalizationFolder}' is not a folder."
            );
        }

        [Test]
        public void LoadLocalizations_TakesLanguageFromFileNameAndIncludesBundledLocales()
        {
            string[] languages = AvatarCompressorLocalization
                .LoadLocalizations()
                .Select(entry => entry.Language)
                .ToArray();
            string[] fileNames = PoPaths().Select(Path.GetFileNameWithoutExtension).ToArray();

            CollectionAssert.AreEquivalent(fileNames, languages);
            CollectionAssert.IsSubsetOf(BundledLocales, languages);
            CollectionAssert.AllItemsAreUnique(
                languages.Select(language => language.ToLowerInvariant()).ToArray()
            );
            foreach (string language in languages)
            {
                Assert.DoesNotThrow(
                    () => CultureInfo.GetCultureInfo(language),
                    $"'{language}' is not a culture name."
                );
            }
        }

        [Test]
        public void LoadLocalizations_SkipsFilesWithoutUniqueCultureNames()
        {
            string valid = CreateTempPo("fr-FR.po", "msgid \"k\"\nmsgstr \"v\"\n");
            string invalid = CreateTempPo("not a culture.po", "msgid \"k\"\nmsgstr \"v\"\n");

            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("not a culture name"));
                LogAssert.Expect(LogType.Warning, new Regex("already provides this locale"));

                var loaded = AvatarCompressorLocalization.LoadLocalizations(
                    new[] { invalid, valid, valid }
                );

                Assert.AreEqual(1, loaded.Count);
                Assert.AreEqual("fr-FR", loaded[0].Language);
                Assert.AreEqual("v", loaded[0].Lookup("k"));
            }
            finally
            {
                DeleteTempAsset(valid);
                DeleteTempAsset(invalid);
            }
        }

        [Test]
        public void LoadLocalizations_ImportsCrowdinExportedHeader()
        {
            string path = CreateTempPo(
                "pt-BR.po",
                "msgid \"k\"\nmsgstr \"v\"\n",
                "\"Language: pt_BR\\n\"\n"
                    + "\"Plural-Forms: nplurals=2; plural=(n > 1);\\n\"\n"
                    + "\"X-Crowdin-Project: avatar-compressor\\n\"\n"
                    + "\"X-Crowdin-Language: pt-BR\\n\"\n"
                    + "\"PO-Revision-Date: 2026-09-23 00:00\\n\"\n"
            );

            try
            {
                var loaded = AvatarCompressorLocalization.LoadLocalizations(new[] { path });

                Assert.AreEqual(1, loaded.Count);
                Assert.AreEqual("pt-BR", loaded[0].Language);
                Assert.AreEqual("v", loaded[0].Lookup("k"));
            }
            finally
            {
                DeleteTempAsset(path);
            }
        }

        [Test]
        public void CreateLookup_EmptyOrMissingTranslation_ReturnsNull()
        {
            string path = CreateTempPo(
                "lac_localization_lookup.po",
                "msgid \"translated\"\nmsgstr \"value\"\n\nmsgid \"untranslated\"\nmsgstr \"\"\n"
            );

            try
            {
                var lookup = AvatarCompressorLocalization.CreateLookup(
                    AssetDatabase.LoadAssetAtPath<LocalizationAsset>(path)
                );

                Assert.IsNotNull(lookup);
                Assert.AreEqual("value", lookup("translated"));
                Assert.IsNull(lookup("untranslated"));
                Assert.IsNull(lookup("missing"));
            }
            finally
            {
                DeleteTempAsset(path);
            }
        }

        [Test]
        public void CreateLookup_DeletedAsset_ReturnsNull()
        {
            string path = CreateTempPo(
                "lac_localization_deleted.po",
                "msgid \"k\"\nmsgstr \"v\"\n"
            );
            var lookup = AvatarCompressorLocalization.CreateLookup(
                AssetDatabase.LoadAssetAtPath<LocalizationAsset>(path)
            );
            Assert.AreEqual("v", lookup("k"));

            DeleteTempAsset(path);

            Assert.IsNull(lookup("k"));
        }

        [Test]
        public void Translations_ReturnsTextFromEveryLocaleThatTranslatesKey()
        {
            const string key = "Common:label:language";
            string[] expected = PoPaths()
                .Select(path => ReadMessages(path).TryGetValue(key, out var text) ? text : "")
                .Where(text => text.Length > 0)
                .ToArray();

            CollectionAssert.AreEquivalent(
                expected,
                AvatarCompressorLocalization.Translations(key).ToArray()
            );
        }

        [TestCaseSource(typeof(LocalizationTestCatalog), nameof(BundledLocales))]
        public void LanguagePrefs_SelectsTranslationFromMatchingFile(string language)
        {
            const string key = "Common:label:general";
            string baseLanguage = language.Split('-')[0];
            string[] fallbackChain = new[] { language }
                .Concat(
                    PoPaths()
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(locale => locale != language && locale.Split('-')[0] == baseLanguage)
                        .OrderBy(locale => locale, StringComparer.OrdinalIgnoreCase)
                )
                .Concat(new[] { "en-US" })
                .ToArray();
            string expected = fallbackChain
                .Select(locale =>
                    ReadMessages(LocalizationFolder + "/" + locale + ".po")
                        .TryGetValue(key, out var text)
                        ? text
                        : ""
                )
                .First(text => text.Length > 0);

            LanguagePrefs.Language = language;

            Assert.AreEqual(expected, AvatarCompressorLocalization.Tr(key));
        }

        [Test]
        public void Tr_FormatsArguments()
        {
            LanguagePrefs.Language = "en-US";

            Assert.AreEqual(
                "Paths (3)",
                AvatarCompressorLocalization.Tr("TextureCompressor:message:pathsCount", 3)
            );
        }

        [Test]
        public void Tr_TooFewFormatArguments_ReturnsMissingKeyPlaceholder()
        {
            LanguagePrefs.Language = "en-US";
            const string key = "TextureCompressor:message:summaryDivisorRange";

            Assert.AreEqual($"<{key}>", AvatarCompressorLocalization.Tr(key, 1));
        }

        [Test]
        public void Localizer_EmptyChineseTranslation_FallsBackToOtherChineseScriptBeforeEnglish()
        {
            string[] paths =
            {
                CreateTempPo("en-US.po", "msgid \"k\"\nmsgstr \"en\"\n"),
                CreateTempPo("zh-Hans.po", "msgid \"k\"\nmsgstr \"\"\n"),
                CreateTempPo("zh-Hant.po", "msgid \"k\"\nmsgstr \"hant\"\n"),
            };
            // Disable the retained NDMF reload callback before deleting temporary assets.
            bool live = true;
            var localizer = new Localizer(
                "en-US",
                () =>
                    live
                        ? AvatarCompressorLocalization.LoadLocalizations(paths)
                        : new List<(string, Func<string, string>)>()
            );

            try
            {
                LanguagePrefs.Language = "zh-Hans";
                Assert.AreEqual("hant", localizer.GetLocalizedString("k"));

                LanguagePrefs.Language = "ja-JP";
                Assert.AreEqual("en", localizer.GetLocalizedString("k"));
            }
            finally
            {
                live = false;
                foreach (string path in paths)
                    DeleteTempAsset(path);
            }
        }
    }
}
