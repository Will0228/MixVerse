using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MixVerse.Game.View
{
    /// <summary>
    /// CUE ボタンで呼び出す、拍手する両手。
    /// スクラッチの回転方向に合わせて、外部（GamePresenter）から SetHandsClosed を呼んでもらう。
    /// 時計回りで合わせ、反時計回りで放す。
    /// 見た目の姿勢はすべて GameScreenPrefabBuilder が焼き込んだ値を使うため、実行時の計算はしない。
    /// </summary>
    public sealed class ClapHandsView : MonoBehaviour
    {
        [SerializeField] private Transform _leftHand;
        [SerializeField] private Transform _rightHand;

        [Header("Pose (Local Position)")]
        [SerializeField] private Vector3 _leftHomeLocalPosition;
        [SerializeField] private Vector3 _rightHomeLocalPosition;
        [SerializeField] private Vector3 _leftClosedLocalPosition;
        [SerializeField] private Vector3 _rightClosedLocalPosition;

        [Header("Timing")]
        [SerializeField] private float _clapInDuration = 0.08f;
        [SerializeField] private float _clapOutDuration = 0.12f;

        private CancellationTokenSource _clapCts;

        public bool IsVisible => gameObject.activeSelf;

        public void Show()
        {
            gameObject.SetActive(true);
            ResetPose();
        }

        public void Hide()
        {
            CancelClap();
            gameObject.SetActive(false);
        }

        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }

        /// <summary>
        /// 手を閉じる（叩き合わせる）か開くかを切り替える。連打されたら直前の分を打ち切って最新の状態を優先する。
        /// </summary>
        public void SetHandsClosed(bool isClosed)
        {
            CancelClap();

            _clapCts = new CancellationTokenSource();
            MoveToPoseAsync(isClosed, _clapCts.Token).Forget();
        }

        private void ResetPose()
        {
            CancelClap();

            if (_leftHand != null)
            {
                _leftHand.localPosition = _leftHomeLocalPosition;
            }

            if (_rightHand != null)
            {
                _rightHand.localPosition = _rightHomeLocalPosition;
            }
        }

        private void CancelClap()
        {
            if (_clapCts == null)
            {
                return;
            }

            _clapCts.Cancel();
            _clapCts.Dispose();
            _clapCts = null;
        }

        private async UniTaskVoid MoveToPoseAsync(bool isClosed, CancellationToken token)
        {
            try
            {
                if (isClosed)
                {
                    // 打ち合わせる瞬間（時計回り）は素早く
                    await UniTask.WhenAll(
                        TweenUtility.MoveLocalAsync(_leftHand, _leftClosedLocalPosition, _leftHand.localRotation, _clapInDuration, token, useSmoothStep: false),
                        TweenUtility.MoveLocalAsync(_rightHand, _rightClosedLocalPosition, _rightHand.localRotation, _clapInDuration, token, useSmoothStep: false));
                }
                else
                {
                    // 放す（反時計回り）は少し柔らかく
                    await UniTask.WhenAll(
                        TweenUtility.MoveLocalAsync(_leftHand, _leftHomeLocalPosition, _leftHand.localRotation, _clapOutDuration, token),
                        TweenUtility.MoveLocalAsync(_rightHand, _rightHomeLocalPosition, _rightHand.localRotation, _clapOutDuration, token));
                }
            }
            catch (OperationCanceledException)
            {
                // 次の切り替えや非表示に割り込まれた場合はここに来る
            }
        }

        private void OnDestroy()
        {
            CancelClap();
        }
    }
}
