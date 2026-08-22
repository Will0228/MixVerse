using System.IO;
using MixVerse.Game;
using MixVerse.Game.View;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MixVerse.EditorTools
{
    /// <summary>
    /// ゲーム画面まわりの Prefab・マテリアル・シーン配置をまとめて生成するエディタ拡張。
    /// 手作業での組み立てを不要にするためのもので、何度実行しても同じ結果になる。
    /// </summary>
    public static class GameScreenPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs";
        private const string MaterialFolder = "Assets/Materials";
        private const string TextureFolder = "Assets/Textures";

        private const string CardPrefabPath = PrefabFolder + "/Card.prefab";
        private const string GameScreenPrefabPath = PrefabFolder + "/GameScreen.prefab";
        private const string ArrowSpritePath = TextureFolder + "/SelectionArrow.png";

        // カードの見た目のサイズ（トランプの縦横比に近づけている）
        private const float CardWidth = 0.7f;
        private const float CardHeight = 1.0f;

        [MenuItem("MixVerse/Setup/Create Game Prefabs")]
        public static void CreateGamePrefabs()
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);

            var faceMaterial = CreateUnlitMaterial(MaterialFolder + "/CardFaceMaterial.mat", new Color(0.96f, 0.96f, 0.96f));
            var backMaterial = CreateUnlitMaterial(MaterialFolder + "/CardBackMaterial.mat", new Color(0.15f, 0.20f, 0.45f));
            var tableMaterial = CreateUnlitMaterial(MaterialFolder + "/TableMaterial.mat", new Color(0.09f, 0.32f, 0.20f));

            var arrowSprite = CreateArrowSprite(ArrowSpritePath);

            var cardPrefab = BuildCardPrefab(faceMaterial, backMaterial);
            var gameScreenPrefab = BuildGameScreenPrefab(cardPrefab, tableMaterial, arrowSprite);

            PlaceIntoScene(gameScreenPrefab);
            SetUpCamera();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MixVerse] ゲーム画面の Prefab を生成し、シーンへ配置しました。シーンを保存してください。");
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

            // 表面はカードのローカル +Z 側、裏面は -Z 側を向くようにそろえる
            var face = CreateQuad("Face", root.transform, faceMaterial, true);
            face.transform.localScale = new Vector3(CardWidth, CardHeight, 1f);

            var back = CreateQuad("Back", root.transform, backMaterial, false);
            back.transform.localScale = new Vector3(CardWidth, CardHeight, 1f);

            // 絵柄画像が入るまでのプレースホルダ。Face の localScale を継承しないようルート直下に置く
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, 0.01f);

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
        // GameScreen.prefab
        // ------------------------------------------------------------------

        private static GameObject BuildGameScreenPrefab(CardView cardPrefab, Material tableMaterial, Sprite arrowSprite)
        {
            var root = new GameObject("GameScreen");
            var gameView = root.AddComponent<GameView>();

            // ---- 3D 盤面 ----
            var board = new GameObject("Board");
            board.transform.SetParent(root.transform, false);

            var table = CreateQuad("Table", board.transform, tableMaterial, true);
            // 法線補正を保ったまま、盤面が上（+Y）を向くように倒す
            table.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * table.transform.localRotation;
            table.transform.localScale = new Vector3(16f, 16f, 1f);

            var playerHand = CreateHand("Hand_Player", board.transform, new Vector3(0f, 0.01f, -3.4f), 0f, true, 0.5f, 7.0f);
            var cpu1Hand = CreateHand("Hand_Cpu1", board.transform, new Vector3(-3.7f, 0.01f, 1.9f), 120f, false, 0.35f, 4.5f);
            var cpu2Hand = CreateHand("Hand_Cpu2", board.transform, new Vector3(3.7f, 0.01f, 1.9f), -120f, false, 0.35f, 4.5f);

            var discardPile = new GameObject("DiscardPile");
            discardPile.transform.SetParent(board.transform, false);
            discardPile.transform.localPosition = new Vector3(0f, 0.02f, 0f);

            // ---- HUD ----
            var hud = new GameObject("Hud", typeof(RectTransform));
            hud.transform.SetParent(root.transform, false);

            var canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            hud.AddComponent<GraphicRaycaster>();

            // Content が先、FadeOverlay が後（＝手前に描画される）
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(hud.transform, false);
            Stretch(content.GetComponent<RectTransform>());
            var contentGroup = content.AddComponent<CanvasGroup>();
            contentGroup.alpha = 0f;

            var selectionArrow = CreateSelectionArrow(content.transform, arrowSprite);
            var turnLabel = CreateHudLabel("TurnLabel", content.transform, new Vector2(0f, -60f), 36f, TextAlignmentOptions.Center);
            turnLabel.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            turnLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            turnLabel.text = string.Empty;

            var resultLabel = CreateHudLabel("ResultLabel", content.transform, Vector2.zero, 72f, TextAlignmentOptions.Center);
            resultLabel.text = string.Empty;
            resultLabel.gameObject.SetActive(false);

            var fadeOverlay = new GameObject("FadeOverlay", typeof(RectTransform));
            fadeOverlay.transform.SetParent(hud.transform, false);
            Stretch(fadeOverlay.GetComponent<RectTransform>());

            var fadeImage = fadeOverlay.AddComponent<Image>();
            fadeImage.color = Color.black;
            fadeImage.raycastTarget = false;

            var fadeGroup = fadeOverlay.AddComponent<CanvasGroup>();
            fadeGroup.alpha = 1f;
            fadeGroup.blocksRaycasts = false;
            fadeGroup.interactable = false;

            // ---- 参照の結線 ----
            var serializedView = new SerializedObject(gameView);
            serializedView.FindProperty("_cardPrefab").objectReferenceValue = cardPrefab;
            serializedView.FindProperty("_discardPile").objectReferenceValue = discardPile.transform;
            serializedView.FindProperty("_canvasGroup").objectReferenceValue = contentGroup;
            serializedView.FindProperty("_fadeOverlayGroup").objectReferenceValue = fadeGroup;
            serializedView.FindProperty("_selectionArrow").objectReferenceValue = selectionArrow;
            serializedView.FindProperty("_turnLabel").objectReferenceValue = turnLabel;
            serializedView.FindProperty("_resultLabel").objectReferenceValue = resultLabel;

            var handsProperty = serializedView.FindProperty("_handViews");
            handsProperty.arraySize = 3;
            handsProperty.GetArrayElementAtIndex(0).objectReferenceValue = playerHand;
            handsProperty.GetArrayElementAtIndex(1).objectReferenceValue = cpu1Hand;
            handsProperty.GetArrayElementAtIndex(2).objectReferenceValue = cpu2Hand;

            serializedView.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, GameScreenPrefabPath);
            Object.DestroyImmediate(root);

            return saved;
        }

        private static HandView CreateHand(
            string name,
            Transform parent,
            Vector3 localPosition,
            float yRotation,
            bool isFaceUp,
            float spacing,
            float maxWidth)
        {
            var handObject = new GameObject(name);
            handObject.transform.SetParent(parent, false);
            handObject.transform.localPosition = localPosition;
            handObject.transform.localRotation = Quaternion.Euler(0f, yRotation, 0f);

            var handView = handObject.AddComponent<HandView>();

            var serialized = new SerializedObject(handView);
            serialized.FindProperty("_isFaceUp").boolValue = isFaceUp;
            serialized.FindProperty("_cardSpacing").floatValue = spacing;
            serialized.FindProperty("_maxWidth").floatValue = maxWidth;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return handView;
        }

        private static SelectionArrowView CreateSelectionArrow(Transform parent, Sprite arrowSprite)
        {
            var arrowObject = new GameObject("SelectionArrow", typeof(RectTransform));
            arrowObject.transform.SetParent(parent, false);

            var rectTransform = arrowObject.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(48f, 48f);

            var image = arrowObject.AddComponent<Image>();
            image.sprite = arrowSprite;
            image.color = new Color(1f, 0.85f, 0.2f);
            image.raycastTarget = false;

            var arrowView = arrowObject.AddComponent<SelectionArrowView>();

            var serialized = new SerializedObject(arrowView);
            serialized.FindProperty("_rectTransform").objectReferenceValue = rectTransform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            arrowObject.SetActive(false);

            return arrowView;
        }

        private static TextMeshProUGUI CreateHudLabel(
            string name,
            Transform parent,
            Vector2 anchoredPosition,
            float fontSize,
            TextAlignmentOptions alignment)
        {
            var labelObject = new GameObject(name, typeof(RectTransform));
            labelObject.transform.SetParent(parent, false);

            var label = labelObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;

            var rectTransform = label.rectTransform;
            rectTransform.sizeDelta = new Vector2(900f, 100f);
            rectTransform.anchoredPosition = anchoredPosition;

            return label;
        }

        // ------------------------------------------------------------------
        // シーンへの配置
        // ------------------------------------------------------------------

        private static void PlaceIntoScene(GameObject gameScreenPrefab)
        {
            var scene = SceneManager.GetActiveScene();

            // 既に置かれているものは作り直す（非アクティブなので Find では見つからない）
            var existingViews = Object.FindObjectsByType<GameView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var view in existingViews)
            {
                Object.DestroyImmediate(view.gameObject);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(gameScreenPrefab, scene);
            instance.name = "GameScreen";

            var gameView = instance.GetComponent<GameView>();

            // Prefab はシーン上のカメラを参照できないので、配置したインスタンス側で結線する
            var serializedView = new SerializedObject(gameView);
            serializedView.FindProperty("_boardCamera").objectReferenceValue = Camera.main;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            instance.SetActive(false);

            var lifetimeScope = Object.FindFirstObjectByType<UndisposableLifetimeScope>();
            if (lifetimeScope == null)
            {
                Debug.LogWarning("[MixVerse] UndisposableLifetimeScope が見つかりません。_gameView の結線は手動で行ってください。");
            }
            else
            {
                var serializedScope = new SerializedObject(lifetimeScope);
                serializedScope.FindProperty("_gameView").objectReferenceValue = gameView;
                serializedScope.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void SetUpCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[MixVerse] MainCamera タグのカメラが見つかりません。カメラの設定はスキップしました。");
                return;
            }

            camera.transform.position = new Vector3(0f, 7f, -6.5f);
            camera.transform.rotation = Quaternion.Euler(48f, 0f, 0f);

            // 3D のカードを EventSystem 経由でクリックできるようにする
            if (camera.GetComponent<PhysicsRaycaster>() == null)
            {
                camera.gameObject.AddComponent<PhysicsRaycaster>();
            }
        }

        // ------------------------------------------------------------------
        // 生成ヘルパー
        // ------------------------------------------------------------------

        /// <summary>
        /// Quad を作り、見える面が親のローカル +Z（faceForward が false なら -Z）を向くようにそろえる。
        /// Unity の Quad の法線の向きに依存しないよう、実際のメッシュ法線を見て判定している。
        /// </summary>
        private static GameObject CreateQuad(string name, Transform parent, Material material, bool faceForward)
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
            var needsFlip = pointsForward != faceForward;

            quad.transform.localRotation = needsFlip ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            return quad;
        }

        private static Material CreateUnlitMaterial(string assetPath, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
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

            EditorUtility.SetDirty(material);

            return material;
        }

        /// <summary>
        /// 下向きの三角形を描いたスプライトを生成する。矢印画像が用意されていないため。
        /// </summary>
        private static Sprite CreateArrowSprite(string assetPath)
        {
            const int size = 64;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                // 上（y が大きい側）ほど幅が広く、下端で頂点になる三角形
                var t = y / (float)(size - 1);
                var halfWidth = t * size * 0.5f;

                for (var x = 0; x < size; x++)
                {
                    var inside = Mathf.Abs(x - (size * 0.5f - 0.5f)) <= halfWidth;
                    pixels[(y * size) + x] = inside
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(255, 255, 255, 0);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            File.WriteAllBytes(Path.Combine(projectRoot, assetPath), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void Stretch(RectTransform rectTransform)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
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
