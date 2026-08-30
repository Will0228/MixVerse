using System;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// CPU の体力。プレイヤーが手札を捨てて CPU より少なくなったときと、
    /// JOKER がその CPU の手札に入ったときに減る。
    /// Unity にも R3 にも依存しない純粋な C# として実装し、
    /// 決定性を保つため乱数器は呼び出し側から受け取る。
    /// </summary>
    public sealed class CpuHealth
    {
        /// <summary>対局開始時の体力。</summary>
        public const int DefaultMaxHealth = 300;

        /// <summary>プレイヤーの手札が自分より少なくなったときのダメージの基準値。</summary>
        public const int HandCountDamageBase = 60;

        /// <summary>JOKER が手札に入ったときのダメージの基準値。</summary>
        public const int JokerDamageBase = 20;

        /// <summary>話し終わりに拍手を返してもらえなかったときのダメージ。ばらつきは付けない。</summary>
        public const int ClapFailureDamage = 20;

        /// <summary>トーク中の相槌（頷き）を返してもらえなかったときのダメージ。ばらつきは付けない。</summary>
        public const int NodFailureDamage = 20;

        private const double MinDamageRate = 0.5;
        private const double MaxDamageRate = 1.5;

        private int[] _values = Array.Empty<int>();

        /// <summary>体力を持っているプレイヤーの人数。プレイヤー本人の分も含む（減ることはない）。</summary>
        public int PlayerCount => _values.Length;

        /// <summary>
        /// 全員の体力を最大値に戻す。対局を始めるたびに呼ぶ。
        /// </summary>
        public void Reset(int playerCount)
        {
            if (playerCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "プレイヤー数に負の値は指定できません。");
            }

            _values = new int[playerCount];

            for (var i = 0; i < _values.Length; i++)
            {
                _values[i] = DefaultMaxHealth;
            }
        }

        public int GetHealth(int playerIndex)
        {
            ThrowIfOutOfRange(playerIndex);
            return _values[playerIndex];
        }

        /// <summary>一度でもダメージを受けたか。変身演出を出すかの判定に使う。</summary>
        public bool IsDamaged(int playerIndex) => GetHealth(playerIndex) < DefaultMaxHealth;

        /// <summary>体力が尽きたか。</summary>
        public bool IsDepleted(int playerIndex) => GetHealth(playerIndex) <= 0;

        /// <summary>
        /// ダメージを与える。減る量は基準値の 0.5～1.5 倍で、体力は 0 未満にならない。
        /// </summary>
        /// <param name="playerIndex">ダメージを受けるプレイヤー。</param>
        /// <param name="damageBase">ダメージの基準値。<see cref="HandCountDamageBase"/> などを渡す。</param>
        /// <param name="random">決定性を保つため呼び出し側から乱数器を受け取る。</param>
        /// <returns>実際に減った量。</returns>
        public int ApplyDamage(int playerIndex, int damageBase, Random random)
        {
            ThrowIfOutOfRange(playerIndex);

            if (damageBase < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(damageBase), damageBase, "ダメージの基準値に負の値は指定できません。");
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var rate = MinDamageRate + (random.NextDouble() * (MaxDamageRate - MinDamageRate));
            var amount = (int)Math.Round(damageBase * rate, MidpointRounding.AwayFromZero);

            return ApplyFixedDamage(playerIndex, amount);
        }

        /// <summary>
        /// ばらつきを付けず、ちょうど指定した量だけ体力を減らす。体力は 0 未満にならない。
        /// 拍手を返せなかったときのように、減る量が決まっているダメージに使う。
        /// </summary>
        /// <param name="playerIndex">ダメージを受けるプレイヤー。</param>
        /// <param name="amount">減らす量。<see cref="ClapFailureDamage"/> などを渡す。</param>
        /// <returns>実際に減った量。</returns>
        public int ApplyFixedDamage(int playerIndex, int amount)
        {
            ThrowIfOutOfRange(playerIndex);

            if (amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount), amount, "ダメージに負の値は指定できません。");
            }

            var before = _values[playerIndex];
            _values[playerIndex] = Math.Max(0, before - amount);

            return before - _values[playerIndex];
        }

        private void ThrowIfOutOfRange(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= _values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(playerIndex), playerIndex, "体力を持たないプレイヤーが指定されました。");
            }
        }
    }
}
