using System.Threading;
using Cysharp.Threading.Tasks;
using MixVerse.Game.Model;
using MixVerse.Midi;
using R3;
using VContainer;
using Random = System.Random;

namespace MixVerse.Game
{
    /// <summary>
    /// Model（ルール）と View（見た目）の橋渡し。
    /// Controller はこのクラス越しにゲームを進め、Model にも View にも直接触れない。
    /// </summary>
    public sealed class GamePresenter
    {
        /// <summary>操作するプレイヤーの番号。残りの2人が CPU。</summary>
        public const int HumanPlayerIndex = 0;

        private const float CpuThinkDuration = 0.6f;
        private const float CpuShowSelectionDuration = 0.5f;
        private const float TurnIntervalDuration = 0.2f;

        /// <summary>SYNC で確定したことを表す番兵。手札インデックスと区別するため負値にする。</summary>
        private const int CursorConfirmed = -1;

        /// <summary>左デッキ（SYNC / CUE）が担当する相手。</summary>
        private const int LeftDeckPlayerIndex = 1;

        /// <summary>右デッキ（SYNC / CUE）が担当する相手。</summary>
        private const int RightDeckPlayerIndex = 2;

        private readonly GameView _view;
        private readonly OldMaidGame _game;
        private readonly CpuStrategy _cpuStrategy;
        private readonly DjControllerInput _djController;
        private readonly ClapGestureDetector _clapGestureDetector = new ClapGestureDetector();

        private Random _random;

        [Inject]
        public GamePresenter(GameView view, OldMaidGame game, CpuStrategy cpuStrategy, DjControllerInput djController)
        {
            _view = view;
            _game = game;
            _cpuStrategy = cpuStrategy;
            _djController = djController;
        }

        public bool IsGameOver => _game.IsGameOver;

        public bool IsHumanTurn => _game.CurrentPlayerIndex == HumanPlayerIndex;

        /// <summary>
        /// CUE ボタンで手を出し入れし、表示中はジョグの回転方向で拍手する手を開閉させる。
        /// 左の CUE で CPU1、右の CUE で CPU2 を向く。正面を向いているときはどちらも受け付ける。
        /// 時計回りで合わせ、反時計回りで放す。手番の進行とは独立して常に受け付けるため、
        /// Controller のライフサイクルに紐づけて一度だけ呼ぶ。
        /// </summary>
        public void SetupClapGesture(CompositeDisposable disposable, CancellationToken token)
        {
            if (_djController == null)
            {
                return;
            }

            _djController.OnCuePressed
                .Subscribe(deckSide =>
                {
                    // カード選択中（SYNC でカメラが専用位置にある間）は拍手を受け付けない
                    if (_view.IsDrawCameraActive)
                    {
                        return;
                    }

                    var targetIndex = GetDeckPlayerIndex(deckSide);

                    // 誰かを向いている間は、その相手の CUE（＝正面に戻す操作）だけを受け付ける
                    if (_view.IsClapCameraActive && _view.ClapCameraTargetIndex != targetIndex)
                    {
                        return;
                    }

                    _view.ToggleClapCamera(targetIndex, token);
                    _clapGestureDetector.Reset();
                })
                .AddTo(disposable);

            _djController.OnJogStep
                .Subscribe(step =>
                {
                    if (!_view.IsClapHandsVisible)
                    {
                        return;
                    }

                    if (_clapGestureDetector.RegisterStep(step, out var isClosed))
                    {
                        _view.SetHandsClosed(isClosed);
                    }
                })
                .AddTo(disposable);
        }

        /// <summary>
        /// 画面を表示し、山札を配るところまで行う。
        /// </summary>
        public async UniTask PrepareAsync(int seed, CancellationToken token)
        {
            _random = new Random(seed);
            _game.Start(OldMaidGame.DefaultPlayerCount, seed);

            await _view.ShowAsync(token);
            await _view.DealAsync(_game.Hands, token);
        }

        /// <summary>
        /// 配札直後のペアを全員分捨てる。
        /// </summary>
        public async UniTask DiscardInitialPairsAsync(CancellationToken token)
        {
            _view.SetTurnText("Discarding pairs...");

            var discardedPerPlayer = _game.DiscardInitialPairs();

            for (var playerIndex = 0; playerIndex < discardedPerPlayer.Count; playerIndex++)
            {
                await _view.DiscardPairsAsync(playerIndex, discardedPerPlayer[playerIndex], token);
            }
        }

        /// <summary>
        /// 手番を1つ進める。プレイヤーなら入力を待ち、CPU なら自動で選ぶ。
        /// </summary>
        public async UniTask PlayTurnAsync(CancellationToken token)
        {
            var targetIndex = _game.TargetPlayerIndex;

            var cardIndex = IsHumanTurn
                ? await WaitForHumanSelectionAsync(targetIndex, token)
                : await RunCpuSelectionAsync(targetIndex, token);

            var result = _game.Draw(cardIndex);

            await _view.PlayDrawAsync(result, token);

            // Model 側では Draw の時点でペアが捨てられているので、View を追従させる
            await _view.DiscardPairsAsync(result.DrawerIndex, result.DiscardedPair, token);

            await _view.WaitAsync(TurnIntervalDuration, token);
        }

        /// <summary>
        /// 決着の表示。
        /// </summary>
        public void ShowResult()
        {
            _view.ClearSelectable();
            _view.HideArrow();
            _view.SetTurnText(string.Empty);

            var loser = _game.LoserIndex;
            _view.ShowResult(loser == HumanPlayerIndex ? "You lose..." : GetPlayerName(loser) + " loses!");
        }

        /// <summary>
        /// 引く対象の手札をクリックできるようにして、選ばれるまで待つ。
        /// カーソルを乗せたカードの上には View 側で矢印が表示される。
        /// </summary>
        private async UniTask<int> WaitForHumanSelectionAsync(int targetIndex, CancellationToken token)
        {
            // DJ コントローラーが無い環境ではマウスだけで遊べるようにしておく
            if (_djController == null)
            {
                return await WaitForMouseOnlySelectionAsync(targetIndex, token);
            }

            // ① SYNC が押されるまでは手札選択状態に入らない
            _view.ClearSelectable();
            _view.HideArrow();
            _view.SetTurnText("Your turn - press " + GetDeckName(targetIndex) + " SYNC to start selecting");

            // CUE で相手を向いて拍手している間は、SYNC を押しても無反応にする。
            // 真正面に戻る（もう一度 CUE を押す）まで、押された SYNC はここで捨てて待ち続ける。
            // 引く相手に対応していないデッキの SYNC（CPU1 が残っている間の右 SYNC など）も同じく捨てる。
            while (true)
            {
                var deckSide = await _djController.OnSyncPressed.FirstAsync(token);

                if (!_view.IsClapCameraActive && GetDeckPlayerIndex(deckSide) == targetIndex)
                {
                    break;
                }
            }

            // ② ここから手札選択状態。ジョグを回すたびにカーソルが左右へ動く
            var cardCount = _game.Hands[targetIndex].Count;
            var cursor = 0;

            await _view.BeginDrawSelectionAsync(targetIndex, token);
            _view.ShowArrowAt(targetIndex, cursor);
            _view.SetTurnText("Turn the jog to move - press " + GetDeckName(targetIndex) + " SYNC to draw from " + GetPlayerName(targetIndex));

            using (_djController.OnJogStep.Subscribe(step =>
                   {
                       cursor = WrapIndex(cursor + step, cardCount);
                       _view.ShowArrowAt(targetIndex, cursor);
                   }))
            {
                // SYNC をもう一度押すとカーソル位置で確定。マウスクリックでも確定できる。
                var selectedIndex = await Observable.Merge(
                        _djController.OnSyncPressed
                            .Where(deckSide => GetDeckPlayerIndex(deckSide) == targetIndex)
                            .Select(_ => CursorConfirmed),
                        _view.OnCardClicked.Select(card => card.HandIndex))
                    .FirstAsync(token);

                _view.ClearSelectable();
                _view.HideArrow();

                return selectedIndex == CursorConfirmed ? cursor : selectedIndex;
            }
        }

        /// <summary>
        /// DJ コントローラーが接続されていない場合の従来どおりの選択。
        /// </summary>
        private async UniTask<int> WaitForMouseOnlySelectionAsync(int targetIndex, CancellationToken token)
        {
            _view.SetTurnText("Your turn - pick a card from " + GetPlayerName(targetIndex));
            await _view.BeginDrawSelectionAsync(targetIndex, token);

            var selected = await _view.OnCardClicked.FirstAsync(token);

            _view.ClearSelectable();
            _view.HideArrow();

            return selected.HandIndex;
        }

        /// <summary>
        /// カーソルを手札の端で反対側へ回り込ませる。
        /// </summary>
        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            return ((index % count) + count) % count;
        }

        /// <summary>
        /// CPU の手番。少し考える間を置き、選んだカードに矢印を出してから引く。
        /// </summary>
        private async UniTask<int> RunCpuSelectionAsync(int targetIndex, CancellationToken token)
        {
            _view.ClearSelectable();
            _view.SetTurnText(GetPlayerName(_game.CurrentPlayerIndex) + " is thinking...");

            await _view.WaitAsync(CpuThinkDuration, token);

            var cardIndex = _cpuStrategy.SelectIndex(_game.Hands[targetIndex].Count, _random);

            _view.ShowArrowAt(targetIndex, cardIndex);
            await _view.WaitAsync(CpuShowSelectionDuration, token);
            _view.HideArrow();

            return cardIndex;
        }

        private static string GetPlayerName(int playerIndex)
            => playerIndex == HumanPlayerIndex ? "You" : "CPU" + playerIndex;

        /// <summary>
        /// DJ コントローラーの左右デッキと相手プレイヤーの対応。左が CPU1、右が CPU2。
        /// </summary>
        private static int GetDeckPlayerIndex(DjDeckSide deckSide)
            => deckSide == DjDeckSide.Left ? LeftDeckPlayerIndex : RightDeckPlayerIndex;

        /// <summary>
        /// 相手プレイヤーを操作するデッキ側の表示名。操作案内のテキストに使う。
        /// </summary>
        private static string GetDeckName(int playerIndex)
            => playerIndex == RightDeckPlayerIndex ? "right" : "left";
    }
}
