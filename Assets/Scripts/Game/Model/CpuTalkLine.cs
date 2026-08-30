namespace MixVerse.Game.Model
{
    /// <summary>
    /// CPU が話すときに流す音源の種類。
    /// View 側で talk_1 / talk_2 / talk_3 / talk_finish_1 / talk_finish_2 と対応付ける。
    /// </summary>
    public enum CpuTalkLine
    {
        Talk1,
        Talk2,
        Talk3,
        Finish1,
        Finish2,
    }
}
