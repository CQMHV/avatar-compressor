using System;
using System.Collections.Generic;
using System.Linq;
using dev.limitex.avatar.compressor.editor.texture;
using dev.limitex.avatar.compressor.editor.texture.integrations;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace dev.limitex.avatar.compressor.tests
{
    [TestFixture]
    internal class OutlineTextureReuseTests
    {
        private Material _material;
        private Texture2D _source;
        private Texture2D _baked;

        [SetUp]
        public void SetUp()
        {
            _material = new Material(Shader.Find("Hidden/LAC/Tests/LilToonBake"));
            _source = new Texture2D(4, 4);
            _baked = new Texture2D(4, 4);
            _material.SetTexture("_MainTex", _baked);
            _material.SetTexture("_OutlineTex", _source);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_material);
            UnityEngine.Object.DestroyImmediate(_source);
            UnityEngine.Object.DestroyImmediate(_baked);
        }

        private bool Reuse(
            IReadOnlyCollection<string> animated = null,
            Func<Texture2D, string, bool> canReplace = null,
            Func<Texture2D, bool> isProtected = null
        )
        {
            return LilToonTextureBaker.TryReuseBakedMainTextureForOutline(
                _material,
                _source,
                _source,
                animated ?? Array.Empty<string>(),
                canReplace,
                isProtected
            );
        }

        [Test]
        public void Reuse_PreservesOutlineToneAndOtherConsumersOfSource()
        {
            var tone = new Vector4(0.1f, 0.8f, 0.9f, 1f);
            _material.SetVector("_OutlineTexHSVG", tone);
            _material.SetTexture("_Main2ndTex", _source);

            Assert.IsTrue(Reuse());
            Assert.AreSame(_baked, _material.GetTexture("_OutlineTex"));
            Assert.AreEqual(tone, _material.GetVector("_OutlineTexHSVG"));
            Assert.AreSame(_source, _material.GetTexture("_Main2ndTex"));
        }

        [Test]
        public void Reuse_WithoutMainBake_DoesNotChangeOutline()
        {
            _material.SetTexture("_MainTex", _source);
            Assert.IsFalse(Reuse());
            Assert.AreSame(_source, _material.GetTexture("_OutlineTex"));
        }

        [Test]
        public void Reuse_DedicatedOutline_IsNotReplaced()
        {
            _material.SetTexture("_OutlineTex", _baked);
            Assert.IsFalse(
                LilToonTextureBaker.TryReuseBakedMainTextureForOutline(
                    _material,
                    _source,
                    _baked,
                    Array.Empty<string>(),
                    null,
                    null
                )
            );
            Assert.AreSame(_baked, _material.GetTexture("_OutlineTex"));
        }

        [TestCase("_MainTex")]
        [TestCase("_OutlineTex")]
        [TestCase("_OutlineTexHSVG")]
        [TestCase("_MainTex_ST")]
        [TestCase("_MainTex_ScrollRotate")]
        [TestCase("_OutlineTex_ST")]
        [TestCase("_OutlineTex_ScrollRotate")]
        public void Reuse_AnimatedBinding_PreservesOutline(string property)
        {
            Assert.IsFalse(Reuse(new[] { property }));
            Assert.AreSame(_source, _material.GetTexture("_OutlineTex"));
        }

        [Test]
        public void Reuse_ProtectedSource_PreservesOutline()
        {
            Assert.IsFalse(Reuse(isProtected: t => t == _source));
            Assert.AreSame(_source, _material.GetTexture("_OutlineTex"));
        }

        [Test]
        public void Reuse_OutlineFilteredOut_PreservesOutline()
        {
            Assert.IsFalse(Reuse(canReplace: (t, p) => p != "_OutlineTex"));
            Assert.AreSame(_source, _material.GetTexture("_OutlineTex"));
        }

        [Test]
        public void Reuse_DifferentTiling_PreservesOutline()
        {
            _material.SetTextureScale("_OutlineTex", new Vector2(2f, 1f));
            Assert.IsFalse(Reuse());
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Bake_OptionUsesFinalMainIncludingAlphaBake(bool reuse, bool alphaMask)
        {
            var shader = Shader.Find("Hidden/lilToonTransparentOutline");
            if (shader == null)
                Assert.Ignore("lilToon is not installed.");
            var baker = new LilToonTextureBaker(reuse);
            if (!baker.IsAvailable)
                Assert.Ignore("lilToon baker requires a supported version and graphics device.");

            var material = new Material(shader);
            var original = new Material(material);
            try
            {
                _source.SetPixels(
                    new[]
                    {
                        Color.red,
                        Color.green,
                        Color.blue,
                        Color.white,
                        Color.red,
                        Color.green,
                        Color.blue,
                        Color.white,
                        Color.red,
                        Color.green,
                        Color.blue,
                        Color.white,
                        Color.red,
                        Color.green,
                        Color.blue,
                        Color.white,
                    }
                );
                _source.Apply();
                material.SetTexture("_MainTex", _source);
                material.SetTexture("_OutlineTex", _source);
                material.SetTexture("_BacklightColorTex", _source);
                material.SetVector("_MainTexHSVG", new Vector4(0f, 0.7f, 0.8f, 1f));
                var outlineTone = reuse
                    ? new Vector4(0.1f, 0.8f, 0.9f, 1f)
                    : new Vector4(0f, 1f, 1f, 1f);
                material.SetVector("_OutlineTexHSVG", outlineTone);
                if (alphaMask)
                {
                    material.SetFloat("_AlphaMaskMode", 1f);
                    material.SetTexture("_AlphaMask", _source);
                }
                original.CopyPropertiesFromMaterial(material);
                baker.Bake(material, Array.Empty<string>(), null, null);

                Assert.AreNotSame(_source, material.GetTexture("_MainTex"));
                Assert.AreSame(
                    reuse ? material.GetTexture("_MainTex") : _source,
                    material.GetTexture("_OutlineTex")
                );
                Assert.AreSame(_source, material.GetTexture("_BacklightColorTex"));
                Assert.AreSame(_source, original.GetTexture("_MainTex"));
                Assert.AreSame(_source, original.GetTexture("_OutlineTex"));
                Assert.AreEqual(outlineTone, material.GetVector("_OutlineTexHSVG"));
                if (alphaMask)
                    Assert.AreEqual(0f, material.GetFloat("_AlphaMaskMode"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(original);
                baker.DestroyOrphanedBakes(Array.Empty<Material>());
            }
        }

        [Test]
        public void Compress_ClonesMaterialAndKeepsOneLiveMainOutlineTexture()
        {
            var shader = Shader.Find("Hidden/lilToonOutline");
            if (shader == null)
                Assert.Ignore("lilToon is not installed.");
            var baker = new LilToonTextureBaker(true);
            if (!baker.IsAvailable)
                Assert.Ignore("lilToon baker requires a supported version and graphics device.");

            var root = new GameObject("OutlineReuseTest");
            var material = new Material(shader);
            var generated = new HashSet<UnityEngine.Object>();
            string folder = "Assets/_LAC_OUTLINE_REUSE_" + Guid.NewGuid().ToString("N");
            using var registryScope = new ObjectRegistryScope(new ObjectRegistry(null));
            try
            {
                AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
                AssetDatabase.CreateAsset(_source, folder + "/Source.asset");
                var config = root.AddComponent<TextureCompressor>();
                config.MinSourceSize = 1;
                config.SkipIfSmallerThan = 0;
                config.MinResolution = 4;
                config.MaxResolution = 4;
                config.MinDivisor = 1;
                config.BakeLilToonTextures = true;
                config.TargetPlatform = CompressionPlatform.Desktop;
                material.SetTexture("_MainTex", _source);
                material.SetTexture("_OutlineTex", _source);
                material.SetVector("_MainTexHSVG", new Vector4(0f, 0.7f, 0.8f, 1f));
                var renderer = root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var service = new TextureCompressorService(
                    config,
                    AnalysisBackendPreference.CPU,
                    ResizeBackendPreference.CPU,
                    animationUsageMap: AnimationUsageMap.Empty,
                    lilToonBaker: baker
                );
                var (textures, materials) = service.CompressWithMappings(
                    MaterialCollector.CollectFromRenderers(root),
                    false
                );
                foreach (var t in textures.Values)
                    generated.Add(t);
                foreach (var m in materials.Values)
                    generated.Add(m);

                var clone = renderer.sharedMaterial;
                Assert.AreNotSame(material, clone);
                Assert.AreEqual(
                    _source.GetInstanceID(),
                    material.GetTexture("_MainTex").GetInstanceID()
                );
                Assert.AreEqual(
                    _source.GetInstanceID(),
                    material.GetTexture("_OutlineTex").GetInstanceID()
                );
                Assert.AreNotSame(_source, clone.GetTexture("_MainTex"));
                Assert.AreSame(clone.GetTexture("_MainTex"), clone.GetTexture("_OutlineTex"));
                Assert.IsTrue(textures.Values.Contains(clone.GetTexture("_MainTex")));
                baker.DestroyOrphanedBakes(materials.Values);
                Assert.IsTrue(clone.GetTexture("_OutlineTex") != null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var obj in generated)
                    if (obj != null)
                        UnityEngine.Object.DestroyImmediate(obj);
                UnityEngine.Object.DestroyImmediate(material);
                baker.DestroyOrphanedBakes(Array.Empty<Material>());
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
