using UnityEngine;

namespace MixVerse.Game.View
{
    /// <summary>
    /// 相手の手札から引くカードを狙う照準。
    /// 画面上のランダムな位置に現れ、DJ コントローラーのツマミで上下左右に動かす。
    ///
    /// 盤面は 3D なので、照準のスクリーン座標からカメラ越しにレイを飛ばして
    /// どのカードに重なっているかを判定する。
    /// </summary>
    public sealed class TargetCursorView : MonoBehaviour
    {
        [SerializeField] private RectTransform _rectTransform;

        [Header("Movement")]
        [Tooltip("ツマミ1ステップあたりに動くピクセル数。")]
        [SerializeField] private float _stepDistance = 24.0f;

        [Tooltip("出現位置と移動範囲を画面端から離しておく余白（ピクセル）。")]
        [SerializeField] private float _screenMargin = 48.0f;

        [Header("Hit Test")]
        [Tooltip("カードとの重なりを調べるレイの長さ。")]
        [SerializeField] private float _rayDistance = 100.0f;

        private Camera _camera;
        private Vector2 _screenPosition;

        private void Awake()
        {
            // SelectionArrowView と同じく、Awake は SetActive(true) で初めて走ることがあるため
            // ここでは参照のキャッシュだけ行う。
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }
        }

        /// <summary>照準が表示されているか。</summary>
        public bool IsVisible => gameObject.activeSelf;

        /// <summary>
        /// 画面内のランダムな位置に照準を出す。
        /// ワールド座標との変換に使うカメラを渡す。
        /// </summary>
        public void Show(Camera targetCamera)
        {
            _camera = targetCamera;
            _screenPosition = GetRandomScreenPosition();

            gameObject.SetActive(true);
            ApplyPosition();
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// ツマミ1ステップ分だけ動かす。delta は +X が右、+Y が上。
        /// 画面外へは出ないよう端で止める。
        /// </summary>
        public void Move(Vector2 delta)
        {
            if (!IsVisible)
            {
                return;
            }

            _screenPosition = ClampToScreen(_screenPosition + (delta * _stepDistance));
            ApplyPosition();
        }

        /// <summary>
        /// 照準が重なっているカード。何にも重なっていなければ null。
        ///
        /// カードのコライダーは引く対象の手札だけ有効になっているため、
        /// ここでは手前にあるカードを探すだけでよい。
        /// </summary>
        public CardView Raycast()
        {
            if (!IsVisible || _camera == null)
            {
                return null;
            }

            var ray = _camera.ScreenPointToRay(_screenPosition);
            var hits = Physics.RaycastAll(ray, _rayDistance);

            CardView nearest = null;
            var nearestDistance = float.MaxValue;

            foreach (var hit in hits)
            {
                var card = hit.collider.GetComponentInParent<CardView>();

                if (card == null || hit.distance >= nearestDistance)
                {
                    continue;
                }

                nearest = card;
                nearestDistance = hit.distance;
            }

            return nearest;
        }

        /// <summary>
        /// Overlay Canvas 上の UI なので、スクリーン座標をそのまま位置として渡せる。
        /// </summary>
        private void ApplyPosition()
        {
            if (_rectTransform == null)
            {
                return;
            }

            _rectTransform.position = new Vector3(_screenPosition.x, _screenPosition.y, 0f);
        }

        private Vector2 GetRandomScreenPosition()
        {
            var margin = GetMargin();

            return new Vector2(
                Random.Range(margin, Screen.width - margin),
                Random.Range(margin, Screen.height - margin));
        }

        private Vector2 ClampToScreen(Vector2 screenPosition)
        {
            var margin = GetMargin();

            return new Vector2(
                Mathf.Clamp(screenPosition.x, margin, Screen.width - margin),
                Mathf.Clamp(screenPosition.y, margin, Screen.height - margin));
        }

        /// <summary>
        /// 画面が極端に小さいときに余白で潰れないよう、短辺の1/4までに抑える。
        /// </summary>
        private float GetMargin() => Mathf.Min(_screenMargin, Mathf.Min(Screen.width, Screen.height) * 0.25f);
    }
}
