using System;
using System.Collections.Generic;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// ババ抜き用の山札を生成する。
    /// 乱数は UnityEngine.Random ではなく System.Random を使い、
    /// seed を指定すれば必ず同じ並びになるようにしている（テストの決定性のため）。
    /// </summary>
    public static class Deck
    {
        /// <summary>ジョーカーの枚数。ババ抜きなので1枚だけ。</summary>
        public const int JokerCount = 1;

        /// <summary>ジョーカーを除いた通常カードの枚数。</summary>
        public const int StandardCardCount = 52;

        /// <summary>山札の総枚数。</summary>
        public const int TotalCardCount = StandardCardCount + JokerCount;

        /// <summary>
        /// スート順・ランク順に並んだ未シャッフルの山札を作る。
        /// </summary>
        public static List<Card> CreateOrdered()
        {
            var cards = new List<Card>(TotalCardCount);

            foreach (CardSuit suit in Enum.GetValues(typeof(CardSuit)))
            {
                if (suit == CardSuit.Joker)
                {
                    continue;
                }

                for (var rank = CardRank.Two; rank <= CardRank.Ace; rank++)
                {
                    cards.Add(new Card(suit, rank));
                }
            }

            cards.Add(Card.Joker);

            return cards;
        }

        /// <summary>
        /// シャッフル済みの山札を作る。同じ seed なら必ず同じ並びになる。
        /// </summary>
        public static List<Card> CreateShuffled(int seed)
        {
            var cards = CreateOrdered();
            Shuffle(cards, new Random(seed));
            return cards;
        }

        /// <summary>
        /// Fisher-Yates シャッフル。渡されたリストを直接並べ替える。
        /// </summary>
        public static void Shuffle(IList<Card> cards, Random random)
        {
            if (cards == null)
            {
                throw new ArgumentNullException(nameof(cards));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            for (var i = cards.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }
    }
}
