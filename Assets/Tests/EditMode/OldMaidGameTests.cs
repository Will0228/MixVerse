using System;
using System.Linq;
using NUnit.Framework;

namespace MixVerse.Game.Model.Tests
{
    public sealed class OldMaidGameTests
    {
        private const int PlayerCount = 3;
        private const int Seed = 20260822;

        /// <summary>無限ループ検知用。3人で53枚なら現実的にこの回数を超えることはない。</summary>
        private const int MaxTurns = 1000;

        private static OldMaidGame CreateStartedGame(int seed = Seed)
        {
            var game = new OldMaidGame();
            game.Start(PlayerCount, seed);
            return game;
        }

        private static int TotalCardsInHands(OldMaidGame game) => game.Hands.Sum(hand => hand.Count);

        /// <summary>決着まで一気に進める。戻り値は消化した手数。</summary>
        private static int PlayToEnd(OldMaidGame game, Random random)
        {
            var turns = 0;

            while (!game.IsGameOver)
            {
                if (turns++ > MaxTurns)
                {
                    Assert.Fail(MaxTurns + " 手を超えても決着しませんでした。手番の進行が破綻している可能性があります。");
                }

                var target = game.TargetPlayerIndex;
                var index = random.Next(game.Hands[target].Count);
                game.Draw(index);
            }

            return turns;
        }

        [Test]
        public void Start_3人に53枚が配られる()
        {
            var game = CreateStartedGame();

            Assert.That(game.PlayerCount, Is.EqualTo(3));
            Assert.That(TotalCardsInHands(game), Is.EqualTo(53));
            Assert.That(game.Hands[0].Count, Is.EqualTo(18));
            Assert.That(game.Hands[1].Count, Is.EqualTo(18));
            Assert.That(game.Hands[2].Count, Is.EqualTo(17));
        }

        [Test]
        public void Start_配札直後はまだペアが捨てられていない()
        {
            var game = CreateStartedGame();

            Assert.That(game.DiscardPile.Count, Is.EqualTo(0));
        }

        [Test]
        public void DiscardInitialPairs_残り枚数は必ず奇数になる()
        {
            // ペアは必ず2枚ずつ減るため、53枚から始まる限り残りは奇数
            for (var seed = 0; seed < 50; seed++)
            {
                var game = CreateStartedGame(seed);
                game.DiscardInitialPairs();

                Assert.That(TotalCardsInHands(game) % 2, Is.EqualTo(1), "seed=" + seed + " で偶数になりました。");
            }
        }

        [Test]
        public void DiscardInitialPairs_どの手札にも同ランクが2枚以上残らない()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();

            foreach (var hand in game.Hands)
            {
                var duplicatedRanks = hand.Cards
                    .Where(card => !card.IsJoker)
                    .GroupBy(card => card.Rank)
                    .Where(group => group.Count() >= 2)
                    .ToList();

                Assert.That(duplicatedRanks, Is.Empty);
            }
        }

        [Test]
        public void DiscardInitialPairs_捨てた枚数と手札の減少が一致する()
        {
            var game = CreateStartedGame();
            var before = TotalCardsInHands(game);

            var discardedPerPlayer = game.DiscardInitialPairs();
            var discardedCount = discardedPerPlayer.Sum(cards => cards.Count);

            Assert.That(TotalCardsInHands(game), Is.EqualTo(before - discardedCount));
            Assert.That(game.DiscardPile.Count, Is.EqualTo(discardedCount));
        }

        [Test]
        public void TargetPlayerIndex_手番の次のプレイヤーを指す()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();

            Assert.That(game.CurrentPlayerIndex, Is.EqualTo(0));
            Assert.That(game.TargetPlayerIndex, Is.EqualTo(1));
        }

        [Test]
        public void Draw_引き元から引き先へカードが移動する()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();

            var drawer = game.CurrentPlayerIndex;
            var target = game.TargetPlayerIndex;
            var targetCountBefore = game.Hands[target].Count;
            var expectedCard = game.Hands[target][0];

            var result = game.Draw(0);

            Assert.That(result.DrawerIndex, Is.EqualTo(drawer));
            Assert.That(result.TargetIndex, Is.EqualTo(target));
            Assert.That(result.DrawnCard, Is.EqualTo(expectedCard));
            Assert.That(game.Hands[target].Count, Is.EqualTo(targetCountBefore - 1));
        }

        [Test]
        public void Draw_ペアが成立したら自動で捨てられる()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();

            var totalBefore = TotalCardsInHands(game);
            var result = game.Draw(0);

            // カードは移動しただけなので総数は変わらず、ペアが出た分だけ減る
            var expected = totalBefore - result.DiscardedPair.Count;
            Assert.That(TotalCardsInHands(game), Is.EqualTo(expected));

            if (result.HasDiscarded)
            {
                Assert.That(result.DiscardedPair.Count % 2, Is.EqualTo(0));
            }
        }

        [Test]
        public void Draw_手番が次のプレイヤーへ進む()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();

            var before = game.CurrentPlayerIndex;
            game.Draw(0);

            Assert.That(game.CurrentPlayerIndex, Is.Not.EqualTo(before));
        }

        [Test]
        public void 上がったプレイヤーはFinishedOrderに記録され手番から外れる()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();
            PlayToEnd(game, new Random(Seed));

            Assert.That(game.FinishedOrder.Count, Is.EqualTo(PlayerCount - 1));
            CollectionAssert.AllItemsAreUnique(game.FinishedOrder);

            foreach (var index in game.FinishedOrder)
            {
                Assert.That(game.IsFinished(index), Is.True);
                Assert.That(game.Hands[index].IsEmpty, Is.True);
            }
        }

        [Test]
        public void ゲームは有限手数で必ず決着する()
        {
            for (var seed = 0; seed < 30; seed++)
            {
                var game = CreateStartedGame(seed);
                game.DiscardInitialPairs();

                var turns = PlayToEnd(game, new Random(seed));

                Assert.That(game.IsGameOver, Is.True, "seed=" + seed + " で決着しませんでした。");
                Assert.That(turns, Is.GreaterThan(0));
            }
        }

        [Test]
        public void 決着時に残るのはジョーカー1枚を持つ1人だけ()
        {
            for (var seed = 0; seed < 30; seed++)
            {
                var game = CreateStartedGame(seed);
                game.DiscardInitialPairs();
                PlayToEnd(game, new Random(seed));

                var loser = game.LoserIndex;
                Assert.That(loser, Is.GreaterThanOrEqualTo(0), "seed=" + seed + " で敗者が確定していません。");

                var loserHand = game.Hands[loser];
                Assert.That(loserHand.Count, Is.EqualTo(1), "seed=" + seed + " で敗者の手札が1枚ではありません。");
                Assert.That(loserHand[0].IsJoker, Is.True, "seed=" + seed + " で敗者が持っているのがジョーカーではありません。");
            }
        }

        [Test]
        public void 同じseedと同じ選択なら同じ結果になる()
        {
            var firstGame = CreateStartedGame();
            firstGame.DiscardInitialPairs();
            PlayToEnd(firstGame, new Random(Seed));

            var secondGame = CreateStartedGame();
            secondGame.DiscardInitialPairs();
            PlayToEnd(secondGame, new Random(Seed));

            CollectionAssert.AreEqual(firstGame.FinishedOrder, secondGame.FinishedOrder);
            Assert.That(firstGame.LoserIndex, Is.EqualTo(secondGame.LoserIndex));
        }

        [Test]
        public void Start前にDrawを呼ぶと例外になる()
        {
            var game = new OldMaidGame();

            Assert.Throws<InvalidOperationException>(() => game.Draw(0));
        }

        [Test]
        public void 決着後にDrawを呼ぶと例外になる()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();
            PlayToEnd(game, new Random(Seed));

            Assert.Throws<InvalidOperationException>(() => game.Draw(0));
        }

        [Test]
        public void Draw_範囲外のインデックスを指定すると例外になる()
        {
            var game = CreateStartedGame();
            game.DiscardInitialPairs();

            var targetCount = game.Hands[game.TargetPlayerIndex].Count;

            Assert.Throws<ArgumentOutOfRangeException>(() => game.Draw(targetCount));
        }
    }
}
