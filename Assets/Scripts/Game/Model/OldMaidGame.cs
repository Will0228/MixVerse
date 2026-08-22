using System;
using System.Collections.Generic;

namespace MixVerse.Game.Model
{
    /// <summary>
    /// ババ抜きのルールエンジン。
    /// Unity にも R3 にも依存しない純粋な C# として実装し、単体テストできるようにしている。
    /// イベントは持たず、状態変化は Draw の戻り値（<see cref="DrawResult"/>）として返す。
    /// </summary>
    public sealed class OldMaidGame
    {
        /// <summary>今回の仕様上のプレイヤー数（本人 + CPU2人）。</summary>
        public const int DefaultPlayerCount = 3;

        private readonly List<PlayerHand> _hands = new List<PlayerHand>();
        private readonly List<int> _finishedOrder = new List<int>();
        private readonly List<Card> _discardPile = new List<Card>();

        private bool[] _isFinished = Array.Empty<bool>();
        private int _currentPlayerIndex;
        private bool _isStarted;

        public IReadOnlyList<PlayerHand> Hands => _hands;

        /// <summary>上がったプレイヤーの番号を上がった順に並べたもの。</summary>
        public IReadOnlyList<int> FinishedOrder => _finishedOrder;

        /// <summary>これまでに捨てられたカード。</summary>
        public IReadOnlyList<Card> DiscardPile => _discardPile;

        public int PlayerCount => _hands.Count;

        /// <summary>これから1枚引くプレイヤー。</summary>
        public int CurrentPlayerIndex => _currentPlayerIndex;

        /// <summary>引かれる側のプレイヤー。上がり済みのプレイヤーは飛ばす。決着済みなら -1。</summary>
        public int TargetPlayerIndex => FindNextActivePlayer(_currentPlayerIndex);

        /// <summary>まだ手札が残っているプレイヤーの人数。</summary>
        public int ActivePlayerCount => PlayerCount - _finishedOrder.Count;

        /// <summary>残り1人になったら決着。</summary>
        public bool IsGameOver => _isStarted && ActivePlayerCount <= 1;

        /// <summary>最後まで残った（ジョーカーを持っている）プレイヤー。決着前は -1。</summary>
        public int LoserIndex
        {
            get
            {
                if (!IsGameOver)
                {
                    return -1;
                }

                for (var i = 0; i < _hands.Count; i++)
                {
                    if (!_isFinished[i])
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        public bool IsFinished(int playerIndex) => _isFinished[playerIndex];

        /// <summary>
        /// 山札を作って配札する。この時点ではまだペアを捨てない
        /// （配札とペア捨てを別々に演出できるようにするため）。
        /// </summary>
        public void Start(int playerCount, int seed)
        {
            if (playerCount < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount), playerCount, "プレイヤーは2人以上必要です。");
            }

            _hands.Clear();
            _finishedOrder.Clear();
            _discardPile.Clear();
            _isFinished = new bool[playerCount];
            _currentPlayerIndex = 0;

            for (var i = 0; i < playerCount; i++)
            {
                _hands.Add(new PlayerHand());
            }

            // 53枚を順番に配るため、人数で割り切れない分は先頭のプレイヤーが1枚多く持つ
            var deck = Deck.CreateShuffled(seed);
            for (var i = 0; i < deck.Count; i++)
            {
                _hands[i % playerCount].Add(deck[i]);
            }

            _isStarted = true;
        }

        /// <summary>
        /// 配札直後のペアを全員分まとめて捨てる。
        /// </summary>
        /// <returns>プレイヤー番号をインデックスとした、捨てられたカードの一覧。</returns>
        public IReadOnlyList<IReadOnlyList<Card>> DiscardInitialPairs()
        {
            ThrowIfNotStarted();

            var discardedPerPlayer = new List<IReadOnlyList<Card>>(_hands.Count);

            for (var i = 0; i < _hands.Count; i++)
            {
                var discarded = _hands[i].DiscardPairs();
                _discardPile.AddRange(discarded);
                discardedPerPlayer.Add(discarded);
            }

            // 配札の時点で手札が空になることは稀だが、念のため上がり判定を通す
            for (var i = 0; i < _hands.Count; i++)
            {
                TryMarkFinished(i, null);
            }

            MoveTurnToActivePlayer();

            return discardedPerPlayer;
        }

        /// <summary>
        /// 現在の手番のプレイヤーが、次のプレイヤーの手札から1枚引く。
        /// 引いた直後にペアが成立していれば自動で捨てる。
        /// </summary>
        /// <param name="cardIndex">引き元の手札における位置。</param>
        public DrawResult Draw(int cardIndex)
        {
            ThrowIfNotStarted();

            if (IsGameOver)
            {
                throw new InvalidOperationException("すでに決着しているためカードを引けません。");
            }

            var drawerIndex = _currentPlayerIndex;
            var targetIndex = TargetPlayerIndex;

            if (targetIndex < 0)
            {
                throw new InvalidOperationException("カードを引ける相手がいません。");
            }

            var target = _hands[targetIndex];
            var drawer = _hands[drawerIndex];

            var drawnCard = target.RemoveAt(cardIndex);
            drawer.Add(drawnCard);

            var discarded = drawer.DiscardPairs();
            _discardPile.AddRange(discarded);

            // 引かれた側が先に空になり、その後で引いた側が上がる可能性がある
            var newlyFinished = new List<int>();
            TryMarkFinished(targetIndex, newlyFinished);
            TryMarkFinished(drawerIndex, newlyFinished);

            MoveTurnToNextPlayer();

            return new DrawResult(
                drawerIndex,
                targetIndex,
                cardIndex,
                drawnCard,
                discarded,
                newlyFinished);
        }

        /// <summary>
        /// 手札が空になったプレイヤーを上がり扱いにする。
        /// </summary>
        private void TryMarkFinished(int playerIndex, List<int> newlyFinished)
        {
            if (_isFinished[playerIndex] || !_hands[playerIndex].IsEmpty)
            {
                return;
            }

            _isFinished[playerIndex] = true;
            _finishedOrder.Add(playerIndex);
            newlyFinished?.Add(playerIndex);
        }

        /// <summary>
        /// 手番を次の未上がりプレイヤーへ進める。
        /// </summary>
        private void MoveTurnToNextPlayer()
        {
            if (IsGameOver)
            {
                return;
            }

            var next = FindNextActivePlayer(_currentPlayerIndex);
            if (next >= 0)
            {
                _currentPlayerIndex = next;
            }
        }

        /// <summary>
        /// 現在の手番が上がり済みのプレイヤーを指している場合に、未上がりのプレイヤーへ寄せる。
        /// </summary>
        private void MoveTurnToActivePlayer()
        {
            if (IsGameOver || !_isFinished[_currentPlayerIndex])
            {
                return;
            }

            var next = FindNextActivePlayer(_currentPlayerIndex);
            if (next >= 0)
            {
                _currentPlayerIndex = next;
            }
        }

        /// <summary>
        /// 指定した位置の次にいる未上がりプレイヤーを探す。見つからなければ -1。
        /// </summary>
        private int FindNextActivePlayer(int from)
        {
            var count = _hands.Count;

            for (var offset = 1; offset < count; offset++)
            {
                var index = (from + offset) % count;
                if (!_isFinished[index])
                {
                    return index;
                }
            }

            return -1;
        }

        private void ThrowIfNotStarted()
        {
            if (!_isStarted)
            {
                throw new InvalidOperationException("Start が呼ばれていません。");
            }
        }
    }
}
