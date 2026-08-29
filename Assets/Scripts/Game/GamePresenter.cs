using System.Threading;
using Cysharp.Threading.Tasks;
using MixVerse.Game.Model;
using MixVerse.Midi;
using R3;
using UnityEngine;
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
        private const float ResultToHomeDelay = 5f;

        /// <summary>SYNC が押されたことを表す番兵。手札インデックスと区別するため負値にする。</summary>
        private const int CursorConfirmed = -1;

        /// <summary>左デッキ（SYNC / CUE）が担当する相手。</summary>
        private const int LeftDeckPlayerIndex = 1;

        /// <summary>右デッキ（SYNC / CUE）が担当する相手。</summary>
        private const int RightDeckPlayerIndex = 2;

        /// <summary>フェーダーを端まで振り切ったとみなす許容差。MIDI の1目盛り(1/127)より小さくしてある。</summary>
        private const float FacingTolerance = 0.002f;

        private readonly GameView _view;
        private readonly OldMaidGame _game;
        private readonly CpuStrategy _cpuStrategy;
        private readonly CpuHealth _cpuHealth;
        private readonly DjControllerInput _djController;
        private readonly ClapGestureDetector _clapGestureDetector = new ClapGestureDetector();

        private Random _random;

        [Inject]
        public GamePresenter(GameView view, OldMaidGame game, CpuStrategy cpuStrategy, CpuHealth cpuHealth, DjControllerInput djController)
        {
            _view = view;
            _game = game;
            _cpuStrategy = cpuStrategy;
            _cpuHealth = cpuHealth;
            _djController = djController;
        }

        public bool IsGameOver => _game.IsGameOver;

        public bool IsHumanTurn => _game.CurrentPlayerIndex == HumanPlayerIndex;

        /// <summary>
        /// フェーダーでどちらを向くかを決め、向いた相手に対して CUE で拍手する手を出し入れする。
        /// 表示中はジョグの回転方向で手を開閉させる（時計回りで合わせ、反時計回りで放す）。
        /// 手番の進行とは独立して常に受け付けるため、Controller のライフサイクルに紐づけて一度だけ呼ぶ。
        /// </summary>
        public void SetupDjControls(CompositeDisposable disposable)
        {
            if (_djController == null)
            {
                return;
            }

            // 購読した時点の値（初期は 0.5 ＝ 正面）でカメラの向きも決まる
            _djController.FacingValue
                .Subscribe(ApplyFacing)
                .AddTo(disposable);

            _djController.OnCuePressed
                .Subscribe(deckSide =>
                {
                    // カード選択中（SYNC で照準を出している間）は拍手を受け付けない
                    if (_view.IsDrawSelectionActive)
                    {
                        return;
                    }

                    // フェーダーがその相手を向き切っているときだけ手を出せる
                    if (!CanActOn(GetDeckPlayerIndex(deckSide)))
                    {
                        return;
                    }

                    _view.ToggleClapHands();
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
        /// フェーダーの値をカメラの向きへ反映する。0.5 で正面、1 で CPU1、0 で CPU2 を向く。
        /// </summary>
        private void ApplyFacing(float value)
        {
            var center = DjControllerInput.DefaultFacingValue;
            var targetIndex = value >= center ? LeftDeckPlayerIndex : RightDeckPlayerIndex;

            // 中央からの距離をそのまま向き具合にする（端まで倒すと 1）
            _view.SetCameraFacing(targetIndex, Mathf.Abs(value - center) / center);

            // 振り切っていない間はその相手に何もできないので、出したままの手は引っ込める
            if (!CanActOn(targetIndex))
            {
                _view.HideClapHands();
                _clapGestureDetector.Reset();
            }
        }

        /// <summary>
        /// フェーダーが端まで振り切っていて、その相手にアクションできるか。
        /// 1 なら CPU1、0 なら CPU2。中途半端な向きではどちらにも何もできない。
        /// </summary>
        private bool CanActOn(int playerIndex)
        {
            // DJ コントローラーが無い環境ではマウスだけで遊ぶため、向きでは制限しない
            if (_djController == null)
            {
                return true;
            }

            var value = _djController.FacingValue.CurrentValue;

            if (playerIndex == LeftDeckPlayerIndex)
            {
                return value >= 1f - FacingTolerance;
            }

            if (playerIndex == RightDeckPlayerIndex)
            {
                return value <= FacingTolerance;
            }

            return false;
        }

        /// <summary>
        /// 画面を表示し、山札を配るところまで行う。
        /// </summary>
        public async UniTask PrepareAsync(int seed, CancellationToken token)
        {
            _random = new Random(seed);
            _game.Start(OldMaidGame.DefaultPlayerCount, seed);
            _cpuHealth.Reset(OldMaidGame.DefaultPlayerCount);

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

            await ApplyPlayerDiscardDamageAsync(discardedPerPlayer[HumanPlayerIndex].Count, token);
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

            // JOKER は誰ともペアにならず手札に残るので、引き当てた CPU はその時点でダメージを受ける
            if (result.DrawerIndex != HumanPlayerIndex && result.DrawnCard.IsJoker)
            {
                await ApplyCpuDamageAsync(result.DrawerIndex, CpuHealth.JokerDamageBase, token);
            }

            if (result.DrawerIndex == HumanPlayerIndex)
            {
                await ApplyPlayerDiscardDamageAsync(result.DiscardedPair.Count, token);
            }

            await _view.WaitAsync(TurnIntervalDuration, token);
        }

        /// <summary>
        /// プレイヤーが手札を捨てた直後、それによって手札が CPU より少なくなっていれば、その CPU の体力を減らす。
        /// 捨てたことで初めて下回った相手だけが対象なので、捨てる前の枚数（捨てた枚数を足し戻したもの）と比べる。
        /// </summary>
        /// <param name="discardedCount">プレイヤーがこのタイミングで捨てた枚数。</param>
        private async UniTask ApplyPlayerDiscardDamageAsync(int discardedCount, CancellationToken token)
        {
            if (discardedCount <= 0)
            {
                return;
            }

            var humanCount = _game.Hands[HumanPlayerIndex].Count;
            var humanCountBeforeDiscard = humanCount + discardedCount;

            for (var playerIndex = 0; playerIndex < _game.PlayerCount; playerIndex++)
            {
                if (playerIndex == HumanPlayerIndex)
                {
                    continue;
                }

                var cpuCount = _game.Hands[playerIndex].Count;

                // 捨てる前からすでに下回っていた相手や、捨てても下回らない相手には効かない。
                // 上がり済み（0枚）の CPU も、下回りようがないのでここで除かれる。
                if (humanCountBeforeDiscard < cpuCount || humanCount >= cpuCount)
                {
                    continue;
                }

                await ApplyCpuDamageAsync(playerIndex, CpuHealth.HandCountDamageBase, token);
            }
        }

        /// <summary>
        /// CPU の体力を減らす。体力が初めて減った CPU は、グリッチで乱れながら別のキャラクターへ変わる。
        /// </summary>
        private async UniTask ApplyCpuDamageAsync(int playerIndex, int damageBase, CancellationToken token)
        {
            var wasDamaged = _cpuHealth.IsDamaged(playerIndex);

            _cpuHealth.ApplyDamage(playerIndex, damageBase, _random);

            // 2回目以降は変身済みなので演出は出さない
            if (!wasDamaged)
            {
                await _view.PlayCharacterMorphAsync(playerIndex, token);
            }
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
            var resultText = loser == HumanPlayerIndex ? "You lose..." : GetPlayerName(loser) + " loses!";
            _view.ShowResult(resultText + "\nReturning to home in 5 seconds...");
        }

        /// <summary>
        /// 決着後、ホーム画面へ戻るまでの待機。
        /// </summary>
        public UniTask WaitBeforeReturnToHomeAsync(CancellationToken token)
            => _view.WaitAsync(ResultToHomeDelay, token);

        /// <summary>
        /// ホーム画面へ戻る際に、この画面を非表示にする。
        /// </summary>
        public void HideView() => _view.Hide();

        /// <summary>
        /// 引く対象の手札を選べる状態にして、選ばれるまで待つ。
        /// フェーダーで引く相手を向き切ってから SYNC を押すと選択が始まり、
        /// 画面のランダムな位置に照準が出るので、担当デッキのツマミで動かし、
        /// カードに重ねた状態で SYNC を押すと引ける。
        /// 狙っているカードの上には View 側で矢印が表示される。
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
            _view.SetTurnText("Your turn - face " + GetPlayerName(targetIndex) + " with the fader, then press " + GetDeckName(targetIndex) + " SYNC");

            // フェーダーで引く相手を向き切っていない間は、SYNC を押しても無反応にする。
            // 拍手する手を出している間（もう一度 CUE を押すまで）も同じく受け付けない。
            // 引く相手に対応していないデッキの SYNC（CPU1 が残っている間の右 SYNC など）もここで捨てる。
            while (true)
            {
                var deckSide = await _djController.OnSyncPressed.FirstAsync(token);

                if (GetDeckPlayerIndex(deckSide) == targetIndex && CanActOn(targetIndex) && !_view.IsClapHandsVisible)
                {
                    break;
                }
            }

            // ② ここから手札選択状態。画面のランダムな位置に照準が出るので、
            //    ツマミで動かして狙ったカードに重ねる
            await _view.BeginDrawSelectionAsync(targetIndex, token);

            _view.HideArrow();
            _view.ShowTargetCursor();
            _view.SetTurnText("Turn the knobs to aim - press " + GetDeckName(targetIndex) + " SYNC on a card to draw from " + GetPlayerName(targetIndex));

            using (_djController.OnCursorStep
                       .Where(cursorStep => GetDeckPlayerIndex(cursorStep.DeckSide) == targetIndex)
                       .Subscribe(cursorStep =>
                       {
                           _view.MoveTargetCursor(cursorStep.Delta);

                           // 重なっていなければ範囲外のインデックスになり、View 側で矢印が消える
                           _view.ShowArrowAt(targetIndex, _view.GetTargetedCardIndex(targetIndex));
                       }))
            {
                // SYNC で確定。マウスクリックでも確定できる。
                var confirmations = Observable.Merge(
                    _djController.OnSyncPressed
                        .Where(deckSide => GetDeckPlayerIndex(deckSide) == targetIndex && CanActOn(targetIndex))
                        .Select(_ => CursorConfirmed),
                    _view.OnCardClicked.Select(card => card.HandIndex));

                int selectedIndex;

                while (true)
                {
                    var confirmed = await confirmations.FirstAsync(token);

                    if (confirmed != CursorConfirmed)
                    {
                        selectedIndex = confirmed;
                        break;
                    }

                    // 照準がカードに重なっていない間は、SYNC を押しても引けない
                    var targeted = _view.GetTargetedCardIndex(targetIndex);

                    if (targeted != GameView.NoTargetedCard)
                    {
                        selectedIndex = targeted;
                        break;
                    }
                }

                _view.ClearSelectable();
                _view.HideArrow();
                _view.HideTargetCursor();

                return selectedIndex;
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
