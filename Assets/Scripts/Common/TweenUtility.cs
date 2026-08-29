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
        /// Transform をワールド座標で移動・回転させる。カメラを専用位置へ動かす演出に使う。
        /// </summary>
        public static async UniTask MoveAsync(
            Transform target,
            Vector3 toPosition,
            Quaternion toRotation,
            float duration,
            CancellationToken token,
            bool useSmoothStep = true)
        {
            if (target == null)
            {
                return;
            }

            var fromPosition = target.position;
            var fromRotation = target.rotation;

            await RunAsync(duration, token, t =>
            {
                var rate = useSmoothStep ? Mathf.SmoothStep(0f, 1f, t) : t;
                target.position = Vector3.LerpUnclamped(fromPosition, toPosition, rate);
                target.rotation = Quaternion.SlerpUnclamped(fromRotation, toRotation, rate);
            });

            target.position = toPosition;
            target.rotation = toRotation;
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
        /// 放り投げるようにローカル座標で移動させる。
        /// 弧を描きながら回転し、着地時にちょうど指定の位置・向きへ収まる。
        /// </summary>
        /// <param name="arcHeight">飛行中の最大の高さ。</param>
        /// <param name="spinDegrees">
        /// 飛行中に余分に加える回転の最大量（度）。飛行の中間で最大になり、
        /// 開始時と着地時はどちらも 0 に戻るため、任意の値を入れても向きがずれない。
        /// 0 なら余分な回転なし。
        /// </param>
        public static async UniTask TossLocalAsync(
            Transform target,
            Vector3 toLocalPosition,
            Quaternion toLocalRotation,
            float arcHeight,
            float spinDegrees,
            float duration,
            CancellationToken token)
        {
            if (target == null)
            {
                return;
            }

            var fromPosition = target.localPosition;
            var fromRotation = target.localRotation;

            await RunAsync(duration, token, t =>
            {
                // 放られたものは初速が速く、落ちるにつれて水平方向の勢いが落ちる。
                // 左右対称の SmoothStep ではなく、減速のみのイージングにする。
                var eased = 1f - ((1f - t) * (1f - t));

                // 放物線。t = 0.5 で最も高くなる
                var arc = arcHeight * 4f * t * (1f - t);

                target.localPosition =
                    Vector3.LerpUnclamped(fromPosition, toLocalPosition, eased) + (Vector3.up * arc);

                // 飛行中だけ少し傾ける。sin なので開始と着地では 0 に戻り、向きがずれない。
                // 軸を X にしているのは、面の法線まわり（Z）に回すと
                // 板が高速に回転しているように見えて不自然だったため。
                var wobble = Quaternion.Euler(spinDegrees * Mathf.Sin(t * Mathf.PI), 0f, 0f);
                target.localRotation = Quaternion.SlerpUnclamped(fromRotation, toLocalRotation, eased) * wobble;
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
        /// 値を線形に補間する。マテリアルのパラメータなど、Transform 以外を動かすときに使う。
        /// </summary>
        public static async UniTask ValueAsync(
            float from,
            float to,
            float duration,
            CancellationToken token,
            System.Action<float> onUpdate)
        {
            await RunAsync(duration, token, t => onUpdate(Mathf.Lerp(from, to, t)));

            onUpdate(to);
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
