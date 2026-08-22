using System;
using System.Collections.Generic;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// プレイヤー1人分の手札。
    /// カードの並び順は View 側の表示順とインデックスが一致する必要があるため、
    /// ペアを捨てる際も残ったカードの相対順序を保つ。
    /// </summary>
    public sealed class PlayerHand
    {
        private static readonly IReadOnlyList<Card> EmptyCards = Array.Empty<Card>();

        private readonly List<Card> _cards = new List<Card>();

        public IReadOnlyList<Card> Cards => _cards;

        public int Count => _cards.Count;

        public bool IsEmpty => _cards.Count == 0;

        public Card this[int index] => _cards[index];

        public void Add(Card card) => _cards.Add(card);

        public void AddRange(IEnumerable<Card> cards)
        {
            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            _cards.AddRange(cards);
        }

        /// <summary>
        /// 指定した位置のカードを抜き取る。
        /// </summary>
        public Card RemoveAt(int index)
        {
            if (index < 0 || index >= _cards.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "手札の範囲外のインデックスが指定されました。");
            }

            var card = _cards[index];
            _cards.RemoveAt(index);
            return card;
        }

        /// <summary>
        /// 同じランクのカード2枚を1組として、揃っているだけすべて捨てる。
        /// ジョーカーはどのカードともペアにならず必ず手札に残る。
        /// </summary>
        /// <returns>捨てたカード。ペアが1組も無ければ空。</returns>
        public IReadOnlyList<Card> DiscardPairs()
        {
            var discarded = new List<Card>();
            var shouldRemove = new bool[_cards.Count];

            // ランクごとに「まだ相方が見つかっていないカードの位置」を覚えておき、
            // 同じランクが再び現れた時点で2枚同時に捨てる。
            var pendingIndexByRank = new Dictionary<CardRank, int>();

            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];

                if (card.IsJoker)
                {
                    continue;
                }

                if (pendingIndexByRank.TryGetValue(card.Rank, out var pairedIndex))
                {
                    shouldRemove[pairedIndex] = true;
                    shouldRemove[i] = true;

                    discarded.Add(_cards[pairedIndex]);
                    discarded.Add(card);

                    pendingIndexByRank.Remove(card.Rank);
                }
                else
                {
                    pendingIndexByRank[card.Rank] = i;
                }
            }

            if (discarded.Count == 0)
            {
                return EmptyCards;
            }

            // 後ろから消すことでインデックスのズレを避ける
            for (var i = _cards.Count - 1; i >= 0; i--)
            {
                if (shouldRemove[i])
                {
                    _cards.RemoveAt(i);
                }
            }

            return discarded;
        }

        public void Clear() => _cards.Clear();
    }
}
