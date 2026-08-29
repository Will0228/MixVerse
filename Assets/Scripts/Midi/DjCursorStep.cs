using UnityEngine;

namespace MixVerse.Midi
{
    /// <summary>
    /// カーソル（照準）を動かすツマミが1ステップ回されたことを表す。
    /// どちらのデッキのツマミかと、画面上での移動方向を持つ。
    /// </summary>
    public readonly struct DjCursorStep
    {
        public DjCursorStep(DjDeckSide deckSide, Vector2 delta)
        {
            DeckSide = deckSide;
            Delta = delta;
        }

        /// <summary>回されたツマミが属するデッキ。左が CPU1、右が CPU2。</summary>
        public DjDeckSide DeckSide { get; }

        /// <summary>画面上での移動方向。+X が右、+Y が上で、大きさは常に 1。</summary>
        public Vector2 Delta { get; }
    }
}
