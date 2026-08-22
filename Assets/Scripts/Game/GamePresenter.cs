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

        private readonly GameView _view;
        private readonly OldMaidGame _game;
        private readonly CpuStrategy _cpuStrategy;
        private readonly DjControllerInput _djController;

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
            _view.SetTurnText("Your turn - press SYNC to start selecting");

            await _djController.OnSyncPressed.FirstAsync(token);

            // ② ここから手札選択状態。ジョグを回すたびにカーソルが左右へ動く
            var cardCount = _game.Hands[targetIndex].Count;
            var cursor = 0;

            _view.SetSelectableHand(targetIndex);
            _view.ShowArrowAt(targetIndex, cursor);
            _view.SetTurnText("Turn the jog to move - press SYNC to draw from " + GetPlayerName(targetIndex));

            using (_djController.OnJogStep.Subscribe(step =>
                   {
                       cursor = WrapIndex(cursor + step, cardCount);
                       _view.ShowArrowAt(targetIndex, cursor);
                   }))
            {
                // SYNC をもう一度押すとカーソル位置で確定。マウスクリックでも確定できる。
                var selectedIndex = await Observable.Merge(
                        _djController.OnSyncPressed.Select(_ => CursorConfirmed),
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
            _view.SetSelectableHand(targetIndex);

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
    }
}
