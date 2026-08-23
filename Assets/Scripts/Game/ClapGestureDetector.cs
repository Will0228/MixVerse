namespace MixVerse.Game
{
    /// <summary>
    /// ジョグの回転方向から、拍手する手を閉じる（時計回り）か開く（反時計回り）かを判定する。
    /// 同じ方向への回転が続く間はムダに再生させないよう、状態が変わったときだけ通知する。
    /// </summary>
    public sealed class ClapGestureDetector
    {
        private bool? _isClosed;

        /// <summary>
        /// ジョグの1ステップを記録する。
        /// 手を閉じる/開くべき状態が直前から変わったときだけ true を返し、目標の状態を isClosed に返す。
        /// </summary>
        public bool RegisterStep(int step, out bool isClosed)
        {
            // +1（右・時計回り）で手を合わせ、-1（左・反時計回り）で放す
            isClosed = step > 0;

            if (_isClosed == isClosed)
            {
                return false;
            }

            _isClosed = isClosed;
            return true;
        }

        /// <summary>
        /// 手を出し入れしたときなど、直前の状態をリセットする。
        /// </summary>
        public void Reset()
        {
            _isClosed = null;
        }
    }
}
