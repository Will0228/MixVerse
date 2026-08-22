using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using VContainer;

namespace MixVerse.Home
{
    public sealed class HomePresenter
    {
        private readonly HomeView _view;
        
        public Observable<Unit> OnStartButtonClicked => _view.OnStartButtonClicked;
        public Observable<Unit> OnQuitButtonClicked => _view.OnQuitButtonClicked;

        [Inject]
        public HomePresenter(HomeView view)
        {
            _view = view;
        }
        
        public async UniTask StartGameAsync(CancellationToken token) => await _view.StartGameAsync(token);
    }
}