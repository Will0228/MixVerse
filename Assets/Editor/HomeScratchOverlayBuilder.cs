using System.IO;
using MixVerse.Home;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace MixVerse.EditorTools
{
    /// <summary>
    /// 現在開いているシーンの HomeView に、スクラッチ遷移用の RawImage オーバーレイを
    /// 実際に生成して配線するエディタ拡張。
    /// マテリアル・スタンプ用テクスチャが無ければ合わせて生成する。
    /// </summary>
    public static class HomeScratchOverlayBuilder
    {
        private const string TextureFolder = "Assets/Textures";
        private const string StampTexturePath = TextureFolder + "/ScratchStampTexture.png";
        private const string BackgroundTexturePath = TextureFolder + "/ScratchBackgroundTexture.png";

        private const string MaterialFolder = "Assets/Materials";
        private const string AccumulateShaderName = "Unlit/StampAccumulateShaderURP";
        private const string DisplayShaderName = "Unlit/SimpleBlitCombineShaderURP";
        private const string AccumulateMaterialPath = MaterialFolder + "/HomeScratchAccumulateMaterial.mat";
        private const string DisplayMaterialPath = MaterialFolder + "/HomeScratchDisplayMaterial.mat";

        [MenuItem("MixVerse/Setup/Create Home Scratch Overlay")]
        public static void CreateHomeScratchOverlay()
        {
            var homeView = Object.FindFirstObjectByType<HomeView>(FindObjectsInactive.Include);
            if (homeView == null)
            {
                Debug.LogError("[MixVerse] シーン内に HomeView が見つかりません。HomeScreen を含むシーンを開いてから実行してください。");
                return;
            }

            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);

            var stampTexture = GetOrCreateStampTexture();
            var backgroundTexture = GetOrCreateBackgroundTexture();
            var accumulateMaterial = GetOrCreateMaterial(AccumulateMaterialPath, AccumulateShaderName);
            var displayMaterial = GetOrCreateMaterial(DisplayMaterialPath, DisplayShaderName);

            if (stampTexture == null || backgroundTexture == null || accumulateMaterial == null || displayMaterial == null)
            {
                Debug.LogError("[MixVerse] テクスチャ・マテリアルの生成に失敗したため、オーバーレイの作成を中止しました。");
                return;
            }

            var homeBackground = FindHomeBackground(homeView.transform);

            var overlayRoot = BuildOverlayHierarchy(homeView.transform, out var resultImage, out var combiner);

            // 実行時は演出開始時のスクショが流し込まれる。エディタ上で中身が空だと分かりづらいので、
            // 見た目の近い背景をプレビュー代わりに入れておくだけ。
            if (homeBackground != null)
            {
                resultImage.texture = homeBackground.mainTexture;
            }

            // Home の中身すべてを覆う位置に置く。演出中は覆われる側（ボタンやテキスト）を
            // HomeView が非表示にするので、UI が二重に見えることはない。
            overlayRoot.transform.SetAsLastSibling();

            var combinerSerialized = new SerializedObject(combiner);
            combinerSerialized.FindProperty("_backgroundTexture").objectReferenceValue = backgroundTexture;
            combinerSerialized.FindProperty("_stampTexture").objectReferenceValue = stampTexture;
            combinerSerialized.FindProperty("_accumulateMaterial").objectReferenceValue = accumulateMaterial;
            combinerSerialized.FindProperty("_displayMaterial").objectReferenceValue = displayMaterial;
            combinerSerialized.FindProperty("_resultImage").objectReferenceValue = resultImage;
            combinerSerialized.ApplyModifiedPropertiesWithoutUndo();

            var homeViewSerialized = new SerializedObject(homeView);
            homeViewSerialized.FindProperty("_scratchOverlayRoot").objectReferenceValue = overlayRoot;
            homeViewSerialized.FindProperty("_scratchCombiner").objectReferenceValue = combiner;
            homeViewSerialized.ApplyModifiedPropertiesWithoutUndo();

            overlayRoot.SetActive(false);

            EditorUtility.SetDirty(homeView);
            EditorSceneManager.MarkSceneDirty(homeView.gameObject.scene);

            Debug.Log("[MixVerse] ScratchOverlay を生成し、HomeView に配線しました。");
        }

        // ------------------------------------------------------------------
        // 階層生成
        // ------------------------------------------------------------------

        /// <summary>
        /// Home 直下の全画面 Image（背景）を探す。ボタンなどの操作要素は除く。
        /// </summary>
        private static Graphic FindHomeBackground(Transform homeRoot)
        {
            for (var i = 0; i < homeRoot.childCount; i++)
            {
                var child = homeRoot.GetChild(i);

                if (child.name == "ScratchOverlay")
                {
                    continue;
                }

                var image = child.GetComponent<Image>();
                if (image == null || child.GetComponent<Selectable>() != null)
                {
                    continue;
                }

                // アンカーが四辺に張り付いている＝全画面に広がる背景とみなす
                var rectTransform = (RectTransform)child;
                if (rectTransform.anchorMin == Vector2.zero && rectTransform.anchorMax == Vector2.one)
                {
                    return image;
                }
            }

            return null;
        }

        private static GameObject BuildOverlayHierarchy(Transform parent, out RawImage resultImage, out SimpleBlitCombiner combiner)
        {
            var existing = parent.Find("ScratchOverlay");
            var overlayObject = existing != null ? existing.gameObject : new GameObject("ScratchOverlay");

            overlayObject.layer = parent.gameObject.layer; // UI レイヤーにそろえる

            var rectTransform = overlayObject.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                rectTransform = overlayObject.AddComponent<RectTransform>();
            }

            overlayObject.transform.SetParent(parent, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            resultImage = overlayObject.GetComponent<RawImage>();
            if (resultImage == null)
            {
                resultImage = overlayObject.AddComponent<RawImage>();
            }

            resultImage.raycastTarget = false; // 演出専用。ボタン操作を邪魔しない

            combiner = overlayObject.GetComponent<SimpleBlitCombiner>();
            if (combiner == null)
            {
                combiner = overlayObject.AddComponent<SimpleBlitCombiner>();
            }

            return overlayObject;
        }

        // ------------------------------------------------------------------
        // マテリアル
        // ------------------------------------------------------------------

        private static Material GetOrCreateMaterial(string assetPath, string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[MixVerse] シェーダー {shaderName} が見つかりません。");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.shader = shader;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------
        // テクスチャ（中心が赤、フチが緑のスタンプ／真っ黒な初期背景）
        // ------------------------------------------------------------------

        private static Texture2D GetOrCreateStampTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(StampTexturePath);
            if (existing != null)
            {
                return existing;
            }

            const int size = 128;
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                    var d = Vector2.Distance(uv, new Vector2(0.5f, 0.5f));

                    // 中心付近は赤（削れ部分）、その外側のリングを緑（フチ）にする
                    var red = 1f - SmoothStep01(0.26f, 0.36f, d);
                    var ring = SmoothStep01(0.28f, 0.38f, d) * (1f - SmoothStep01(0.42f, 0.5f, d));

                    pixels[y * size + x] = new Color(red, ring, 0f, 1f);
                }
            }

            return SaveTexture(pixels, size, size, StampTexturePath);
        }

        private static Texture2D GetOrCreateBackgroundTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundTexturePath);
            if (existing != null)
            {
                return existing;
            }

            const int size = 8;
            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 255);
            }

            return SaveTexture(pixels, size, size, BackgroundTexturePath);
        }

        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static Texture2D SaveTexture(Color32[] pixels, int width, int height, string assetPath)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            var leaf = Path.GetFileName(folderPath);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
