using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace MixVerse.Game
{
    /// <summary>
    /// ババ抜きの進行役。ScreenNavigator から ChangeController を呼ばれて動き出す。
    /// </summary>
    public sealed class GameController : ControllerBase
    {
        private readonly GamePresenter _presenter;

        private CancellationTokenSource _cancellationTokenSource;

        [Inject]
        public GameController(GamePresenter presenter)
        {
            _presenter = presenter;
        }

        public override void ChangeController()
        {
            base.ChangeController();

            _cancellationTokenSource = new CancellationTokenSource();

            // CUE ボタンの拍手はターン進行と独立して常に受け付けるため、ここで一度だけ購読する
            _presenter.SetupClapGesture(disposable, _cancellationTokenSource.Token);

            PlayAsync(_cancellationTokenSource.Token).Forget();
        }

        public override void LeaveController()
        {
            // 進行中の演出を止めてから購読を破棄する
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }

            base.LeaveController();
        }

        /// <summary>
        /// 配札から決着までを一続きの非同期処理として回す。
        /// </summary>
        private async UniTaskVoid PlayAsync(CancellationToken token)
        {
            try
            {
                await _presenter.PrepareAsync(Environment.TickCount, token);
                await _presenter.DiscardInitialPairsAsync(token);

                var turns = 0;

                while (!_presenter.IsGameOver)
                {
                    await _presenter.PlayTurnAsync(token);
                }

                _presenter.ShowResult();
            }
            catch (OperationCanceledException)
            {
                // 画面遷移などで中断された場合は何もしない
            }
        }
    }
}
