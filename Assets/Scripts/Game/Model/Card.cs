using System;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// トランプのスート。ジョーカーは専用のスートとして扱う。
    /// </summary>
    public enum CardSuit
    {
        Clubs,
        Diamonds,
        Hearts,
        Spades,
        Joker,
    }

    /// <summary>
    /// トランプのランク。ババ抜きでは同じランク同士がペアになる。
    /// </summary>
    public enum CardRank
    {
        Joker = 0,
        Two = 2,
        Three = 3,
        Four = 4,
        Five = 5,
        Six = 6,
        Seven = 7,
        Eight = 8,
        Nine = 9,
        Ten = 10,
        Jack = 11,
        Queen = 12,
        King = 13,
        Ace = 14,
    }

    /// <summary>
    /// カード1枚。値型かつ不変で、同じスートとランクなら等価として扱う。
    /// </summary>
    public readonly struct Card : IEquatable<Card>
    {
        /// <summary>ババ（ジョーカー）。デッキ内に1枚だけ存在する。</summary>
        public static readonly Card Joker = new Card(CardSuit.Joker, CardRank.Joker);

        public CardSuit Suit { get; }
        public CardRank Rank { get; }

        /// <summary>ジョーカーはペアにならないため、判定を専用のプロパティで公開する。</summary>
        public bool IsJoker => Suit == CardSuit.Joker;

        public Card(CardSuit suit, CardRank rank)
        {
            Suit = suit;
            Rank = rank;
        }

        /// <summary>カード表面に表示する短い文字列。絵柄画像が用意されるまでのプレースホルダ。</summary>
        public string ToShortString()
        {
            if (IsJoker)
            {
                return "JOKER";
            }

            return RankLabel() + SuitLabel();
        }

        private string RankLabel()
        {
            switch (Rank)
            {
                case CardRank.Ace: return "A";
                case CardRank.King: return "K";
                case CardRank.Queen: return "Q";
                case CardRank.Jack: return "J";
                case CardRank.Ten: return "10";
                default: return ((int)Rank).ToString();
            }
        }

        private string SuitLabel()
        {
            switch (Suit)
            {
                case CardSuit.Clubs: return "\u2663";
                case CardSuit.Diamonds: return "\u2666";
                case CardSuit.Hearts: return "\u2665";
                case CardSuit.Spades: return "\u2660";
                default: return string.Empty;
            }
        }

        public bool Equals(Card other) => Suit == other.Suit && Rank == other.Rank;

        public override bool Equals(object obj) => obj is Card other && Equals(other);

        public override int GetHashCode() => ((int)Suit * 100) + (int)Rank;

        public override string ToString() => ToShortString();

        public static bool operator ==(Card left, Card right) => left.Equals(right);

        public static bool operator !=(Card left, Card right) => !left.Equals(right);
    }
}
