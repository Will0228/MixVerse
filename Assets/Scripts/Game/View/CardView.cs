using System.Threading;
using Cysharp.Threading.Tasks;
using MixVerse.Game.Model;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MixVerse.Game.View
{
    /// <summary>
    /// カード1枚の見た目と入力。
    /// 表面と裏面の Quad を持ち、Y 軸 180 度の回転で裏返す。
    /// </summary>
    public sealed class CardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        /// <summary>
        /// 裏向きにするための追加回転。カード自身の Y 軸まわりに 180 度ひっくり返す。
        /// </summary>
        public static readonly Quaternion FaceDownFlip = Quaternion.Euler(0f, 180f, 0f);

        private static readonly int BaseMapPropertyId = Shader.PropertyToID("_BaseMap");

        [SerializeField] private MeshRenderer _faceRenderer;
        [SerializeField] private MeshRenderer _backRenderer;
        [SerializeField] private TextMeshPro _faceLabel;
        [SerializeField] private BoxCollider _collider;

        [Header("Pose")]
        // カードの姿勢は手札（HandView）側の Rotation で決めるため、既定では傾けない。
        // 手札の向きとは別に、カード単体をさらに倒したいときだけ使う。
        [SerializeField] private float _faceUpTiltAngle;

        [Header("Draw Animation")]
        [SerializeField] private float _liftHeight = 0.8f;
        [SerializeField] private float _liftDuration = 0.15f;
        [SerializeField] private float _moveDuration = 0.35f;

        private readonly Subject<CardView> _onPointerEntered = new Subject<CardView>();
        private readonly Subject<CardView> _onPointerExited = new Subject<CardView>();
        private readonly Subject<CardView> _onClicked = new Subject<CardView>();

        private MaterialPropertyBlock _facePropertyBlock;

        /// <summary>カーソルが乗った。矢印 UI の表示に使う。</summary>
        public Observable<CardView> OnPointerEntered => _onPointerEntered;

        /// <summary>カーソルが外れた。</summary>
        public Observable<CardView> OnPointerExited => _onPointerExited;

        /// <summary>クリックで選択が確定した。</summary>
        public Observable<CardView> OnClicked => _onClicked;

        /// <summary>このカードが表しているモデル上のカード。</summary>
        public Card Card { get; private set; }

        /// <summary>手札の中での位置。ドロー時に Model へ渡すインデックスになる。</summary>
        public int HandIndex { get; set; }

        /// <summary>自分の手番で、かつ引く対象の手札のときだけ true。</summary>
        public bool IsSelectable { get; set; }

        public bool IsFaceUp { get; private set; }

        /// <summary>カードの上端あたりのワールド座標。矢印 UI の表示位置に使う。</summary>
        public Vector3 IndicatorWorldPosition => transform.position + Vector3.up * 0.6f;

        /// <summary>
        /// このカードが表す内容を設定する。
        /// </summary>
        public void SetCard(Card card)
        {
            Card = card;

            if (_faceLabel != null)
            {
                _faceLabel.text = card.ToShortString();
                _faceLabel.color = IsRedSuit(card) ? new Color(0.78f, 0.12f, 0.16f) : new Color(0.12f, 0.12f, 0.14f);
            }

            name = "Card_" + card.ToShortString();
        }

        /// <summary>
        /// 絵柄画像を差し込む。マテリアルを複製しないよう MaterialPropertyBlock を使う。
        /// </summary>
        public void SetFaceSprite(Sprite sprite)
        {
            if (_faceRenderer == null || sprite == null)
            {
                return;
            }

            _facePropertyBlock ??= new MaterialPropertyBlock();

            _faceRenderer.GetPropertyBlock(_facePropertyBlock);
            _facePropertyBlock.SetTexture(BaseMapPropertyId, sprite.texture);
            _faceRenderer.SetPropertyBlock(_facePropertyBlock);

            // 絵柄が入ったらプレースホルダの文字は不要
            if (_faceLabel != null)
            {
                _faceLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 表裏を切り替える。回転はローカル回転で表現する。
        /// </summary>
        public void SetFaceUp(bool faceUp)
        {
            IsFaceUp = faceUp;
            transform.localRotation = GetLocalRotation();

            // TMP の既定シェーダーは両面描画なので、裏向きのときは
            // 回転だけに任せず明示的に消さないと文字が透けて見えてしまう。
            if (_faceLabel != null)
            {
                _faceLabel.enabled = faceUp;
            }
        }

        /// <summary>
        /// 現在の表裏に応じたローカル回転。
        /// </summary>
        public Quaternion GetLocalRotation()
        {
            var faceUpRotation = Quaternion.Euler(_faceUpTiltAngle, 0f, 0f);
            return IsFaceUp ? faceUpRotation : faceUpRotation * FaceDownFlip;
        }

        /// <summary>
        /// クリック判定の有効・無効。上がったプレイヤーのカードなどで使う。
        /// </summary>
        public void SetRaycastEnabled(bool enabled)
        {
            if (_collider != null)
            {
                _collider.enabled = enabled;
            }
        }

        /// <summary>
        /// 引かれる演出。少し上に浮いてから引いた側の手札へ移動する。
        /// </summary>
        public async UniTask PlayDrawAsync(Vector3 destination, CancellationToken token)
        {
            var lifted = transform.position + Vector3.up * _liftHeight;

            await TweenUtility.MoveAsync(transform, lifted, _liftDuration, token);
            await TweenUtility.MoveAsync(transform, destination, _moveDuration, token);
        }

        /// <summary>
        /// 捨て札置き場へ飛ばす演出。
        /// </summary>
        public UniTask PlayDiscardAsync(Vector3 destination, CancellationToken token)
            => TweenUtility.MoveAsync(transform, destination, _moveDuration, token);

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!IsSelectable)
            {
                return;
            }

            _onPointerEntered.OnNext(this);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!IsSelectable)
            {
                return;
            }

            _onPointerExited.OnNext(this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsSelectable)
            {
                return;
            }

            _onClicked.OnNext(this);
        }

        private void OnDestroy()
        {
            _onPointerEntered.Dispose();
            _onPointerExited.Dispose();
            _onClicked.Dispose();
        }

        private static bool IsRedSuit(Card card) => card.Suit == CardSuit.Hearts || card.Suit == CardSuit.Diamonds;
    }
}
