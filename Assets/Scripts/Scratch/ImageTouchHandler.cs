using R3;
using UnityEngine;
using UnityEngine.EventSystems;

public class ImageTouchHandler : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    private RectTransform _rectTransform;

    private Subject<Vector2> _onTouchPositionSubject = new Subject<Vector2>();
    public Observable<Vector2> OnTouchPositionAsObservable => _onTouchPositionSubject.AsObservable();

    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        ProcessTouch(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        ProcessTouch(eventData);
    }

    private void ProcessTouch(PointerEventData eventData)
    {
        // 1. 画面上のスクリーン座標（ピクセル）を、UI（RawImage）内のローカル座標に変換する
        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out localPoint))
        {
            // 2. ローカル座標を、UIのサイズを元に 0.0 ～ 1.0 の「UV座標」に変換する
            // Rectの左下が (0,0)、右上が (Width, Height) になっているため、サイズで割るだけでUVになります
            float uvX = (localPoint.x - _rectTransform.rect.xMin) / _rectTransform.rect.width;
            float uvY = (localPoint.y - _rectTransform.rect.yMin) / _rectTransform.rect.height;

            Vector2 touchUV = new Vector2(uvX, uvY);
            _onTouchPositionSubject.OnNext(touchUV);
        }
    }
}
