using MixVerse.Game;
using MixVerse.Game.Model;
using MixVerse.Home;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MixVerse
{
    public sealed class UndisposableLifetimeScope : LifetimeScope
    {
        [SerializeField] private HomeView _homeView;
        [SerializeField] private GameView _gameView;
        
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            
            // 画面の切り替えは ScreenNavigator が受け持つので、起点もここになる
            builder.RegisterEntryPoint<ScreenNavigator>().AsSelf();
            
            builder.Register<HomeController>(Lifetime.Singleton);
            builder.RegisterComponent(_homeView);
            builder.Register<HomePresenter>(Lifetime.Singleton);
            
            builder.Register<GameController>(Lifetime.Singleton);
            builder.RegisterComponent(_gameView);
            builder.Register<GamePresenter>(Lifetime.Singleton);
            builder.Register<OldMaidGame>(Lifetime.Singleton);
            builder.Register<CpuStrategy>(Lifetime.Singleton);
        }
    }
}