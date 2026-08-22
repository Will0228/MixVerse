using UnityEngine;

namespace MixVerse.Game.View
{
    /// <summary>
    /// 選ぼうとしているカードの上に表示する矢印。
    /// 盤面は 3D だが矢印は Overlay Canvas 上の UI なので、
    /// カードのワールド座標をスクリーン座標へ変換して追従させる。
    /// </summary>
    public sealed class SelectionArrowView : MonoBehaviour
    {
        [SerializeField] private RectTransform _rectTransform;
        [SerializeField] private float _bobHeight = 12.0f;
        [SerializeField] private float _bobSpeed = 4.0f;

        private Camera _camera;
        private Transform _target;
        private Vector3 _targetWorldOffset;

        private void Awake()
        {
            // Awake は Show() の SetActive(true) で初めて走ることがあるため、
            // ここで Hide() を呼ぶと表示した直後に消えてしまう。参照のキャッシュだけ行う。
            if (_rectTransform == null)
            {
                _rectTransform = GetComponent<RectTransform>();
            }
        }

        /// <summary>
        /// ワールド座標の変換に使うカメラを渡す。
        /// </summary>
        public void Initialize(Camera targetCamera)
        {
            _camera = targetCamera;
        }

        /// <summary>
        /// 指定したカードの上に矢印を出す。
        /// </summary>
        public void Show(CardView card)
        {
            if (card == null)
            {
                Hide();
                return;
            }

            _target = card.transform;
            _targetWorldOffset = card.IndicatorWorldPosition - card.transform.position;

            gameObject.SetActive(true);
            UpdatePosition();
        }

        public void Hide()
        {
            _target = null;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                return;
            }

            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (_target == null || _camera == null || _rectTransform == null)
            {
                return;
            }

            var worldPosition = _target.position + _targetWorldOffset;
            var screenPosition = _camera.WorldToScreenPoint(worldPosition);

            // カメラの背面に回った場合は表示しない
            if (screenPosition.z < 0f)
            {
                gameObject.SetActive(false);
                return;
            }

            // ふわふわと上下させて、選択中であることを分かりやすくする
            var bob = Mathf.Sin(Time.time * _bobSpeed) * _bobHeight;

            _rectTransform.position = new Vector3(screenPosition.x, screenPosition.y + bob, 0f);
        }
    }
}
