using R3;

namespace MixVerse
{
    public abstract class ControllerBase
    {
        protected CompositeDisposable disposable = new();

        public virtual void ChangeController()
        {
            disposable = new();
        }

        public virtual void LeaveController()
        {
            if (disposable.IsDisposed)
            {
                return;
            }
            
            disposable.Dispose();
        }
    }
}