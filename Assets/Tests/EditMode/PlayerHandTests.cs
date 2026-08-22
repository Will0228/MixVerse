using System.Linq;
using NUnit.Framework;

namespace MixVerse.Game.Model.Tests
{
    public sealed class PlayerHandTests
    {
        private static PlayerHand CreateHand(params Card[] cards)
        {
            var hand = new PlayerHand();
            hand.AddRange(cards);
            return hand;
        }

        [Test]
        public void DiscardPairs_同ランク2枚を捨てて手札が空になる()
        {
            var hand = CreateHand(
                new Card(CardSuit.Hearts, CardRank.Seven),
                new Card(CardSuit.Spades, CardRank.Seven));

            var discarded = hand.DiscardPairs();

            Assert.That(hand.Count, Is.EqualTo(0));
            Assert.That(discarded.Count, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Card(CardSuit.Hearts, CardRank.Seven),
                    new Card(CardSuit.Spades, CardRank.Seven),
                },
                discarded);
        }

        [Test]
        public void DiscardPairs_同ランク3枚では1枚だけ残る()
        {
            var hand = CreateHand(
                new Card(CardSuit.Hearts, CardRank.Three),
                new Card(CardSuit.Spades, CardRank.Three),
                new Card(CardSuit.Clubs, CardRank.Three));

            var discarded = hand.DiscardPairs();

            Assert.That(discarded.Count, Is.EqualTo(2));
            Assert.That(hand.Count, Is.EqualTo(1));
            Assert.That(hand[0], Is.EqualTo(new Card(CardSuit.Clubs, CardRank.Three)));
        }

        [Test]
        public void DiscardPairs_同ランク4枚では2ペア成立して空になる()
        {
            var hand = CreateHand(
                new Card(CardSuit.Hearts, CardRank.King),
                new Card(CardSuit.Spades, CardRank.King),
                new Card(CardSuit.Clubs, CardRank.King),
                new Card(CardSuit.Diamonds, CardRank.King));

            var discarded = hand.DiscardPairs();

            Assert.That(discarded.Count, Is.EqualTo(4));
            Assert.That(hand.IsEmpty, Is.True);
        }

        [Test]
        public void DiscardPairs_ジョーカーはペアにならず残り続ける()
        {
            var hand = CreateHand(
                Card.Joker,
                new Card(CardSuit.Hearts, CardRank.Two),
                new Card(CardSuit.Spades, CardRank.Two));

            var discarded = hand.DiscardPairs();

            Assert.That(discarded.Count, Is.EqualTo(2));
            Assert.That(hand.Count, Is.EqualTo(1));
            Assert.That(hand[0].IsJoker, Is.True);
        }

        [Test]
        public void DiscardPairs_ペアが無ければ何も変わらない()
        {
            var hand = CreateHand(
                new Card(CardSuit.Hearts, CardRank.Two),
                new Card(CardSuit.Spades, CardRank.Five),
                Card.Joker);

            var discarded = hand.DiscardPairs();

            Assert.That(discarded.Count, Is.EqualTo(0));
            Assert.That(hand.Count, Is.EqualTo(3));
        }

        [Test]
        public void DiscardPairs_残ったカードの並び順が保たれる()
        {
            var hand = CreateHand(
                new Card(CardSuit.Hearts, CardRank.Two),
                new Card(CardSuit.Hearts, CardRank.Nine),
                new Card(CardSuit.Spades, CardRank.Five),
                new Card(CardSuit.Spades, CardRank.Nine),
                new Card(CardSuit.Clubs, CardRank.Ace));

            hand.DiscardPairs();

            CollectionAssert.AreEqual(
                new[]
                {
                    new Card(CardSuit.Hearts, CardRank.Two),
                    new Card(CardSuit.Spades, CardRank.Five),
                    new Card(CardSuit.Clubs, CardRank.Ace),
                },
                hand.Cards.ToArray());
        }

        [Test]
        public void RemoveAt_指定した位置のカードを抜き取れる()
        {
            var hand = CreateHand(
                new Card(CardSuit.Hearts, CardRank.Two),
                new Card(CardSuit.Spades, CardRank.Five));

            var removed = hand.RemoveAt(0);

            Assert.That(removed, Is.EqualTo(new Card(CardSuit.Hearts, CardRank.Two)));
            Assert.That(hand.Count, Is.EqualTo(1));
        }

        [Test]
        public void RemoveAt_範囲外を指定すると例外になる()
        {
            var hand = CreateHand(new Card(CardSuit.Hearts, CardRank.Two));

            Assert.Throws<System.ArgumentOutOfRangeException>(() => hand.RemoveAt(1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => hand.RemoveAt(-1));
        }
    }
}
