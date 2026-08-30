namespace MixVerse.Game.Model
{
    /// <summary>
    /// CPU が話すときに流す音源の種類。
    /// View 側で talk_1 / talk_2 / talk_3 / talk_finish_1 / talk_finish_2 / talk_angry と対応付ける。
    /// </summary>
    public enum CpuTalkLine
    {
        Talk1,
        Talk2,
        Talk3,
        Finish1,
        Finish2,

        /// <summary>求められていないのに頷きすぎて、会話が強制的に終わるときの音源。</summary>
        Angry,
    }
}
