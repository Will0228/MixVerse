using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace MixVerse.Home
{
    public sealed class HomeView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Button _startButton;
        [SerializeField] private Button _quitButton;
        
        public Observable<Unit> OnStartButtonClicked => _startButton.OnClickAsObservable();
        public Observable<Unit> OnQuitButtonClicked => _quitButton.OnClickAsObservable();

        private bool _isFading;

        public async UniTask StartGameAsync(CancellationToken token)
        {
            if (_isFading)
            {
                return;
            }
            
            _isFading = true;
            var duration = 1.0f;
            var elapsedTime = 0f;

            _canvasGroup.alpha = 1.0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.deltaTime;
                _canvasGroup.alpha = Mathf.Lerp(1.0f, 0.0f, elapsedTime / duration);
                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }

            _isFading = false;
            _canvasGroup.alpha = 0.0f;
            
            gameObject.SetActive(false);
        }
    }
}