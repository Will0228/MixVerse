namespace MixVerse.Game.Model
{
    /// <summary>
    /// <see cref="ClapChallenge"/> の判定結果。まだ決まっていない間は <see cref="Pending"/>。
    /// </summary>
    public enum ClapChallengeResult
    {
        Pending,
        Success,
        Failure,
    }
}
