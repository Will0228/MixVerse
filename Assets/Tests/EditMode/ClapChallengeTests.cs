using NUnit.Framework;

namespace MixVerse.Game.Model.Tests
{
    public sealed class ClapChallengeTests
    {
        /// <summary>話し終わる時刻。ここを基準に時間を進めて判定する。</summary>
        private const float TalkEndTime = 100f;

        [Test]
        public void Evaluate_始めるまでは判定が出ない()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            Assert.That(challenge.Evaluate(TalkEndTime), Is.EqualTo(ClapChallengeResult.Pending));
        }

        [Test]
        public void RegisterClap_制限時間内に規定回数そろえば成功になる()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            for (var i = 0; i < ClapChallenge.RequiredClapCount; i++)
            {
                Assert.That(challenge.RegisterClap(TalkEndTime + 0.5f + (i * 0.2f)), Is.True);
            }

            Assert.That(challenge.Evaluate(TalkEndTime + 2f), Is.EqualTo(ClapChallengeResult.Success));
            Assert.That(challenge.RemainingClapCount, Is.EqualTo(0));
        }

        [Test]
        public void Evaluate_拍手を始めなければ失敗になる()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            var justInTime = TalkEndTime + ClapChallenge.StartLimitSeconds;

            Assert.That(challenge.Evaluate(justInTime), Is.EqualTo(ClapChallengeResult.Pending));
            Assert.That(challenge.Evaluate(justInTime + 0.01f), Is.EqualTo(ClapChallengeResult.Failure));
        }

        [Test]
        public void Evaluate_拍手が足りないまま制限時間を過ぎると失敗になる()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            var firstClapTime = TalkEndTime + 0.5f;

            for (var i = 0; i < ClapChallenge.RequiredClapCount - 1; i++)
            {
                challenge.RegisterClap(firstClapTime + (i * 0.1f));
            }

            var deadline = firstClapTime + ClapChallenge.ClapLimitSeconds;

            Assert.That(challenge.Evaluate(deadline), Is.EqualTo(ClapChallengeResult.Pending));
            Assert.That(challenge.Evaluate(deadline + 0.01f), Is.EqualTo(ClapChallengeResult.Failure));
        }

        [Test]
        public void RegisterClap_話の途中すぎる拍手は数えない()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            var tooEarly = TalkEndTime - ClapChallenge.StartGraceSeconds - 0.01f;

            Assert.That(challenge.RegisterClap(tooEarly), Is.False);
            Assert.That(challenge.ClapCount, Is.EqualTo(0));
        }

        [Test]
        public void RegisterClap_話し終わる直前の猶予の間なら数える()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            Assert.That(challenge.RegisterClap(TalkEndTime - ClapChallenge.StartGraceSeconds), Is.True);
            Assert.That(challenge.ClapCount, Is.EqualTo(1));
        }

        [Test]
        public void RegisterClap_始めるのが遅すぎた拍手は数えない()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            var tooLate = TalkEndTime + ClapChallenge.StartLimitSeconds + 0.01f;

            Assert.That(challenge.RegisterClap(tooLate), Is.False);
            Assert.That(challenge.Evaluate(tooLate), Is.EqualTo(ClapChallengeResult.Failure));
        }

        [Test]
        public void RegisterClap_制限時間を過ぎた拍手は数えない()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            var firstClapTime = TalkEndTime + 0.5f;
            challenge.RegisterClap(firstClapTime);

            Assert.That(challenge.RegisterClap(firstClapTime + ClapChallenge.ClapLimitSeconds + 0.01f), Is.False);
            Assert.That(challenge.ClapCount, Is.EqualTo(1));
        }

        [Test]
        public void RegisterClap_終えたあとは数えない()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);
            challenge.End();

            Assert.That(challenge.RegisterClap(TalkEndTime), Is.False);
            Assert.That(challenge.Evaluate(TalkEndTime + 10f), Is.EqualTo(ClapChallengeResult.Pending));
        }

        [Test]
        public void RemainingClapCount_数えた分だけ減る()
        {
            var challenge = new ClapChallenge();
            challenge.Begin(TalkEndTime);

            challenge.RegisterClap(TalkEndTime);
            challenge.RegisterClap(TalkEndTime + 0.1f);

            Assert.That(challenge.RemainingClapCount, Is.EqualTo(ClapChallenge.RequiredClapCount - 2));
        }
    }
}
