using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MixVerse.EditorTools
{
    /// <summary>
    /// GlitchMorphShader を使った変身演出のセットアップをまとめたエディタ拡張。
    ///
    /// ・確認用に、変身元・変身先の 3D オブジェクトを 1 組作る
    /// ・CPUs.prefab の Giant → Monster Skin1 に演出を仕込む
    /// </summary>
    public static class GlitchMorphBuilder
    {
        private const string MaterialFolder = "Assets/Materials";
        private const string ShaderName = "Unlit/GlitchMorphShader";

        private const string CpusPrefabPath = "Assets/Prefabs/CPUs.prefab";
        private const string CharacterMaterialPath = MaterialFolder + "/GlitchMorphCharacterMaterial.mat";

        // CPUs.prefab の中の子オブジェクト名。Prefab 側で名前が変わっても拾えるよう候補で持つ。
        private static readonly string[] GiantObjectNames = { "Giant", "GiantPrefab" };
        private static readonly string[] MonsterObjectNames = { "Monster Skin1", "MonsterSkin1" };

        // ------------------------------------------------------------------
        // 確認用のデモ
        // ------------------------------------------------------------------

        [MenuItem("MixVerse/Setup/Create Glitch Morph Demo")]
        public static void CreateGlitchMorphDemo()
        {
            var shader = FindGlitchShader();
            if (shader == null)
            {
                return;
            }

            EnsureFolder(MaterialFolder);

            var fromMaterial = CreateMaterial(
                shader, MaterialFolder + "/GlitchMorphFromMaterial.mat",
                new Color(0.25f, 0.75f, 1f), new Color(0f, 0.9f, 1f));

            var toMaterial = CreateMaterial(
                shader, MaterialFolder + "/GlitchMorphToMaterial.mat",
                new Color(1f, 0.35f, 0.55f), new Color(1f, 0.4f, 0.1f));

            var root = new GameObject("GlitchMorphDemo");

            // 帯はワールドの高さで切られるので、2 つを同じ位置に重ねると
            // 消える帯と現れる帯がそろい、片方から片方へ乗り移ったように見える。
            var from = CreatePrimitive(PrimitiveType.Cube, "From", root.transform, fromMaterial);
            var to = CreatePrimitive(PrimitiveType.Sphere, "To", root.transform, toMaterial);
            to.transform.localScale = Vector3.one * 1.2f;

            var effect = root.AddComponent<GlitchMorphEffect>();

            var serializedEffect = new SerializedObject(effect);
            serializedEffect.FindProperty("_fromObject").objectReferenceValue = from;
            serializedEffect.FindProperty("_toObject").objectReferenceValue = to;
            serializedEffect.FindProperty("_playOnStart").boolValue = true;
            serializedEffect.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(root, "Create Glitch Morph Demo");
            Selection.activeGameObject = root;

            AssetDatabase.SaveAssets();

            Debug.Log("[MixVerse] GlitchMorphDemo を生成しました。再生すると変身演出が動きます。");
        }

        // ------------------------------------------------------------------
        // CPUs.prefab（Giant → Monster Skin1）
        // ------------------------------------------------------------------

        [MenuItem("MixVerse/Setup/Setup CPUs Glitch Morph")]
        public static void SetupCpusGlitchMorph()
        {
            var shader = FindGlitchShader();
            if (shader == null)
            {
                return;
            }

            EnsureFolder(MaterialFolder);

            // キャラクターは元のマテリアルから絵柄を引き継ぐので、ここでは
            // 基本色は白のまま、グリッチの効き方だけを決める。
            // CPUs は Scale 4 で置かれていて見た目が大きいため、帯は粗め・ズレは大きめにする。
            var characterMaterial = CreateMaterial(
                shader, CharacterMaterialPath,
                Color.white, new Color(0f, 0.9f, 1f),
                blockSize: 2.5f, glitchIntensity: 0.5f);

            var root = PrefabUtility.LoadPrefabContents(CpusPrefabPath);
            if (root == null)
            {
                Debug.LogError($"[MixVerse] {CpusPrefabPath} を読み込めませんでした。");
                return;
            }

            try
            {
                var from = FindChild(root, GiantObjectNames);
                var to = FindChild(root, MonsterObjectNames);

                if (from == null || to == null)
                {
                    Debug.LogError(
                        $"[MixVerse] {CpusPrefabPath} の中に Giant / Monster Skin1 が見つかりませんでした。");
                    return;
                }

                var effect = root.GetComponent<GlitchMorphEffect>();
                if (effect == null)
                {
                    effect = root.AddComponent<GlitchMorphEffect>();
                }

                var serializedEffect = new SerializedObject(effect);
                serializedEffect.FindProperty("_fromObject").objectReferenceValue = from;
                serializedEffect.FindProperty("_toObject").objectReferenceValue = to;
                serializedEffect.FindProperty("_glitchMaterialTemplate").objectReferenceValue = characterMaterial;

                // シーンには CPUs が 2 体並んでいる。再生時に自動で走らせると 2 体同時に変身してしまうので、
                // 再生のきっかけは GlitchMorphTester（F / K キー）やゲーム側の処理に任せる。
                serializedEffect.FindProperty("_playOnStart").boolValue = false;
                serializedEffect.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, CpusPrefabPath);

                Debug.Log(
                    $"[MixVerse] {CpusPrefabPath} に GlitchMorphEffect を設定しました（{from.name} → {to.name}）。");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            // 2 体を別々に走らせて確かめられるよう、キー入力の確認用オブジェクトも用意する
            CreateGlitchMorphTester();
        }

        // ------------------------------------------------------------------
        // 動作確認用のキー入力（F / K）
        // ------------------------------------------------------------------

        [MenuItem("MixVerse/Setup/Create Glitch Morph Tester")]
        public static void CreateGlitchMorphTester()
        {
            var effects = Object.FindObjectsByType<GlitchMorphEffect>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (effects.Length == 0)
            {
                Debug.LogWarning("[MixVerse] シーンに GlitchMorphEffect が見つかりません。先に CPUs のセットアップを実行してください。");
                return;
            }

            // 名前が同じ CPUs が並んでいるので、ヒエラルキーの並び順で 1 体目・2 体目を決める
            System.Array.Sort(effects, CompareHierarchyOrder);

            var tester = Object.FindFirstObjectByType<GlitchMorphTester>(FindObjectsInactive.Include);

            if (tester == null)
            {
                var testerObject = new GameObject("GlitchMorphTester");
                tester = testerObject.AddComponent<GlitchMorphTester>();
                Undo.RegisterCreatedObjectUndo(testerObject, "Create Glitch Morph Tester");
            }

            var keys = new[] { Key.F, Key.K };

            var serializedTester = new SerializedObject(tester);
            var bindings = serializedTester.FindProperty("_bindings");
            bindings.arraySize = keys.Length;

            for (var i = 0; i < keys.Length; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                binding.FindPropertyRelative("Key").intValue = (int)keys[i];
                binding.FindPropertyRelative("Effect").objectReferenceValue = i < effects.Length ? effects[i] : null;

                Debug.Log(
                    $"[MixVerse] {keys[i]} キー → {(i < effects.Length ? GetHierarchyPath(effects[i].transform) : "（未割り当て）")}");
            }

            serializedTester.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(tester.gameObject.scene);
            Selection.activeGameObject = tester.gameObject;
        }

        private static int CompareHierarchyOrder(GlitchMorphEffect a, GlitchMorphEffect b)
        {
            var rootOrder = a.transform.root.GetSiblingIndex().CompareTo(b.transform.root.GetSiblingIndex());
            return rootOrder != 0 ? rootOrder : a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }

        private static string GetHierarchyPath(Transform target)
        {
            var path = target.name;

            for (var parent = target.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }

        // ------------------------------------------------------------------
        // 共通処理
        // ------------------------------------------------------------------

        private static Shader FindGlitchShader()
        {
            var shader = Shader.Find(ShaderName);

            if (shader == null)
            {
                Debug.LogError($"[MixVerse] シェーダー {ShaderName} が見つかりません。");
            }

            return shader;
        }

        /// <summary>
        /// 名前の候補で子を探す。非アクティブな子（Monster Skin1）も対象にする。
        /// </summary>
        private static GameObject FindChild(GameObject root, string[] names)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root.transform)
                {
                    continue;
                }

                foreach (var name in names)
                {
                    if (child.name == name)
                    {
                        return child.gameObject;
                    }
                }
            }

            return null;
        }

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.GetComponent<Renderer>().sharedMaterial = material;

            // 演出用の見た目だけなので当たり判定は要らない
            var collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            return primitive;
        }

        private static Material CreateMaterial(
            Shader shader,
            string assetPath,
            Color baseColor,
            Color emissionColor,
            float blockSize = 12f,
            float glitchIntensity = 0.15f)
        {
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

            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_EmissionColor", emissionColor);
            material.SetFloat("_BlockSize", blockSize);
            material.SetFloat("_GlitchIntensity", glitchIntensity);

            // 実体の状態から始める
            material.SetFloat("_Progress", 0f);

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
