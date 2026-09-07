using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if MIXVERSE_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace MixVerse.CI
{
    /// <summary>
    /// 対象フォルダ配下のアセットに "親フォルダ名_アセット名" という Addressable key を付与する。
    /// 例) Assets/Sound/talk_1.wav -> Sound_talk_1
    /// </summary>
    public static class AddressableKeyAssigner
    {
        /// key を付与する対象フォルダ。サブフォルダも再帰的に走査する。
        /// CI からは -addressableTargetFolders "Assets/A,Assets/B" で上書きできる。
        static readonly string[] DefaultTargetFolders =
        {
            "Assets/Data",
            "Assets/Materials",
            "Assets/Prefabs",
            "Assets/Shaders",
            "Assets/Sound",
            "Assets/Textures",
        };

        /// Addressable にしても意味の無いアセット。
        static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".asmdef", ".asmref", ".dll", ".meta", ".rsp", ".preset",
        };

        const string TargetFoldersArg = "-addressableTargetFolders";

        public sealed class Result
        {
            /// 走査したアセット数。
            public int Total;
            /// key を新しく付け替えたアセット数。
            public int Updated;
            /// key が衝突して付与できなかったアセット。
            public readonly List<string> Duplicates = new List<string>();
        }

        [MenuItem("Tools/MixVerse/Assign Addressable Keys")]
        public static void AssignFromMenu()
        {
            var result = Assign(DefaultTargetFolders);
            Debug.Log($"[AddressableKeyAssigner] {result.Total} 件走査 / {result.Updated} 件更新 / " +
                      $"{result.Duplicates.Count} 件 key 衝突");
        }

        /// <summary>
        /// CI から -executeMethod MixVerse.CI.AddressableKeyAssigner.Run で単体実行するためのエントリポイント。
        /// key が衝突していたら exit code 1 で終了する。
        /// </summary>
        public static void Run()
        {
            try
            {
                var result = Assign();
                Debug.Log($"[AddressableKeyAssigner] {result.Total} 件走査 / {result.Updated} 件更新");
                EditorApplication.Exit(result.Duplicates.Count > 0 ? 1 : 0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 既定の対象フォルダ (-addressableTargetFolders 指定があればそちら) に key を付与する。
        /// </summary>
        public static Result Assign() => Assign(ParseTargetFoldersArg() ?? DefaultTargetFolders);

        /// <summary>
        /// targetFolders 配下のアセットに key を付与し、AddressableAssetSettings を保存する。
        /// </summary>
        public static Result Assign(IReadOnlyList<string> targetFolders)
        {
#if !MIXVERSE_ADDRESSABLES
            throw new InvalidOperationException(
                "com.unity.addressables が見つかりません。Packages/manifest.json を確認してください。");
#else
            var result = new Result();
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
                throw new InvalidOperationException("AddressableAssetSettings を取得できませんでした。");

            var group = settings.DefaultGroup;
            if (group == null)
                throw new InvalidOperationException("AddressableAssetSettings に DefaultGroup がありません。");

            var folders = targetFolders.Where(AssetDatabase.IsValidFolder).ToArray();
            foreach (var missing in targetFolders.Where(f => !AssetDatabase.IsValidFolder(f)))
                Debug.LogWarning($"[AddressableKeyAssigner] 対象フォルダが見つかりません: {missing}");
            if (folders.Length == 0)
                return result;

            // key -> 先に確保したアセットパス。同じ key を二重に付けないための重複検出に使う。
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var path in AssetDatabase.FindAssets(string.Empty, folders)
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => !string.IsNullOrEmpty(p))
                         .Distinct()
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (IgnoredExtensions.Contains(Path.GetExtension(path))) continue;

                result.Total++;
                var key = BuildKey(path);

                if (owners.TryGetValue(key, out var owner))
                {
                    result.Duplicates.Add(key);
                    Debug.LogError($"[AddressableKeyAssigner] key が衝突しています: \"{key}\" -> {owner} / {path}");
                    continue;
                }
                owners.Add(key, path);

                var guid = AssetDatabase.AssetPathToGUID(path);
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                if (entry == null)
                {
                    Debug.LogWarning($"[AddressableKeyAssigner] エントリを作成できませんでした: {path}");
                    continue;
                }
                if (entry.address == key) continue;

                entry.SetAddress(key, false);
                result.Updated++;
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            return result;
#endif
        }

        /// "Assets/Sound/talk_1.wav" -> "Sound_talk_1"
        static string BuildKey(string assetPath)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(assetPath));
            var name = Path.GetFileNameWithoutExtension(assetPath);
            return string.IsNullOrEmpty(folder) ? name : $"{folder}_{name}";
        }

        static string[] ParseTargetFoldersArg()
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != TargetFoldersArg) continue;
                return args[i + 1]
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().TrimEnd('/'))
                    .Where(s => s.Length > 0)
                    .ToArray();
            }
            return null;
        }
    }
}
