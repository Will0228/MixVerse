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
        [Header("Board")]
        [SerializeField] private CardView _cardPrefab;
        [SerializeField] private HandView[] _handViews;
        [SerializeField] private Transform _discardPile;
        [SerializeField] private Camera _boardCamera;

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

        private readonly Subject<CardView> _onCardClicked = new Subject<CardView>();
        private readonly CompositeDisposable _cardSubscriptions = new CompositeDisposable();
        private readonly List<CardView> _spawnedCards = new List<CardView>();

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
        /// 引かれたカードが上に浮いてから、引いた側の手札へ移動する演出。
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

            var destination = toHand.GetIncomingWorldPosition();
            await cardView.PlayDrawAsync(destination, token);

            toHand.Add(cardView);
            cardView.SetFaceUp(toHand.IsFaceUp);

            await UniTask.WhenAll(
                toHand.ArrangeAsync(_arrangeDuration, token),
                fromHand.ArrangeAsync(_arrangeDuration, token));
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
        }
    }
}
