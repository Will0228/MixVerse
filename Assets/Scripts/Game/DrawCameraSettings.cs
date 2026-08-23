using UnityEngine;

namespace MixVerse.Game
{
    /// <summary>
    /// 相手の手札を引くときのカメラ演出のタイミングを持つ設定データ。
    /// カメラの位置・向きはシーン依存のため HandView 側の Transform 参照で持ち、
    /// ここでは使い回せるチューニング値だけを扱う。
    /// </summary>
    [CreateAssetMenu(fileName = "DrawCameraSettings", menuName = "MixVerse/Draw Camera Settings")]
    public sealed class DrawCameraSettings : ScriptableObject
    {
        [SerializeField] private float _transitionDuration = 0.5f;

        /// <summary>カメラが専用位置へ移動・復帰する所要時間。手札が向きを変える時間も兼ねる。</summary>
        public float TransitionDuration => _transitionDuration;
    }
}
