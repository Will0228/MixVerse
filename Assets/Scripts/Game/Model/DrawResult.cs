using System;
using System.Collections.Generic;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// 1回のドローで何が起きたかをまとめた結果。
    /// Model はイベントを持たず、この構造体を返すことで View 側の演出に必要な情報を渡す。
    /// </summary>
    public readonly struct DrawResult
    {
        /// <summary>カードを引いたプレイヤー。</summary>
        public int DrawerIndex { get; }

        /// <summary>カードを引かれたプレイヤー。</summary>
        public int TargetIndex { get; }

        /// <summary>引かれる前に、引き元の手札の何番目にあったか。</summary>
        public int DrawnCardIndex { get; }

        /// <summary>引いたカード。</summary>
        public Card DrawnCard { get; }

        /// <summary>引いた結果その場で捨てられたペア。成立しなければ空。</summary>
        public IReadOnlyList<Card> DiscardedPair { get; }

        /// <summary>このドローで新たに上がったプレイヤー。上がった順に並ぶ。</summary>
        public IReadOnlyList<int> NewlyFinishedPlayers { get; }

        public bool HasDiscarded => DiscardedPair.Count > 0;

        public DrawResult(
            int drawerIndex,
            int targetIndex,
            int drawnCardIndex,
            Card drawnCard,
            IReadOnlyList<Card> discardedPair,
            IReadOnlyList<int> newlyFinishedPlayers)
        {
            DrawerIndex = drawerIndex;
            TargetIndex = targetIndex;
            DrawnCardIndex = drawnCardIndex;
            DrawnCard = drawnCard;
            DiscardedPair = discardedPair ?? Array.Empty<Card>();
            NewlyFinishedPlayers = newlyFinishedPlayers ?? Array.Empty<int>();
        }
    }
}
