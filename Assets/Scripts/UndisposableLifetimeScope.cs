using MixVerse.Game;
using MixVerse.Game.Model;
using MixVerse.Home;
using MixVerse.Midi;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace MixVerse
{
    public sealed class UndisposableLifetimeScope : LifetimeScope
    {
        [SerializeField] private HomeView _homeView;
        [SerializeField] private GameView _gameView;
        [SerializeField] private DjControllerInput _djControllerInput;
        
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
            builder.Register<CpuHealth>(Lifetime.Singleton);
            builder.Register<CpuTalkScript>(Lifetime.Singleton);
            
            RegisterDjController(builder);
        }
        
        /// <summary>
        /// DJ コントローラーは任意接続なので、未設定でもコンテナが壊れないようにする。
        /// null が解決された場合、GamePresenter はマウス操作のみにフォールバックする。
        /// </summary>
        private void RegisterDjController(IContainerBuilder builder)
        {
            if (_djControllerInput != null)
            {
                builder.RegisterComponent(_djControllerInput);
                return;
            }
            
            Debug.LogWarning("[MixVerse] DjControllerInput が未設定です。カード選択はマウスのみになります。");
            builder.Register<DjControllerInput>(_ => null, Lifetime.Singleton);
        }
    }
}