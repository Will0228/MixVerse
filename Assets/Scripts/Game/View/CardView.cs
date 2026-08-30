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

        // カードは CardDissolveShader で描く。絵柄のプロパティ名もそれに合わせる。
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");

        [SerializeField] private MeshRenderer _faceRenderer;
        [SerializeField] private MeshRenderer _backRenderer;
        [SerializeField] private TextMeshPro _faceLabel;
        [SerializeField] private BoxCollider _collider;

        [Header("Pose")]
        // カードの姿勢は手札（HandView）側の Rotation で決めるため、既定では傾けない。
        // 手札の向きとは別に、カード単体をさらに倒したいときだけ使う。
        [SerializeField] private float _faceUpTiltAngle;

        [Header("Draw Animation")]
        // 引かれたカードは真上へ抜けていき、引いた側の手札へ真上から降りてくる。
        // 抜ける高さは、カメラの画角から出るだけの余裕を持たせておく。
        [SerializeField] private float _flyOutHeight = 4f;
        [SerializeField] private float _flyOutDuration = 0.16f;
        [SerializeField] private float _flyInHeight = 4f;
        [SerializeField] private float _flyInDuration = 0.2f;

        [Header("After Image")]
        // 残像の枚数と、姿勢を記録する間隔（秒）。間隔が小さいほど尾が詰まる。
        [SerializeField] private int _afterImageCount = 6;
        [SerializeField] private float _afterImageInterval = 0.016f;
        // 残像がいちばん濃くなる速さ（ワールド単位/秒）。これより遅ければ薄くなり、止まっていれば出ない。
        [SerializeField] private float _afterImageReferenceSpeed = 18f;
        [SerializeField] private float _afterImageMaxAlpha = 0.65f;

        [Header("Discard Toss")]
        [SerializeField] private float _tossDuration = 0.45f;
        [SerializeField] private float _tossArcHeight = 1.2f;
        // 飛行中に加える傾きの最大量（度）。0 で余分な回転なし。
        // 開始時と着地時は必ず 0 に戻るので、任意の値を入れても向きはずれない。
        [SerializeField] private float _tossSpinDegrees;

        private readonly Subject<CardView> _onPointerEntered = new Subject<CardView>();
        private readonly Subject<CardView> _onPointerExited = new Subject<CardView>();
        private readonly Subject<CardView> _onClicked = new Subject<CardView>();

        private MaterialPropertyBlock _facePropertyBlock;
        private Vector3? _faceLocalDirection;

        // 引かれるときだけ使う残像。使い始めるまでは作らない。
        private CardAfterImage _afterImage;

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
        /// 表面が向いているカードローカル方向。
        ///
        /// 表が +Z 側か -Z 側かは Prefab の組み方次第で、Unity の Quad の法線の向きにも依存する。
        /// 決め打ちすると Prefab を作り直したときに裏返るので、実際のメッシュ法線から求める。
        /// </summary>
        public Vector3 FaceLocalDirection
        {
            get
            {
                if (_faceLocalDirection.HasValue)
                {
                    return _faceLocalDirection.Value;
                }

                var direction = Vector3.forward;

                if (_faceRenderer != null)
                {
                    var meshFilter = _faceRenderer.GetComponent<MeshFilter>();
                    var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                    var normals = mesh != null ? mesh.normals : null;

                    if (normals != null && normals.Length > 0)
                    {
                        direction = _faceRenderer.transform.localRotation * normals[0];
                    }
                }

                _faceLocalDirection = direction.normalized;
                return _faceLocalDirection.Value;
            }
        }

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
            _facePropertyBlock.SetTexture(MainTexPropertyId, sprite.texture);
            _faceRenderer.SetPropertyBlock(_facePropertyBlock);

            // 絵柄が入ったらプレースホルダの文字は不要
            if (_faceLabel != null)
            {
                _faceLabel.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 表裏を切り替え、手札としての姿勢に合わせて回転もそろえる。
        /// </summary>
        public void SetFaceUp(bool faceUp)
        {
            SetFaceUpVisibility(faceUp);
            transform.localRotation = GetLocalRotation();
        }

        /// <summary>
        /// 回転はそのままに、表裏の状態とラベルの表示だけ切り替える。
        /// 捨て札のように手札とは別の向きで置く場合、
        /// SetFaceUp だと回転が手札の姿勢へ引き戻されてしまうのでこちらを使う。
        /// </summary>
        public void SetFaceUpVisibility(bool faceUp)
        {
            IsFaceUp = faceUp;

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
        /// 引かれる演出の前半。素早く真上へ抜けていく。抜けきったところで見えなくなるので、
        /// 呼び出し側はその間に移動と表裏の切り替えを済ませ、PlayFlyInAsync で降ろす。
        /// </summary>
        public async UniTask PlayFlyOutAsync(CancellationToken token)
        {
            var lifted = transform.position + (Vector3.up * _flyOutHeight);

            SetAfterImageActive(true);

            try
            {
                await TweenUtility.MoveAsync(transform, lifted, _flyOutDuration, TweenEase.AccelerateIn, token);
            }
            finally
            {
                SetAfterImageActive(false);
            }

            // 画角からは外れている高さだが、抜けたあとも描かれたままだと
            // 手札の向き直しを待つ間ずっと宙に浮いて見えてしまうので消しておく。
            SetVisible(false);
        }

        /// <summary>
        /// 引かれる演出の後半。指定したワールド座標の真上から素早く降りてくる。
        /// </summary>
        public async UniTask PlayFlyInAsync(Vector3 worldPosition, CancellationToken token)
        {
            transform.position = worldPosition + (Vector3.up * _flyInHeight);
            SetVisible(true);

            SetAfterImageActive(true);

            try
            {
                await TweenUtility.MoveAsync(transform, worldPosition, _flyInDuration, TweenEase.DecelerateOut, token);
            }
            finally
            {
                SetAfterImageActive(false);
            }
        }

        /// <summary>
        /// 描画のオン・オフ。移動の途中で見せたくない区間に使う。
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_faceRenderer != null)
            {
                _faceRenderer.enabled = visible;
            }

            if (_backRenderer != null)
            {
                _backRenderer.enabled = visible;
            }

            // 文字は表向きのときだけ出す。裏向きで出すと透けて見えてしまう。
            if (_faceLabel != null)
            {
                _faceLabel.enabled = visible && IsFaceUp;
            }
        }

        /// <summary>
        /// 残像の表示を切り替える。使い始めるまで実体は作らない。
        /// </summary>
        private void SetAfterImageActive(bool active)
        {
            if (_afterImage == null)
            {
                if (!active)
                {
                    return;
                }

                _afterImage = new CardAfterImage(
                    transform,
                    GetVisualRenderers(),
                    _afterImageCount,
                    _afterImageInterval,
                    _afterImageReferenceSpeed,
                    _afterImageMaxAlpha);
            }

            _afterImage.SetActive(active);
        }

        /// <summary>
        /// 残像の元にする板。表と裏をそのまま複製する。
        /// </summary>
        private MeshRenderer[] GetVisualRenderers()
        {
            if (_faceRenderer != null && _backRenderer != null)
            {
                return new[] { _faceRenderer, _backRenderer };
            }

            var single = _faceRenderer != null ? _faceRenderer : _backRenderer;
            return single != null ? new[] { single } : System.Array.Empty<MeshRenderer>();
        }

        private void LateUpdate()
        {
            // 移動はトゥイーンが Update で書くので、その結果の姿勢を残像に記録するのは LateUpdate。
            _afterImage?.Tick(Time.deltaTime);
        }

        /// <summary>
        /// 捨て札置き場へ放り投げる演出。弧を描きながら回転して落ちる。
        /// 呼び出し前に捨て札置き場の子にしておくこと（ローカル座標で動かす）。
        /// </summary>
        public UniTask PlayDiscardAsync(Vector3 localPosition, Quaternion localRotation, CancellationToken token)
            => TweenUtility.TossLocalAsync(
                transform, localPosition, localRotation,
                _tossArcHeight, _tossSpinDegrees, _tossDuration, token);

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
            _afterImage?.Dispose();

            _onPointerEntered.Dispose();
            _onPointerExited.Dispose();
            _onClicked.Dispose();
        }

        private static bool IsRedSuit(Card card) => card.Suit == CardSuit.Hearts || card.Suit == CardSuit.Diamonds;
    }
}
