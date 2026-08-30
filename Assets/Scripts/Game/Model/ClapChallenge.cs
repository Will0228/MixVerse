using System;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// CPU が話し終わるのに合わせて拍手を求める判定。
    /// 話し終わる少し前から受け付け、話し終わってから一定時間内に拍手を始めたうえで、
    /// そこから短い制限時間のうちに規定回数そろえられなければ失敗になる。
    /// Unity にも R3 にも依存しない純粋な C# として実装し、
    /// 現在時刻（Time.time 相当の秒）は呼び出し側から受け取る。
    /// </summary>
    public sealed class ClapChallenge
    {
        /// <summary>そろえる必要のある拍手の回数。</summary>
        public const int RequiredClapCount = 7;

        /// <summary>話し終わる何秒前から拍手を数え始めるか。フライング気味の拍手を拾うための猶予。</summary>
        public const float StartGraceSeconds = 1f;

        /// <summary>話し終わってから拍手を始められる制限時間（秒）。</summary>
        public const float StartLimitSeconds = 1f;

        /// <summary>拍手を始めてから規定回数そろえるまでの制限時間（秒）。</summary>
        public const float ClapLimitSeconds = 2f;

        private float _talkEndTime;
        private float _firstClapTime;
        private bool _hasFirstClap;

        /// <summary>判定を受け付けている最中か。</summary>
        public bool IsActive { get; private set; }

        /// <summary>ここまでに数えた拍手の回数。</summary>
        public int ClapCount { get; private set; }

        /// <summary>あと何回そろえれば成功か。デバッグ表示に使う。</summary>
        public int RemainingClapCount => Math.Max(0, RequiredClapCount - ClapCount);

        /// <summary>
        /// 判定を始める。締めの音源を鳴らし始めるときに、鳴り終わる時刻を渡す。
        /// </summary>
        /// <param name="talkEndTime">締めの音源が鳴り終わる時刻（秒）。</param>
        public void Begin(float talkEndTime)
        {
            _talkEndTime = talkEndTime;
            _firstClapTime = 0f;
            _hasFirstClap = false;
            ClapCount = 0;
            IsActive = true;
        }

        /// <summary>
        /// 判定を終える。決着後や画面を離れたあとの拍手を数えないようにする。
        /// </summary>
        public void End()
        {
            IsActive = false;
        }

        /// <summary>
        /// 拍手を1回数える。受け付けていない間や、早すぎる・遅すぎる拍手は数えない。
        /// </summary>
        /// <param name="time">拍手した時刻（秒）。</param>
        /// <returns>回数として数えたか。</returns>
        public bool RegisterClap(float time)
        {
            if (!IsActive || ClapCount >= RequiredClapCount)
            {
                return false;
            }

            // まだ話の途中。猶予より前の拍手は数えない
            if (time < _talkEndTime - StartGraceSeconds)
            {
                return false;
            }

            if (!_hasFirstClap)
            {
                // 話し終わってから始めるのが遅すぎた場合は、この先いくら叩いても失敗のまま
                if (time > _talkEndTime + StartLimitSeconds)
                {
                    return false;
                }

                _hasFirstClap = true;
                _firstClapTime = time;
            }
            else if (time > _firstClapTime + ClapLimitSeconds)
            {
                return false;
            }

            ClapCount++;
            return true;
        }

        /// <summary>
        /// 現在の判定結果。まだ決まっていなければ <see cref="ClapChallengeResult.Pending"/>。
        /// 状態は変えないので、毎フレーム呼んでよい。
        /// </summary>
        /// <param name="time">判定する時刻（秒）。</param>
        public ClapChallengeResult Evaluate(float time)
        {
            if (!IsActive)
            {
                return ClapChallengeResult.Pending;
            }

            if (ClapCount >= RequiredClapCount)
            {
                return ClapChallengeResult.Success;
            }

            // 拍手を始めるまでは話し終わりから、始めたあとはその1回目からの制限時間で判定する
            return time > GetDeadline() ? ClapChallengeResult.Failure : ClapChallengeResult.Pending;
        }

        /// <summary>
        /// 制限時間の残り（秒）。デバッグ表示に使う。
        /// </summary>
        /// <param name="time">現在の時刻（秒）。</param>
        public float GetRemainingSeconds(float time)
            => IsActive ? Math.Max(0f, GetDeadline() - time) : 0f;

        private float GetDeadline()
            => _hasFirstClap ? _firstClapTime + ClapLimitSeconds : _talkEndTime + StartLimitSeconds;
    }
}
