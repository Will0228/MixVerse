using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MixVerse
{
    /// <summary>
    /// トゥイーンライブラリを導入していないため、HomeView と同じ
    /// 「手書きの Lerp ループ + UniTask.Yield」方式を共通化したもの。
    /// </summary>
    public static class TweenUtility
    {
        /// <summary>
        /// Transform をワールド座標で移動させる。
        /// </summary>
        public static async UniTask MoveAsync(
            Transform target,
            Vector3 toPosition,
            float duration,
            CancellationToken token,
            bool useSmoothStep = true)
        {
            if (target == null)
            {
                return;
            }

            var fromPosition = target.position;

            await RunAsync(duration, token, t =>
            {
                var rate = useSmoothStep ? Mathf.SmoothStep(0f, 1f, t) : t;
                target.position = Vector3.LerpUnclamped(fromPosition, toPosition, rate);
            });

            target.position = toPosition;
        }

        /// <summary>
        /// Transform をローカル座標で移動・回転させる。手札の整列に使う。
        /// </summary>
        public static async UniTask MoveLocalAsync(
            Transform target,
            Vector3 toLocalPosition,
            Quaternion toLocalRotation,
            float duration,
            CancellationToken token,
            bool useSmoothStep = true)
        {
            if (target == null)
            {
                return;
            }

            var fromPosition = target.localPosition;
            var fromRotation = target.localRotation;

            await RunAsync(duration, token, t =>
            {
                var rate = useSmoothStep ? Mathf.SmoothStep(0f, 1f, t) : t;
                target.localPosition = Vector3.LerpUnclamped(fromPosition, toLocalPosition, rate);
                target.localRotation = Quaternion.SlerpUnclamped(fromRotation, toLocalRotation, rate);
            });

            target.localPosition = toLocalPosition;
            target.localRotation = toLocalRotation;
        }

        /// <summary>
        /// CanvasGroup のアルファをフェードさせる。
        /// </summary>
        public static async UniTask FadeAsync(
            CanvasGroup canvasGroup,
            float from,
            float to,
            float duration,
            CancellationToken token)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = from;

            await RunAsync(duration, token, t => canvasGroup.alpha = Mathf.Lerp(from, to, t));

            canvasGroup.alpha = to;
        }

        /// <summary>
        /// 指定秒だけ待つ。CPU の思考時間の演出などに使う。
        /// </summary>
        public static UniTask WaitAsync(float seconds, CancellationToken token)
            => UniTask.Delay(Mathf.RoundToInt(seconds * 1000f), DelayType.DeltaTime, PlayerLoopTiming.Update, token);

        /// <summary>
        /// 0→1 の進捗を毎フレーム渡す共通ループ。
        /// </summary>
        private static async UniTask RunAsync(float duration, CancellationToken token, System.Action<float> onUpdate)
        {
            if (duration <= 0f)
            {
                onUpdate(1f);
                return;
            }

            var elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                onUpdate(Mathf.Clamp01(elapsedTime / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }
    }
}
