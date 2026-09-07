using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MixVerse.CI
{
    /// <summary>
    /// GitHub Actions から -executeMethod で呼ぶビルドエントリポイント。
    /// </summary>
    public static class CIBuild
    {
        /// GameCI (unity-builder) が -customBuildPath を渡してこなかった場合の出力先。
        const string DefaultOutputPath = "build/WebGL/MixVerse";

        const string OutputPathArg = "-customBuildPath";

        /// <summary>
        /// Addressable key を付け直してから WebGL プレイヤーをビルドする。
        /// Addressables のコンテンツは Addressables 側の "Build Addressables on Player Build" 設定に従って
        /// プレイヤービルドと一緒にビルドされる。
        /// </summary>
        public static void BuildWebGL()
        {
            try
            {
                var assign = AddressableKeyAssigner.Assign();
                Debug.Log($"[CIBuild] Addressable key: {assign.Total} 件走査 / {assign.Updated} 件更新 / " +
                          $"{assign.Duplicates.Count} 件衝突");

                var report = BuildPlayer();
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[CIBuild] WebGL ビルド失敗: {summary.result} " +
                                   $"(errors: {summary.totalErrors})");
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"[CIBuild] WebGL ビルド成功: {summary.outputPath} " +
                          $"({summary.totalSize / (1024 * 1024)} MB / {summary.totalTime})");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        static BuildReport BuildPlayer()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Build Settings に有効なシーンが 1 つもありません。");

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            // WebGL は出力先がディレクトリ。
            var outputPath = GetArg(OutputPathArg) ?? DefaultOutputPath;
            Directory.CreateDirectory(outputPath);

            Debug.Log($"[CIBuild] WebGL ビルド開始: {outputPath}\n  scenes: {string.Join(", ", scenes)}");

            return BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
