using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MixVerse.Game.Model;
using MixVerse.Game.View;
using R3;
using TMPro;
using UnityEngine;

namespace MixVerse.Game
{
    /// <summary>
    /// ゲーム画面のルート。3D の盤面と Overlay Canvas の HUD をまとめて持つ。
    /// Model を一切知らず、渡された情報をそのまま描画する。
    /// </summary>
    public sealed class GameView : MonoBehaviour
    {
        /// <summary>CUE でどの相手も向いていない（正面を向いている）ことを表す番兵。</summary>
        private const int NoClapCameraTarget = -1;

        [Header("Board")]
        [SerializeField] private CardView _cardPrefab;
        [SerializeField] private HandView[] _handViews;
        [SerializeField] private Transform _discardPile;
        [SerializeField] private Camera _boardCamera;
        [SerializeField] private ClapHandsView _clapHandsView;

        [Header("Sound")]
        [SerializeField] private AudioSource _bgmSource;

        [Header("Hud")]
        [SerializeField] private CanvasGroup _canvasGroup;
        // 3D の盤面は CanvasGroup ではフェードできないため、画面全体を覆う黒板を別に用意する
        [SerializeField] private CanvasGroup _fadeOverlayGroup;
        [SerializeField] private SelectionArrowView _selectionArrow;
        [SerializeField] private TextMeshProUGUI _turnLabel;
        [SerializeField] private TextMeshProUGUI _resultLabel;

        [Header("Discard Pile")]
        // そろったカードは盤面中央に残す。きれいに重ねると不自然なので位置と向きを散らす。
        [SerializeField] private float _discardScatterRadius = 0.35f;
        [SerializeField] private float _discardStackHeight = 0.006f;
        [SerializeField] private float _discardTiltJitter = 8f;

        [Header("Timing")]
        [SerializeField] private float _fadeDuration = 0.6f;
        [SerializeField] private float _dealInterval = 0.02f;
        [SerializeField] private float _arrangeDuration = 0.2f;

        [Header("Draw Camera")]
        // 引く対象の手札に DrawCameraPoint が設定されているときだけ演出する
        [SerializeField] private DrawCameraSettings _drawCameraSettings;
        // カードを引く前の、カメラを戻す先の位置・向き。未設定ならカメラの現在位置を使う
        [SerializeField] private Transform _defaultCameraPoint;

        private readonly Subject<CardView> _onCardClicked = new Subject<CardView>();
        private readonly CompositeDisposable _cardSubscriptions = new CompositeDisposable();
        private readonly List<CardView> _spawnedCards = new List<CardView>();

        // 選択可能になった瞬間からカードが自分の手札に加わるまでカメラを専用位置に置いておくための状態。
        // BeginDrawSelectionAsync で開始し、PlayDrawAsync 側で終了させる。
        private HandView _activeDrawCameraHand;
        private Vector3 _drawCameraHomePosition;
        private Quaternion _drawCameraHomeRotation;
        private Quaternion _drawHandHomeRotation;

        // CUE ボタンで相手向きに寄せている間の状態。ToggleClapCamera で開始・終了を切り替える。
        // 正面を向いている間は -1。
        private int _clapCameraTargetIndex = NoClapCameraTarget;
        private CancellationTokenSource _clapCameraCts;

        /// <summary>捨て札置き場に積まれた枚数。積み上げる高さの計算に使う。</summary>
        private int _discardStackCount;

        /// <summary>引く対象のカードがクリックされた。</summary>
        public Observable<CardView> OnCardClicked => _onCardClicked;

        public int HandCount => _handViews.Length;

        private Camera BoardCamera => _boardCamera != null ? _boardCamera : Camera.main;

        /// <summary>
        /// 画面を有効化してフェードインする。
        /// </summary>
        public async UniTask ShowAsync(CancellationToken token)
        {
            gameObject.SetActive(true);

            if (_bgmSource != null && !_bgmSource.isPlaying)
            {
                _bgmSource.Play();
            }

            if (_selectionArrow != null)
            {
                _selectionArrow.Initialize(BoardCamera);
                _selectionArrow.Hide();
            }

            if (_resultLabel != null)
            {
                _resultLabel.gameObject.SetActive(false);
            }

            // 黒板を消しながら HUD を出すことで、盤面ごとフェードインしているように見せる
            await UniTask.WhenAll(
                TweenUtility.FadeAsync(_fadeOverlayGroup, 1f, 0f, _fadeDuration, token),
                TweenUtility.FadeAsync(_canvasGroup, 0f, 1f, _fadeDuration, token));
        }

        /// <summary>
        /// 手札のカードを生成して配る。
        /// </summary>
        public async UniTask DealAsync(IReadOnlyList<PlayerHand> hands, CancellationToken token)
        {
            ClearCards();

            // 全員に1枚ずつ順番に配ることで、実際に配っているように見せる
            var maxCount = 0;
            foreach (var hand in hands)
            {
                maxCount = Mathf.Max(maxCount, hand.Count);
            }

            for (var i = 0; i < maxCount; i++)
            {
                for (var playerIndex = 0; playerIndex < hands.Count && playerIndex < _handViews.Length; playerIndex++)
                {
                    var hand = hands[playerIndex];
                    if (i >= hand.Count)
                    {
                        continue;
                    }

                    var handView = _handViews[playerIndex];
                    var cardView = CreateCard(hand[i]);

                    cardView.transform.position = _discardPile != null ? _discardPile.position : Vector3.zero;
                    handView.Add(cardView);
                    handView.ArrangeImmediate();
                }

                await TweenUtility.WaitAsync(_dealInterval, token);
            }
        }

        /// <summary>
        /// 指定プレイヤーの手札から、揃ったペアを捨て札置き場へ飛ばす。
        /// </summary>
        public async UniTask DiscardPairsAsync(int playerIndex, IReadOnlyList<Card> discarded, CancellationToken token)
        {
            if (discarded == null || discarded.Count == 0)
            {
                return;
            }

            var handView = _handViews[playerIndex];
            var targets = new List<CardView>(discarded.Count);

            foreach (var card in discarded)
            {
                var cardView = FindCardView(handView, card);
                if (cardView == null)
                {
                    continue;
                }

                handView.Remove(cardView);
                targets.Add(cardView);
            }

            var pileParent = _discardPile != null ? _discardPile : transform;
            var moves = new List<UniTask>(targets.Count);

            foreach (var cardView in targets)
            {
                // SetFaceUp だと回転が手札の姿勢へ戻ってしまうので、表示だけ切り替える
                cardView.SetFaceUpVisibility(true);
                cardView.SetRaycastEnabled(false);
                cardView.IsSelectable = false;

                // ローカル座標で動かすため、先に捨て札置き場の子にする（見た目の位置は維持）
                cardView.transform.SetParent(pileParent, true);

                moves.Add(cardView.PlayDiscardAsync(
                    GetDiscardLocalPosition(_discardStackCount),
                    GetDiscardLocalRotation(cardView),
                    token));

                _discardStackCount++;
            }

            await UniTask.WhenAll(moves);

            // 破棄せずそのまま盤面に残す。次の対局開始時に ClearCards でまとめて片付ける。
            await handView.ArrangeAsync(_arrangeDuration, token);
        }

        /// <summary>
        /// 引かれたカードが上に浮いてから溶けて消え、引いた側の手札で実体化する演出。
        /// </summary>
        public async UniTask PlayDrawAsync(DrawResult result, CancellationToken token)
        {
            var fromHand = _handViews[result.TargetIndex];
            var toHand = _handViews[result.DrawerIndex];

            var cardView = fromHand.TakeAt(result.DrawnCardIndex);
            cardView.SetRaycastEnabled(false);
            cardView.IsSelectable = false;

            // 移動中は手札の子から外し、ワールド座標で動かす
            cardView.transform.SetParent(transform, true);

            await cardView.PlayDissolveOutAsync(token);

            // 選択可能になった瞬間から寄せていたカメラは、カードが消えているこの間に戻す。
            // 実体化を引いた側の手札で見せたいので、カメラを戻すのはディゾルブインより前。
            if (_activeDrawCameraHand == fromHand)
            {
                _activeDrawCameraHand = null;
                await PlayDrawCameraOutAsync(fromHand, _drawCameraHomePosition, _drawCameraHomeRotation, _drawHandHomeRotation, token);
            }

            // 移動と表裏の切り替えも消えている間に済ませるので、
            // 相手の手札から自分の手札へ飛ぶ様子や裏返る瞬間は見えない
            cardView.transform.position = toHand.GetIncomingWorldPosition();

            toHand.Add(cardView);
            cardView.SetFaceUp(toHand.IsFaceUp);

            await cardView.PlayDissolveInAsync(token);

            await UniTask.WhenAll(
                toHand.ArrangeAsync(_arrangeDuration, token),
                fromHand.ArrangeAsync(_arrangeDuration, token));
        }

        /// <summary>
        /// 引く対象の手札を選択可能にする。専用カメラ位置が設定されていれば、
        /// そこに寄せつつ手札をこちらへ向け、カードが引かれて自分の手札に加わるまでその位置を保つ。
        /// </summary>
        public async UniTask BeginDrawSelectionAsync(int targetIndex, CancellationToken token)
        {
            SetSelectableHand(targetIndex);

            var hand = _handViews[targetIndex];

            if (_drawCameraSettings == null || hand.DrawCameraPoint == null)
            {
                return;
            }

            _activeDrawCameraHand = hand;

            if (_defaultCameraPoint != null)
            {
                _drawCameraHomePosition = _defaultCameraPoint.position;
                _drawCameraHomeRotation = _defaultCameraPoint.rotation;
            }
            else
            {
                var cameraTransform = BoardCamera.transform;
                _drawCameraHomePosition = cameraTransform.position;
                _drawCameraHomeRotation = cameraTransform.rotation;
            }

            _drawHandHomeRotation = hand.transform.localRotation;

            await PlayDrawCameraInAsync(hand, token);
        }

        /// <summary>
        /// カメラを相手の手札の専用位置へ寄せつつ、その手札をこちらへ向ける。
        /// </summary>
        private UniTask PlayDrawCameraInAsync(HandView fromHand, CancellationToken token)
        {
            var point = fromHand.DrawCameraPoint;

            return UniTask.WhenAll(
                TweenUtility.MoveAsync(BoardCamera.transform, point.position, point.rotation, _drawCameraSettings.TransitionDuration, token),
                TweenUtility.MoveLocalAsync(fromHand.transform, fromHand.transform.localPosition, fromHand.DrawFacingRotation, _drawCameraSettings.TransitionDuration, token));
        }

        /// <summary>
        /// カメラと相手の手札を、演出前の位置・向きへ戻す。
        /// </summary>
        private UniTask PlayDrawCameraOutAsync(
            HandView fromHand, Vector3 cameraPosition, Quaternion cameraRotation, Quaternion handRotation, CancellationToken token)
        {
            return UniTask.WhenAll(
                TweenUtility.MoveAsync(BoardCamera.transform, cameraPosition, cameraRotation, _drawCameraSettings.TransitionDuration, token),
                TweenUtility.MoveLocalAsync(fromHand.transform, fromHand.transform.localPosition, handRotation, _drawCameraSettings.TransitionDuration, token));
        }

        /// <summary>
        /// 引く対象の手札だけをクリックできるようにする。
        /// </summary>
        public void SetSelectableHand(int playerIndex)
        {
            for (var i = 0; i < _handViews.Length; i++)
            {
                _handViews[i].SetSelectable(i == playerIndex);
            }
        }

        /// <summary>
        /// すべての手札のクリックを無効にする。CPU の手番中などに使う。
        /// </summary>
        public void ClearSelectable()
        {
            foreach (var handView in _handViews)
            {
                handView.SetSelectable(false);
            }
        }

        /// <summary>
        /// CPU が選んだカードにも矢印を出して、何を選んだか分かるようにする。
        /// </summary>
        public void ShowArrowAt(int playerIndex, int cardIndex)
        {
            var handView = _handViews[playerIndex];

            if (cardIndex < 0 || cardIndex >= handView.Count)
            {
                HideArrow();
                return;
            }

            _selectionArrow?.Show(handView.Cards[cardIndex]);
        }

        public void HideArrow() => _selectionArrow?.Hide();

        public void SetTurnText(string text)
        {
            if (_turnLabel != null)
            {
                _turnLabel.text = text;
            }
        }

        public void ShowResult(string text)
        {
            if (_resultLabel == null)
            {
                return;
            }

            _resultLabel.gameObject.SetActive(true);
            _resultLabel.text = text;
        }

        public UniTask WaitAsync(float seconds, CancellationToken token) => TweenUtility.WaitAsync(seconds, token);

        /// <summary>CUE ボタンで拍手する手が表示中か。</summary>
        public bool IsClapHandsVisible => _clapHandsView != null && _clapHandsView.IsVisible;

        /// <summary>カード選択中（SYNC でカメラが専用位置にある間）か。CUE を受け付けるかの判定に使う。</summary>
        public bool IsDrawCameraActive => _activeDrawCameraHand != null;

        /// <summary>CUE でカメラが相手向きになっているか。SYNC を受け付けるかの判定に使う。</summary>
        public bool IsClapCameraActive => _clapCameraTargetIndex != NoClapCameraTarget;

        /// <summary>CUE で向いている相手のプレイヤー番号。正面を向いていれば -1。</summary>
        public int ClapCameraTargetIndex => _clapCameraTargetIndex;

        /// <summary>
        /// CUE ボタンで、カメラを対象プレイヤーの手札用位置（カード選択時と同じ DrawCameraPoint）へ
        /// 移動しつつ拍手する手を出す。同じ相手に対してもう一度呼ぶと真正面へ戻す。
        /// カード選択中は Presenter 側で呼び出さないようにガードしてもらう想定。
        /// </summary>
        public void ToggleClapCamera(int targetIndex, CancellationToken token)
        {
            // 同じ相手を向いている状態で押されたら正面へ戻す
            var isTurningToTarget = _clapCameraTargetIndex != targetIndex;
            _clapCameraTargetIndex = isTurningToTarget ? targetIndex : NoClapCameraTarget;

            _clapCameraCts?.Cancel();
            _clapCameraCts?.Dispose();
            _clapCameraCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            if (isTurningToTarget)
            {
                var point = GetClapCameraPoint(targetIndex);
                _clapHandsView?.Show();
                PlayClapCameraAsync(point, _clapCameraCts.Token).Forget();
            }
            else
            {
                _clapHandsView?.Hide();
                PlayClapCameraAsync(_defaultCameraPoint, _clapCameraCts.Token).Forget();
            }
        }

        /// <summary>
        /// 拍手時に向く先。カード選択時と同じく、対象プレイヤーの手札に設定された DrawCameraPoint を使う。
        /// </summary>
        private Transform GetClapCameraPoint(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _handViews.Length)
            {
                return null;
            }

            return _handViews[targetIndex].DrawCameraPoint;
        }

        /// <summary>スクラッチの回転方向に合わせて、拍手する手を閉じる（時計回り）か開く（反時計回り）かを切り替える。</summary>
        public void SetHandsClosed(bool isClosed) => _clapHandsView?.SetHandsClosed(isClosed);

        /// <summary>
        /// カメラを指定位置へ移動させる。位置が未設定、または演出タイミングが未設定なら何もしない。
        /// </summary>
        private async UniTask PlayClapCameraAsync(Transform point, CancellationToken token)
        {
            if (point == null || _drawCameraSettings == null)
            {
                return;
            }

            try
            {
                await TweenUtility.MoveAsync(BoardCamera.transform, point.position, point.rotation, _drawCameraSettings.TransitionDuration, token);
            }
            catch (System.OperationCanceledException)
            {
                // 連打などで次の切り替えに割り込まれた場合はここに来る
            }
        }

        /// <summary>
        /// 捨て札置き場での位置。中心から少しずらしつつ、積むほど高くする。
        /// 見た目だけの乱数なので、Model 側の seed 付き乱数とは分けて UnityEngine.Random を使う。
        /// </summary>
        private Vector3 GetDiscardLocalPosition(int stackIndex)
        {
            var offset = Random.insideUnitCircle * _discardScatterRadius;
            return new Vector3(offset.x, stackIndex * _discardStackHeight, offset.y);
        }

        /// <summary>
        /// 捨て札置き場での向き。柄が見えるよう表を真上に向けたうえで、向きと傾きを散らす。
        /// </summary>
        private Quaternion GetDiscardLocalRotation(CardView cardView)
        {
            // 表が +Z 側か -Z 側かは Prefab の組み方で変わるため、
            // 角度を決め打ちせず、実測した表面の向きを真上へ合わせる回転を求める。
            var facing = Quaternion.FromToRotation(cardView.FaceLocalDirection, Vector3.up);

            var tilt = Quaternion.Euler(
                Random.Range(-_discardTiltJitter, _discardTiltJitter),
                0f,
                Random.Range(-_discardTiltJitter, _discardTiltJitter));

            var spin = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            return spin * tilt * facing;
        }

        private CardView CreateCard(Card card)
        {
            var cardView = Instantiate(_cardPrefab, transform);
            cardView.SetCard(card);
            cardView.IsSelectable = false;
            cardView.SetRaycastEnabled(false);

            // 生成したカードの入力を GameView 側へ集約する
            cardView.OnClicked.Subscribe(_onCardClicked.OnNext).AddTo(_cardSubscriptions);
            cardView.OnPointerEntered.Subscribe(view => _selectionArrow?.Show(view)).AddTo(_cardSubscriptions);
            cardView.OnPointerExited.Subscribe(_ => _selectionArrow?.Hide()).AddTo(_cardSubscriptions);

            _spawnedCards.Add(cardView);

            return cardView;
        }

        private static CardView FindCardView(HandView handView, Card card)
        {
            foreach (var cardView in handView.Cards)
            {
                if (cardView.Card == card)
                {
                    return cardView;
                }
            }

            return null;
        }

        private void ClearCards()
        {
            _cardSubscriptions.Clear();
            _discardStackCount = 0;

            foreach (var cardView in _spawnedCards)
            {
                if (cardView != null)
                {
                    Destroy(cardView.gameObject);
                }
            }

            _spawnedCards.Clear();

            foreach (var handView in _handViews)
            {
                handView.Clear();
            }
        }

        private void OnDestroy()
        {
            _cardSubscriptions.Dispose();
            _onCardClicked.Dispose();

            _clapCameraCts?.Cancel();
            _clapCameraCts?.Dispose();
        }
    }
}
