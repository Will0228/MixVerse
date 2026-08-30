using System;
using System.Collections.Generic;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// CPU が話すときに流す音源の並びを決める。
    /// talk_1 で始まり、talk_2 → talk_3 と続くか talk_3 だけかは半々で、
    /// どちらの場合も最後は必ず talk_finish_1 か talk_finish_2 で締める。
    /// Unity にも R3 にも依存しない純粋な C# として実装し、
    /// 決定性を保つため乱数器は呼び出し側から受け取る。
    /// </summary>
    public sealed class CpuTalkScript
    {
        /// <summary>
        /// 一度のトークで流す音源を、流す順に返す。末尾は必ず締めの音源になる。
        /// </summary>
        /// <param name="random">決定性を保つため呼び出し側から乱数器を受け取る。</param>
        public IReadOnlyList<CpuTalkLine> CreateSequence(Random random)
        {
            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var lines = new List<CpuTalkLine>(4) { CpuTalkLine.Talk1 };

            // talk_2 を挟むかどうかは半々。挟まない場合も talk_3 は必ず流れる
            if (random.Next(2) == 0)
            {
                lines.Add(CpuTalkLine.Talk2);
            }

            lines.Add(CpuTalkLine.Talk3);

            // 締めはどちらか一方が必ず流れる
            lines.Add(random.Next(2) == 0 ? CpuTalkLine.Finish1 : CpuTalkLine.Finish2);

            return lines;
        }

        /// <summary>
        /// 締めの音源か。これが流れ終わったところから拍手の判定が始まる。
        /// </summary>
        public static bool IsFinishLine(CpuTalkLine line)
            => line == CpuTalkLine.Finish1 || line == CpuTalkLine.Finish2;
    }
}
