using System;
using System.Linq;
using System.Reflection;
using dev.limitex.avatar.compressor.editor.texture;
using dev.limitex.avatar.compressor.editor.texture.ui;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using static dev.limitex.avatar.compressor.tests.LocalizationTestCatalog;

namespace dev.limitex.avatar.compressor.tests
{
    [TestFixture]
    internal sealed class LocalizedInspectorTests : LocalizationTestBase
    {
        [TestCaseSource(typeof(LocalizationTestCatalog), nameof(BundledLocales))]
        public void PresetPropertyLabels_AllSerializedFields_HaveLocalizedContent(string language)
        {
            LanguagePrefs.Language = language;
            var preset = ScriptableObject.CreateInstance<CustomTextureCompressorPreset>();
            try
            {
                using var serialized = new SerializedObject(preset);
                var property = serialized.GetIterator();
                for (bool enter = true; property.NextVisible(enter); enter = false)
                {
                    if (property.name == "m_Script")
                        continue;

                    var content = TextureCompressorPropertyLabels.Content(property.name);
                    Assert.IsNotEmpty(content.text, property.name);
                    Assert.IsFalse(
                        content.text.StartsWith("<", StringComparison.Ordinal),
                        property.name
                    );
                    Assert.IsFalse(
                        (content.tooltip ?? "").StartsWith("<", StringComparison.Ordinal),
                        property.name
                    );
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void EnumKeys_MatchSourceCatalog()
        {
            string[] sourceKeys = ReadMessages(LocalizationFolder + "/en-US.po")
                .Keys.Where(key => key.Contains(":enum:"))
                .ToArray();
            string[] mappedKeys = LocalizableEnumTypes()
                .SelectMany(type => Enum.GetValues(type).Cast<Enum>())
                .Select(TextureCompressorEnumLabels.Key)
                .ToArray();

            CollectionAssert.DoesNotContain(mappedKeys, null);
            CollectionAssert.AllItemsAreUnique(mappedKeys);
            CollectionAssert.AreEquivalent(sourceKeys, mappedKeys);
        }

        [Test]
        public void EnumMappings_CoverEveryDisplayedValue()
        {
            LanguagePrefs.Language = "en-US";

            AssertEnumLocalized<AnalysisStrategyType>();
            AssertEnumLocalized<CompressionPlatform>();
            AssertEnumLocalized<FrozenTextureFormat>();
            AssertEnumLocalized<AnalysisBackendPreference>();
            AssertEnumLocalized<ResizeBackendPreference>();
            AssertEnumLocalized<TexturePropertyCategory>();
            AssertEnumLocalized<SkipReason>();
        }

        [Test]
        public void CustomPresetInspector_PreservesMultiObjectEditing()
        {
            Assert.IsTrue(
                Attribute.IsDefined(
                    typeof(CustomTextureCompressorPresetEditor),
                    typeof(CanEditMultipleObjects)
                )
            );
        }

        [Test]
        public void LocalizedInspectorTargets_HaveNoHeaderOrTooltipDecorators()
        {
            const BindingFlags allFields =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly;
            string[] decoratedFields = new[]
            {
                typeof(TextureCompressor),
                typeof(CustomTextureCompressorPreset),
            }
                .SelectMany(type => type.GetFields(allFields))
                .Where(field =>
                    Attribute.IsDefined(field, typeof(HeaderAttribute))
                    || Attribute.IsDefined(field, typeof(TooltipAttribute))
                )
                .Select(field => $"{field.DeclaringType.Name}.{field.Name}")
                .ToArray();

            CollectionAssert.IsEmpty(decoratedFields);
        }

        [TestCase(CompressorPreset.HighQuality, "High Quality")]
        [TestCase(CompressorPreset.Quality, "Quality")]
        [TestCase(CompressorPreset.Balanced, "Balanced")]
        [TestCase(CompressorPreset.Aggressive, "Aggressive")]
        [TestCase(CompressorPreset.Maximum, "Maximum")]
        [TestCase(CompressorPreset.Custom, "Custom")]
        public void StandardPreset_KeepsDisplayNameEnglish(
            CompressorPreset preset,
            string expectedName
        )
        {
            LanguagePrefs.Language = "zh-Hans";

            Assert.AreEqual(expectedName, PresetSection.GetPresetDisplayName(preset));
        }

        private static void AssertEnumLocalized<T>()
            where T : struct, Enum
        {
            foreach (T value in Enum.GetValues(typeof(T)))
            {
                string localized = TextureCompressorEnumLabels.Text(value);
                Assert.IsFalse(
                    localized.StartsWith("<", StringComparison.Ordinal),
                    $"Missing localization for {typeof(T).Name}.{value}."
                );
            }
        }

        private static Type[] LocalizableEnumTypes()
        {
            return new[]
            {
                typeof(AnalysisStrategyType),
                typeof(CompressionPlatform),
                typeof(FrozenTextureFormat),
                typeof(AnalysisBackendPreference),
                typeof(ResizeBackendPreference),
                typeof(TexturePropertyCategory),
                typeof(SkipReason),
            };
        }

        [Test]
        public void EnumLabels_UnknownSerializedValue_ShowsPlaceholder()
        {
            string text = TextureCompressorEnumLabels.Text((AnalysisStrategyType)int.MaxValue);
            StringAssert.StartsWith("<", text);
            StringAssert.EndsWith(">", text);
        }
    }
}
