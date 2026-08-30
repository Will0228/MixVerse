using System;
using NUnit.Framework;

namespace MixVerse.Game.Model.Tests
{
    public sealed class CpuTalkScriptTests
    {
        [Test]
        public void CreateSequence_talk1で始まり締めで終わる()
        {
            var script = new CpuTalkScript();
            var random = new Random(1234);

            for (var trial = 0; trial < 100; trial++)
            {
                var lines = script.CreateSequence(random);

                Assert.That(lines[0], Is.EqualTo(CpuTalkLine.Talk1));
                Assert.That(CpuTalkScript.IsFinishLine(lines[lines.Count - 1]), Is.True);
            }
        }

        [Test]
        public void CreateSequence_talk3は必ず締めの直前に流れる()
        {
            var script = new CpuTalkScript();
            var random = new Random(99);

            for (var trial = 0; trial < 100; trial++)
            {
                var lines = script.CreateSequence(random);

                Assert.That(lines[lines.Count - 2], Is.EqualTo(CpuTalkLine.Talk3));
            }
        }

        [Test]
        public void CreateSequence_talk2を挟む場合はtalk1とtalk3の間に入る()
        {
            var script = new CpuTalkScript();
            var random = new Random(2024);

            var withTalk2 = 0;
            var withoutTalk2 = 0;

            for (var trial = 0; trial < 200; trial++)
            {
                var lines = script.CreateSequence(random);

                if (lines.Count == 4)
                {
                    Assert.That(lines[1], Is.EqualTo(CpuTalkLine.Talk2));
                    withTalk2++;
                }
                else
                {
                    Assert.That(lines.Count, Is.EqualTo(3));
                    withoutTalk2++;
                }
            }

            // どちらの並びも起こりうる
            Assert.That(withTalk2, Is.GreaterThan(0));
            Assert.That(withoutTalk2, Is.GreaterThan(0));
        }

        [Test]
        public void CreateSequence_締めは2種類とも選ばれる()
        {
            var script = new CpuTalkScript();
            var random = new Random(7);

            var finish1 = 0;
            var finish2 = 0;

            for (var trial = 0; trial < 200; trial++)
            {
                var lines = script.CreateSequence(random);

                if (lines[lines.Count - 1] == CpuTalkLine.Finish1)
                {
                    finish1++;
                }
                else
                {
                    finish2++;
                }
            }

            Assert.That(finish1, Is.GreaterThan(0));
            Assert.That(finish2, Is.GreaterThan(0));
        }

        [Test]
        public void CreateSequence_同じseedなら同じ並びになる()
        {
            var script = new CpuTalkScript();

            var first = script.CreateSequence(new Random(555));
            var second = script.CreateSequence(new Random(555));

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void 乱数器がnullなら例外になる()
        {
            var script = new CpuTalkScript();

            Assert.Throws<ArgumentNullException>(() => script.CreateSequence(null));
        }
    }
}
