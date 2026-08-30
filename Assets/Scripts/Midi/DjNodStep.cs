namespace MixVerse.Midi
{
    /// <summary>
    /// 頷き用のツマミが1ステップ回されたことを表す。
    /// どちらのデッキのツマミかと、頷いて（下を向いて）いるかを持つ。
    /// </summary>
    public readonly struct DjNodStep
    {
        public DjNodStep(DjDeckSide deckSide, bool isNodding)
        {
            DeckSide = deckSide;
            IsNodding = isNodding;
        }

        /// <summary>回されたツマミが属するデッキ。左が CPU1、右が CPU2。</summary>
        public DjDeckSide DeckSide { get; }

        /// <summary>頷いて下を向いた状態か。false なら元の向きへ戻す。</summary>
        public bool IsNodding { get; }
    }
}
