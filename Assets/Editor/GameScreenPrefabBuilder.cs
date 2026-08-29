using System.IO;
using MixVerse.Game.View;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MixVerse.EditorTools
{
    /// <summary>
    /// Card Prefab・マテリアルを生成するエディタ拡張。
    /// GameScreen.prefab は手作業で組み立てて直接編集する方針のため、ここでは扱わない。
    /// </summary>
    public static class GameScreenPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string MaterialFolder = "Assets/Materials";

        private const string CardPrefabPath = PrefabFolder + "/Card.prefab";

        // カードはディゾルブ演出のため専用シェーダーで描く
        private const string DissolveShaderName = "Unlit/CardDissolveShader";
        private const string NoiseTexturePath = "Assets/Textures/noise (2).jpg";

        // カードの見た目のサイズ（トランプの縦横比に近づけている）
        private const float CardWidth = 0.7f;
        private const float CardHeight = 1.0f;

        [MenuItem("MixVerse/Setup/Create Game Prefabs")]
        public static void CreateGamePrefabs()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);

            var faceMaterial = CreateCardMaterial(MaterialFolder + "/CardFaceMaterial.mat", new Color(0.96f, 0.96f, 0.96f));
            var backMaterial = CreateCardMaterial(MaterialFolder + "/CardBackMaterial.mat", new Color(0.15f, 0.20f, 0.45f));

            BuildCardPrefab(faceMaterial, backMaterial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MixVerse] Card Prefab を生成しました。");
        }

        // ------------------------------------------------------------------
        // Card.prefab
        // ------------------------------------------------------------------

        private static CardView BuildCardPrefab(Material faceMaterial, Material backMaterial)
        {
            var root = new GameObject("Card");

            var cardView = root.AddComponent<CardView>();

            var boxCollider = root.AddComponent<BoxCollider>();
            boxCollider.size = new Vector3(CardWidth, CardHeight, 0.05f);

            // 表面はカードのローカル +Z 側、裏面は -Z 側。
            var face = CreateQuad("Face", root.transform, faceMaterial, true);
            face.transform.localScale = new Vector3(CardWidth, CardHeight, 1f);
            face.transform.localPosition = new Vector3(0f, 0f, 0.001f);

            var back = CreateQuad("Back", root.transform, backMaterial, false);
            back.transform.localScale = new Vector3(CardWidth, CardHeight, 1f);
            back.transform.localPosition = new Vector3(0f, 0f, -0.001f);

            // 絵柄画像が入るまでのプレースホルダ。Face の localScale を継承しないようルート直下に置く。
            //
            // TextMeshPro はローカル -Z 側から読める向きに描画される。
            // 表面は +Z 側にあるので、そのまま置くと読み手と反対を向いて文字が鏡像になる。
            // Y 軸に 180 度回して、表面と同じ側から読めるようにする。
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            labelObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var label = labelObject.AddComponent<TextMeshPro>();
            label.text = "AS";
            label.fontSize = 3.5f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = new Color(0.12f, 0.12f, 0.14f);
            label.rectTransform.sizeDelta = new Vector2(CardWidth, CardHeight);

            var serializedCard = new SerializedObject(cardView);
            serializedCard.FindProperty("_faceRenderer").objectReferenceValue = face.GetComponent<MeshRenderer>();
            serializedCard.FindProperty("_backRenderer").objectReferenceValue = back.GetComponent<MeshRenderer>();
            serializedCard.FindProperty("_faceLabel").objectReferenceValue = label;
            serializedCard.FindProperty("_collider").objectReferenceValue = boxCollider;
            serializedCard.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
            Object.DestroyImmediate(root);

            return saved.GetComponent<CardView>();
        }

        // ------------------------------------------------------------------
        // 生成ヘルパー
        // ------------------------------------------------------------------

        /// <summary>
        /// Quad を作り、見える面がローカル +Z（visibleAlongPositiveZ が false なら -Z）を向くようにそろえる。
        /// Unity の Quad の法線の向きに依存しないよう、実際のメッシュ法線を見て判定している。
        /// </summary>
        private static GameObject CreateQuad(string name, Transform parent, Material material, bool visibleAlongPositiveZ)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;

            var meshCollider = quad.GetComponent<MeshCollider>();
            if (meshCollider != null)
            {
                Object.DestroyImmediate(meshCollider);
            }

            quad.transform.SetParent(parent, false);

            var mesh = quad.GetComponent<MeshFilter>().sharedMesh;
            var normals = mesh != null ? mesh.normals : null;
            var normal = normals != null && normals.Length > 0 ? normals[0] : Vector3.back;

            var pointsForward = Vector3.Dot(normal, Vector3.forward) > 0f;
            var needsFlip = pointsForward != visibleAlongPositiveZ;

            quad.transform.localRotation = needsFlip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            return quad;
        }

        /// <summary>
        /// カード用のマテリアルを作る。ディゾルブ量は CardView が
        /// MaterialPropertyBlock でカードごとに上書きするので、ここでは 0（実体）のままにしておく。
        /// </summary>
        private static Material CreateCardMaterial(string assetPath, Color color)
        {
            var shader = Shader.Find(DissolveShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[MixVerse] {DissolveShaderName} が見つかりません。ディゾルブなしの Unlit で作ります。");
                shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
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

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            // シェーダー既定値は 0.5（半分溶けた状態）なので、実体の 0 に戻しておく
            if (material.HasProperty("_Threshold"))
            {
                material.SetFloat("_Threshold", 0f);
            }

            if (material.HasProperty("_NoiseTex"))
            {
                var noiseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NoiseTexturePath);
                if (noiseTexture == null)
                {
                    Debug.LogWarning($"[MixVerse] ノイズテクスチャ {NoiseTexturePath} が見つかりません。ディゾルブの模様が出ません。");
                }

                material.SetTexture("_NoiseTex", noiseTexture);
            }

            EditorUtility.SetDirty(material);

            return material;
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
