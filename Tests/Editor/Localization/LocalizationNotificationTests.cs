using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using dev.limitex.avatar.compressor.editor;
using dev.limitex.avatar.compressor.editor.texture;
using nadena.dev.ndmf.localization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace dev.limitex.avatar.compressor.tests
{
    [TestFixture]
    internal sealed class LocalizationNotificationTests : LocalizationTestBase
    {
        [UnityTest]
        public IEnumerator LanguageChanges_NotifyOnceWithFinalTranslation()
        {
            LanguagePrefs.Language = "en-US";
            _ = AvatarCompressorLocalization.Localizer;
            yield return WaitForEditorCallbacks();

            int calls = 0;
            string translation = null;
            void OnChanged()
            {
                calls++;
                translation = AvatarCompressorLocalization.Tr("Common:label:general");
            }

            AvatarCompressorLocalization.Changed += OnChanged;
            try
            {
                LanguagePrefs.Language = "ko-KR";
                LanguagePrefs.Language = "ja-JP";
                Assert.AreEqual(0, calls, "Notifications must not run inside the language picker.");

                yield return WaitForEditorCallbacks();
                Assert.AreEqual(1, calls);
                Assert.AreEqual(
                    AvatarCompressorLocalization.Tr("Common:label:general"),
                    translation
                );

                LanguagePrefs.Language = "ja-JP";
                yield return WaitForEditorCallbacks();
                Assert.AreEqual(1, calls, "Selecting the same language must not repaint again.");
            }
            finally
            {
                AvatarCompressorLocalization.Changed -= OnChanged;
            }
        }

        [UnityTest]
        public IEnumerator CatalogReload_WithoutLanguageChange_NotifiesAfterReload()
        {
            _ = AvatarCompressorLocalization.Localizer;
            yield return WaitForEditorCallbacks();
            int calls = 0;
            int observedRevision = -1;
            int before = AvatarCompressorLocalization.CatalogRevision;
            string language = AvatarCompressorLocalization.CurrentLanguage;
            void OnChanged()
            {
                calls++;
                observedRevision = AvatarCompressorLocalization.CatalogRevision;
            }

            AvatarCompressorLocalization.Changed += OnChanged;
            try
            {
                Localizer.ReloadLocalizations();
                Localizer.ReloadLocalizations();
                Assert.AreEqual(0, calls);
                yield return WaitForEditorCallbacks();

                Assert.AreEqual(1, calls);
                Assert.Greater(observedRevision, before);
                Assert.AreEqual(AvatarCompressorLocalization.CatalogRevision, observedRevision);
                Assert.AreEqual(language, AvatarCompressorLocalization.CurrentLanguage);
            }
            finally
            {
                AvatarCompressorLocalization.Changed -= OnChanged;
            }
        }

        [UnityTest]
        public IEnumerator UnsubscribedListener_DoesNotReceivePendingNotification()
        {
            _ = AvatarCompressorLocalization.Localizer;
            yield return WaitForEditorCallbacks();
            int calls = 0;
            void OnChanged() => calls++;
            AvatarCompressorLocalization.Changed += OnChanged;
            Localizer.ReloadLocalizations();
            AvatarCompressorLocalization.Changed -= OnChanged;

            yield return WaitForEditorCallbacks();
            Assert.AreEqual(0, calls);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InspectorLifecycle_ReleasesRepaintSubscription(bool presetInspector)
        {
            var gameObject = new GameObject("Localization lifecycle test");
            var preset = ScriptableObject.CreateInstance<CustomTextureCompressorPreset>();
            UnityEditor.Editor inspector = null;
            try
            {
                UnityEngine.Object target = presetInspector
                    ? (UnityEngine.Object)preset
                    : gameObject.AddComponent<TextureCompressor>();
                inspector = UnityEditor.Editor.CreateEditor(target);
                Assert.AreEqual(
                    presetInspector
                        ? typeof(CustomTextureCompressorPresetEditor)
                        : typeof(TextureCompressorEditor),
                    inspector.GetType()
                );
                Assert.AreEqual(1, RepaintSubscriptionCount(inspector));

                for (int i = 0; i < 3; i++)
                {
                    InvokeLifecycle(inspector, "OnDisable");
                    Assert.AreEqual(0, RepaintSubscriptionCount(inspector));
                    InvokeLifecycle(inspector, "OnEnable");
                    Assert.AreEqual(1, RepaintSubscriptionCount(inspector));
                }

                UnityEngine.Object.DestroyImmediate(inspector);
                Assert.AreEqual(0, RepaintSubscriptionCount(inspector));
            }
            finally
            {
                if (inspector != null)
                    UnityEngine.Object.DestroyImmediate(inspector);
                UnityEngine.Object.DestroyImmediate(preset);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void PreferencesLifecycle_SubscribesOnlyWhileActive()
        {
            var provider = (SettingsProvider)
                typeof(AvatarCompressorPreferences)
                    .GetMethod("CreateProvider", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, null);
            Assert.AreEqual(0, RepaintSubscriptionCount(provider));
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    provider.OnActivate("", new VisualElement());
                    Assert.AreEqual(1, RepaintSubscriptionCount(provider));
                    provider.OnDeactivate();
                    Assert.AreEqual(0, RepaintSubscriptionCount(provider));
                }
            }
            finally
            {
                provider.OnDeactivate();
            }
        }

        private static void InvokeLifecycle(UnityEditor.Editor inspector, string name)
        {
            inspector
                .GetType()
                .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(inspector, null);
        }

        private static int RepaintSubscriptionCount(object target)
        {
            var handlers = (Action)
                typeof(AvatarCompressorLocalization)
                    .GetField("Changed", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            return handlers
                    ?.GetInvocationList()
                    .Count(handler =>
                        ReferenceEquals(handler.Target, target) && handler.Method.Name == "Repaint"
                    )
                ?? 0;
        }
    }
}
