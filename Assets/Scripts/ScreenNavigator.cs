using VContainer;
using VContainer.Unity;

namespace MixVerse
{
    /// <summary>
    /// 画面（Controller）の切り替えを一手に引き受ける調停役。
    /// ControllerBase が用意している ChangeController / LeaveController を実際に呼ぶのはここだけ。
    ///
    /// Controller を直接コンストラクタで受け取ると
    /// HomeController ⇄ ScreenNavigator の循環依存になるため、
    /// IObjectResolver を経由して必要になった時点で解決する。
    /// </summary>
    public sealed class ScreenNavigator : IInitializable
    {
        private readonly IObjectResolver _resolver;

        private ControllerBase _current;

        [Inject]
        public ScreenNavigator(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>起動時は Home から始める。</summary>
        public void Initialize() => Navigate<Home.HomeController>();

        /// <summary>
        /// 現在の画面を終了させて、指定した画面へ切り替える。
        /// </summary>
        public void Navigate<T>() where T : ControllerBase
        {
            _current?.LeaveController();

            _current = _resolver.Resolve<T>();
            _current.ChangeController();
        }
    }
}
