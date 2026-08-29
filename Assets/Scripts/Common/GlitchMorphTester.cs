using UnityEngine;
using UnityEngine.InputSystem;

namespace MixVerse
{
    /// <summary>
    /// キー入力で GlitchMorphEffect を 1 体ずつ再生する動作確認用のコンポーネント。
    /// 既定では F キーで 1 体目、K キーで 2 体目の CPU が変身する。
    ///
    /// 演出そのものは GlitchMorphEffect 側がキャラクターごとに完結しているので、
    /// ここで対象を選んで Play を呼べば、その 1 体だけが乱れる。
    /// </summary>
    public sealed class GlitchMorphTester : MonoBehaviour
    {
        [System.Serializable]
        public struct KeyBinding
        {
            public Key Key;
            public GlitchMorphEffect Effect;
        }

        [SerializeField]
        private KeyBinding[] _bindings =
        {
            new KeyBinding { Key = Key.F },
            new KeyBinding { Key = Key.K },
        };

        [Tooltip("Effect が空の枠を、シーン内の GlitchMorphEffect でヒエラルキー順に埋める。")]
        [SerializeField] private bool _autoAssignFromScene = true;

        private void Awake()
        {
            if (_autoAssignFromScene)
            {
                AutoAssign();
            }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || _bindings == null)
            {
                return;
            }

            foreach (var binding in _bindings)
            {
                if (binding.Effect == null || binding.Key == Key.None)
                {
                    continue;
                }

                if (keyboard[binding.Key].wasPressedThisFrame)
                {
                    Debug.Log($"[MixVerse] {binding.Key} → {binding.Effect.name} の変身を再生します。");
                    binding.Effect.Play();
                }
            }
        }

        /// <summary>
        /// 空いている枠にシーン内の GlitchMorphEffect を順番に割り当てる。
        /// 名前が同じインスタンスが並んでいても迷わないよう、ヒエラルキーの並び順にそろえる。
        /// </summary>
        private void AutoAssign()
        {
            if (_bindings == null || _bindings.Length == 0)
            {
                return;
            }

            var effects = FindObjectsByType<GlitchMorphEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            System.Array.Sort(effects, CompareHierarchyOrder);

            var effectIndex = 0;

            for (var i = 0; i < _bindings.Length && effectIndex < effects.Length; i++)
            {
                if (_bindings[i].Effect != null)
                {
                    continue;
                }

                _bindings[i].Effect = effects[effectIndex];
                effectIndex++;
            }
        }

        private static int CompareHierarchyOrder(GlitchMorphEffect a, GlitchMorphEffect b)
        {
            var rootOrder = a.transform.root.GetSiblingIndex().CompareTo(b.transform.root.GetSiblingIndex());
            return rootOrder != 0 ? rootOrder : a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
        }
    }
}
