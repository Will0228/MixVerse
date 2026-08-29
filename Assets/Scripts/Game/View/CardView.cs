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

        // カードは CardDissolveShader で描く。絵柄もディゾルブ量もそのプロパティ名に合わせる。
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int DissolveThresholdPropertyId = Shader.PropertyToID("_Threshold");

        [SerializeField] private MeshRenderer _faceRenderer;
        [SerializeField] private MeshRenderer _backRenderer;
        [SerializeField] private TextMeshPro _faceLabel;
        [SerializeField] private BoxCollider _collider;

        [Header("Pose")]
        // カードの姿勢は手札（HandView）側の Rotation で決めるため、既定では傾けない。
        // 手札の向きとは別に、カード単体をさらに倒したいときだけ使う。
        [SerializeField] private float _faceUpTiltAngle;

        [Header("Draw Animation")]
        // 引かれたカードは溶けて消えている間に移動するため、移動そのものの演出時間は持たない
        [SerializeField] private float _liftHeight = 0.8f;
        [SerializeField] private float _liftDuration = 0.15f;

        [Header("Dissolve")]
        [SerializeField] private float _dissolveOutDuration = 0.35f;
        [SerializeField] private float _dissolveInDuration = 0.35f;

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
        private MaterialPropertyBlock _backPropertyBlock;
        private Vector3? _faceLocalDirection;

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
        /// 引かれる演出の前半。少し上に浮いてから、その場で溶けて消える。
        /// 消えている間に呼び出し側が移動と表裏の切り替えを済ませ、PlayDissolveInAsync で実体化させる。
        /// </summary>
        public async UniTask PlayDissolveOutAsync(CancellationToken token)
        {
            var lifted = transform.position + Vector3.up * _liftHeight;

            await TweenUtility.MoveAsync(transform, lifted, _liftDuration, token);
            await PlayDissolveAsync(0f, 1f, _dissolveOutDuration, token);
        }

        /// <summary>
        /// 消えた状態から実体化する演出。引いた側の手札へ加えたあとに呼ぶ。
        /// </summary>
        public async UniTask PlayDissolveInAsync(CancellationToken token)
        {
            await PlayDissolveAsync(1f, 0f, _dissolveInDuration, token);

            // 溶けている間は隠していた文字を、表裏の状態に応じて戻す
            SetFaceUpVisibility(IsFaceUp);
        }

        /// <summary>
        /// ディゾルブ量を即座に設定する。0 が実体、1 が完全に消えた状態。
        /// </summary>
        public void SetDissolveAmount(float amount)
        {
            SetDissolveAmount(_faceRenderer, ref _facePropertyBlock, amount);
            SetDissolveAmount(_backRenderer, ref _backPropertyBlock, amount);
        }

        private UniTask PlayDissolveAsync(float from, float to, float duration, CancellationToken token)
        {
            // 文字は TMP 側のシェーダーで描かれるためディゾルブしない。溶けている間は消しておく。
            if (_faceLabel != null)
            {
                _faceLabel.enabled = false;
            }

            return TweenUtility.ValueAsync(from, to, duration, token, SetDissolveAmount);
        }

        /// <summary>
        /// マテリアルを複製すると全カードで共有されなくなるうえ枚数分の実体ができるため、
        /// SetFaceSprite と同じく MaterialPropertyBlock でカードごとに上書きする。
        /// </summary>
        private static void SetDissolveAmount(MeshRenderer meshRenderer, ref MaterialPropertyBlock propertyBlock, float amount)
        {
            if (meshRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();

            meshRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(DissolveThresholdPropertyId, amount);
            meshRenderer.SetPropertyBlock(propertyBlock);
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
            _onPointerEntered.Dispose();
            _onPointerExited.Dispose();
            _onClicked.Dispose();
        }

        private static bool IsRedSuit(Card card) => card.Suit == CardSuit.Hearts || card.Suit == CardSuit.Diamonds;
    }
}
