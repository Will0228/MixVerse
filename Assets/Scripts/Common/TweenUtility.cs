using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MixVerse
{
    /// <summary>
    /// 補間のかかり方。
    /// </summary>
    public enum TweenEase
    {
        /// <summary>始めと終わりが緩やか。</summary>
        SmoothStep,

        /// <summary>等速。</summary>
        Linear,

        /// <summary>止まった状態から加速していく。素早く抜けていく動きに使う。</summary>
        AccelerateIn,

        /// <summary>速い状態から減速して止まる。素早く飛び込んでくる動きに使う。</summary>
        DecelerateOut,
    }

    /// <summary>
    /// トゥイーンライブラリを導入していないため、HomeView と同じ
    /// 「手書きの Lerp ループ + UniTask.Yield」方式を共通化したもの。
    /// </summary>
    public static class TweenUtility
    {
        /// <summary>
        /// Transform をワールド座標で移動させる。
        /// </summary>
        public static UniTask MoveAsync(
            Transform target,
            Vector3 toPosition,
            float duration,
            CancellationToken token,
            bool useSmoothStep = true)
            => MoveAsync(
                target, toPosition, duration,
                useSmoothStep ? TweenEase.SmoothStep : TweenEase.Linear,
                token);

        /// <summary>
        /// Transform をワールド座標で移動させる。加速・減速のかかり方を選ぶ版。
        /// </summary>
        public static async UniTask MoveAsync(
            Transform target,
            Vector3 toPosition,
            float duration,
            TweenEase ease,
            CancellationToken token)
        {
            if (target == null)
            {
                return;
            }

            var fromPosition = target.position;

            var completed = await RunAsync(target, duration, token, t =>
            {
                target.position = Vector3.LerpUnclamped(fromPosition, toPosition, Evaluate(ease, t));
            });

            if (!completed)
            {
                return;
            }

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

            var completed = await RunAsync(target, duration, token, t =>
            {
                var rate = useSmoothStep ? Mathf.SmoothStep(0f, 1f, t) : t;
                target.position = Vector3.LerpUnclamped(fromPosition, toPosition, rate);
                target.rotation = Quaternion.SlerpUnclamped(fromRotation, toRotation, rate);
            });

            if (!completed)
            {
                return;
            }

            target.position = toPosition;
            target.rotation = toRotation;
        }

        /// <summary>
        /// Transform の位置は変えずに、向きだけをワールド回転で変える。
        /// カメラの位置を固定したまま向きだけ調整する演出に使う。
        /// </summary>
        public static async UniTask RotateAsync(
            Transform target,
            Quaternion toRotation,
            float duration,
            CancellationToken token,
            bool useSmoothStep = true)
        {
            if (target == null)
            {
                return;
            }

            var fromRotation = target.rotation;

            var completed = await RunAsync(target, duration, token, t =>
            {
                var rate = useSmoothStep ? Mathf.SmoothStep(0f, 1f, t) : t;
                target.rotation = Quaternion.SlerpUnclamped(fromRotation, toRotation, rate);
            });

            if (!completed)
            {
                return;
            }

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

            var completed = await RunAsync(target, duration, token, t =>
            {
                var rate = useSmoothStep ? Mathf.SmoothStep(0f, 1f, t) : t;
                target.localPosition = Vector3.LerpUnclamped(fromPosition, toLocalPosition, rate);
                target.localRotation = Quaternion.SlerpUnclamped(fromRotation, toLocalRotation, rate);
            });

            if (!completed)
            {
                return;
            }

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

            var completed = await RunAsync(target, duration, token, t =>
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

            if (!completed)
            {
                return;
            }

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

            var completed = await RunAsync(canvasGroup, duration, token, t => canvasGroup.alpha = Mathf.Lerp(from, to, t));

            if (!completed)
            {
                return;
            }

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
        /// 0→1 の進捗にイージングをかける。
        /// </summary>
        private static float Evaluate(TweenEase ease, float t)
        {
            switch (ease)
            {
                case TweenEase.Linear:
                    return t;

                case TweenEase.AccelerateIn:
                    return t * t;

                case TweenEase.DecelerateOut:
                    return 1f - ((1f - t) * (1f - t));

                default:
                    return Mathf.SmoothStep(0f, 1f, t);
            }
        }

        /// <summary>
        /// 動かす対象を見張りながら 0→1 の進捗を毎フレーム渡す。
        /// 対象が途中で破棄されたら（次の対局の準備でカードが Destroy される場合など）
        /// そこで打ち切り、false を返す。破棄されたオブジェクトに触れて
        /// MissingReferenceException を投げると、それを await している対局進行ごと止まってしまう。
        /// </summary>
        /// <returns>最後まで進んだなら true。途中で対象が消えたなら false。</returns>
        private static async UniTask<bool> RunAsync(
            UnityEngine.Object target,
            float duration,
            CancellationToken token,
            System.Action<float> onUpdate)
        {
            if (target == null)
            {
                return false;
            }

            if (duration <= 0f)
            {
                onUpdate(1f);
                return true;
            }

            var elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                onUpdate(Mathf.Clamp01(elapsedTime / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, token);

                // Destroy はフレーム終わりに効くので、再開した次のフレームの頭で見る
                if (target == null)
                {
                    return false;
                }
            }

            return true;
        }

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
