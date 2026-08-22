using System;
using NUnit.Framework;

namespace MixVerse.Game.Model.Tests
{
    public sealed class CpuStrategyTests
    {
        [Test]
        public void SelectIndex_常に手札の範囲内を返す()
        {
            var strategy = new CpuStrategy();
            var random = new Random(1234);

            for (var handCount = 1; handCount <= 20; handCount++)
            {
                for (var trial = 0; trial < 100; trial++)
                {
                    var index = strategy.SelectIndex(handCount, random);

                    Assert.That(index, Is.GreaterThanOrEqualTo(0));
                    Assert.That(index, Is.LessThan(handCount));
                }
            }
        }

        [Test]
        public void SelectIndex_手札が1枚なら必ず0を返す()
        {
            var strategy = new CpuStrategy();
            var random = new Random(1);

            for (var trial = 0; trial < 20; trial++)
            {
                Assert.That(strategy.SelectIndex(1, random), Is.EqualTo(0));
            }
        }

        [Test]
        public void SelectIndex_同じseedなら同じ選択になる()
        {
            var strategy = new CpuStrategy();

            var first = strategy.SelectIndex(17, new Random(99));
            var second = strategy.SelectIndex(17, new Random(99));

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void SelectIndex_手札が空なら例外になる()
        {
            var strategy = new CpuStrategy();

            Assert.Throws<ArgumentOutOfRangeException>(() => strategy.SelectIndex(0, new Random()));
        }

        [Test]
        public void SelectIndex_乱数器がnullなら例外になる()
        {
            var strategy = new CpuStrategy();

            Assert.Throws<ArgumentNullException>(() => strategy.SelectIndex(5, null));
        }
    }
}
