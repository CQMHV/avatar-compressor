using System;
using System.Reflection;
using dev.limitex.avatar.compressor.editor.texture.ui;
using dev.limitex.avatar.compressor.editor.ui;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace dev.limitex.avatar.compressor.tests
{
    [TestFixture]
    internal sealed class LocalizedEnumEditingTests
    {
        private CustomTextureCompressorPreset _first;
        private CustomTextureCompressorPreset _second;
        private LocalizedEnumTestWindow _window;
        private string _prefabPath;
        private GameObject _prefabInstance;

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore(
                    "IMGUI interaction tests require a graphics device; omit -nographics."
                );

            _first = ScriptableObject.CreateInstance<CustomTextureCompressorPreset>();
            _second = ScriptableObject.CreateInstance<CustomTextureCompressorPreset>();
            _first.Strategy = AnalysisStrategyType.Fast;
            _second.Strategy = AnalysisStrategyType.Combined;
            _window = ScriptableObject.CreateInstance<LocalizedEnumTestWindow>();
            _window.position = new Rect(50, 50, 450, 100);
            _window.ShowUtility();
            _window.SetTargets(_first, _second);
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
                _window.Close();
            if (_prefabInstance != null)
                Undo.ClearUndo(_prefabInstance.GetComponent<TextureCompressor>());
            foreach (var target in new UnityEngine.Object[] { _first, _second, _prefabInstance })
            {
                if (target == null)
                    continue;
                Undo.ClearUndo(target);
                UnityEngine.Object.DestroyImmediate(target);
            }
            if (_prefabPath != null)
                AssetDatabase.DeleteAsset(_prefabPath);
        }

        [Test]
        public void MixedValues_DrawingWithoutInput_PreservesEachValue()
        {
            _window.Draw();
            Assert.IsTrue(_window.HasMixedValues);
            Assert.AreEqual(AnalysisStrategyType.Fast, _first.Strategy);
            Assert.AreEqual(AnalysisStrategyType.Combined, _second.Strategy);
            Assert.IsFalse(_window.AppliedChanges);
        }

        [TestCase(AnalysisStrategyType.Fast)]
        [TestCase(AnalysisStrategyType.Perceptual)]
        public void MixedValues_SelectingValue_SupportsUndoAndRedo(AnalysisStrategyType selection)
        {
            _window.Draw();
            Assert.IsTrue(_window.HasMixedValues);
            Undo.IncrementCurrentGroup();

            _window.Select(selection);

            Assert.IsTrue(_window.AppliedChanges);
            Assert.AreEqual(selection, _first.Strategy);
            Assert.AreEqual(selection, _second.Strategy);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Assert.AreEqual(AnalysisStrategyType.Fast, _first.Strategy);
            Assert.AreEqual(AnalysisStrategyType.Combined, _second.Strategy);
            Undo.PerformRedo();
            Assert.AreEqual(selection, _first.Strategy);
            Assert.AreEqual(selection, _second.Strategy);
        }

        [Test]
        public void Prefab_SelectingValue_CreatesOverrideThatCanBeReverted()
        {
            _prefabPath = "Assets/_LAC_EnumEditing_" + Guid.NewGuid().ToString("N") + ".prefab";
            var source = new GameObject("Localized enum prefab");
            GameObject prefab;
            try
            {
                source.AddComponent<TextureCompressor>().Strategy = AnalysisStrategyType.Fast;
                prefab = PrefabUtility.SaveAsPrefabAsset(source, _prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }

            _prefabInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var component = _prefabInstance.GetComponent<TextureCompressor>();
            _window.SetTargets(component);
            _window.Draw();
            Assert.IsFalse(_window.Property.prefabOverride);

            _window.Select(AnalysisStrategyType.Perceptual);

            Assert.AreEqual(AnalysisStrategyType.Perceptual, component.Strategy);
            // Flush undo and refresh the serialized view to expose prefab overrides.
            Undo.FlushUndoRecordObjects();
            _window.Draw();
            Assert.IsTrue(_window.Property.prefabOverride);
            Assert.AreEqual(
                AnalysisStrategyType.Fast,
                prefab.GetComponent<TextureCompressor>().Strategy
            );
            PrefabUtility.RevertPropertyOverride(_window.Property, InteractionMode.AutomatedAction);
            _window.Draw();
            Assert.AreEqual(AnalysisStrategyType.Fast, component.Strategy);
            Assert.IsFalse(_window.Property.prefabOverride);
        }
    }

    internal sealed class LocalizedEnumTestWindow : EditorWindow
    {
        private const string ControlName = "LAC enum under test";
        private SerializedObject _serialized;
        private int _controlId;
        private int _drawCount;
        private Exception _guiException;
        internal bool HasMixedValues { get; private set; }
        internal bool AppliedChanges { get; private set; }
        internal SerializedProperty Property =>
            _serialized.FindProperty(nameof(CustomTextureCompressorPreset.Strategy));

        internal void SetTargets(params UnityEngine.Object[] targets)
        {
            _serialized?.Dispose();
            _serialized = new SerializedObject(targets);
        }

        private void OnGUI()
        {
            if (_serialized == null)
                return;
            try
            {
                _serialized.Update();
                HasMixedValues = Property.hasMultipleDifferentValues;
                GUI.SetNextControlName(ControlName);
                LocalizedEnumPopup.DrawProperty<AnalysisStrategyType>(
                    Property,
                    TextureCompressorPropertyLabels.Content(
                        nameof(CustomTextureCompressorPreset.Strategy)
                    ),
                    TextureCompressorEnumLabels.Text
                );
                GUI.FocusControl(ControlName);
                _controlId = GUIUtility.keyboardControl;
                AppliedChanges = _serialized.ApplyModifiedProperties();
                _drawCount++;
            }
            catch (Exception exception)
            {
                _guiException = exception;
            }
        }

        internal void Draw()
        {
            int before = _drawCount;
            SendEvent(new Event { type = EventType.Layout });
            SendEvent(new Event { type = EventType.Repaint });
            Assert.IsNull(_guiException);
            Assert.Greater(_drawCount, before, "The test must execute the actual IMGUI control.");
            Assert.Greater(_controlId, 0);
        }

        internal void Select(AnalysisStrategyType value)
        {
            // Inject Unity's popup callback because native menus cannot be driven in batchmode.
            var callbackType = typeof(EditorGUI).GetNestedType(
                "PopupCallbackInfo",
                BindingFlags.NonPublic
            );
            Assert.IsNotNull(callbackType);
            var instanceField = callbackType.GetField(
                "instance",
                BindingFlags.Public | BindingFlags.Static
            );
            object previous = instanceField.GetValue(null);
            try
            {
                object callback = Activator.CreateInstance(
                    callbackType,
                    new object[] { _controlId }
                );
                callbackType
                    .GetField("m_SelectedIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(
                        callback,
                        Array.IndexOf(Enum.GetValues(typeof(AnalysisStrategyType)), value)
                    );
                instanceField.SetValue(null, callback);
                AppliedChanges = false;
                SendEvent(EditorGUIUtility.CommandEvent("PopupMenuChanged"));
                Assert.IsNull(_guiException);
                Assert.IsNull(
                    instanceField.GetValue(null),
                    "The popup must consume the selection command."
                );
            }
            finally
            {
                instanceField.SetValue(null, previous);
            }
        }

        private void OnDisable()
        {
            _serialized?.Dispose();
            _serialized = null;
        }
    }
}
