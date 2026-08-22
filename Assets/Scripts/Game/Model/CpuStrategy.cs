using System;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// CPU がどのカードを引くかを決める。
    /// 相手の手札は裏向きで中身が分からないため、現状は一様ランダム。
    /// 思考を強くしたい場合はこのクラスだけを差し替えればよい。
    /// </summary>
    public sealed class CpuStrategy
    {
        /// <summary>
        /// 引き元の手札から選ぶ位置を返す。
        /// </summary>
        /// <param name="handCount">引き元の手札の枚数。</param>
        /// <param name="random">決定性を保つため呼び出し側から乱数器を受け取る。</param>
        public int SelectIndex(int handCount, Random random)
        {
            if (handCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(handCount), handCount, "手札が空の相手からは引けません。");
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            return random.Next(handCount);
        }
    }
}
