using MixVerse.Midi;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MixVerse.EditorTools
{
    /// <summary>
    /// MIDI の動作確認用オブジェクトをシーンへ置くためのメニュー。
    /// </summary>
    public static class MidiTesterMenu
    {
        [MenuItem("MixVerse/Setup/Add MIDI Tester")]
        public static void AddMidiTester()
        {
            var existing = Object.FindFirstObjectByType<MidiTester>();

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[MixVerse] MidiTester は既にシーンにあります。");
                return;
            }

            var testerObject = new GameObject("MidiTester");
            testerObject.AddComponent<MidiTester>();

            Undo.RegisterCreatedObjectUndo(testerObject, "Add MIDI Tester");
            Selection.activeGameObject = testerObject;

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[MixVerse] MidiTester をシーンに追加しました。Play して DJ コントローラーを操作してください。");
        }
    }
}
