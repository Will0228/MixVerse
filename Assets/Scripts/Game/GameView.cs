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
        /// <summary>照準がどのカードにも重なっていないことを表す番兵。</summary>
        public const int NoTargetedCard = -1;

        [Header("Board")]
        [SerializeField] private CardView _cardPrefab;
        [SerializeField] private HandView[] _handViews;
        [SerializeField] private Transform _discardPile;
        [SerializeField] private Camera _boardCamera;
        [SerializeField] private ClapHandsView _clapHandsView;
        // 手札と同じ並び（0 がプレイヤー）。体力が初めて減った CPU をここで変身させる。
        // プレイヤーの枠と、変身しないキャラクターの枠は空のままでよい。
        [SerializeField] private GlitchMorphEffect[] _characterMorphs;

        [Header("Sound")]
        [SerializeField] private AudioSource _bgmSource;

        // CPU の話し声。手札と同じ並び（0 がプレイヤー）で、プレイヤーの枠は空のままでよい。
        // 2人が同時に話すため、どちらを向いているかで音量を変えられるよう CPU ごとに分けている
        [SerializeField] private AudioSource[] _talkSources;

        [Tooltip("正面を向いているときの話し声の音量。")]
        [SerializeField] private float _talkCenterVolume = 0.3f;

        [Tooltip("その相手を向き切っているときの、その相手の話し声の音量。")]
        [SerializeField] private float _talkFacingVolume = 1f;

        [Tooltip("その相手を向き切っているときの、反対側の相手の話し声の音量。")]
        [SerializeField] private float _talkAwayVolume = 0.05f;

        [SerializeField] private AudioClip _talk1Clip;
        [SerializeField] private AudioClip _talk2Clip;
        [SerializeField] private AudioClip _talk3Clip;
        [SerializeField] private AudioClip _talkFinish1Clip;
        [SerializeField] private AudioClip _talkFinish2Clip;
        [SerializeField] private AudioClip _talkAngryClip;

        [Header("Hud")]
        [SerializeField] private CanvasGroup _canvasGroup;
        // 3D の盤面は CanvasGroup ではフェードできないため、画面全体を覆う黒板を別に用意する
        [SerializeField] private CanvasGroup _fadeOverlayGroup;
        [SerializeField] private SelectionArrowView _selectionArrow;
        [SerializeField] private TargetCursorView _targetCursor;
        [SerializeField] private TextMeshProUGUI _turnLabel;
        [SerializeField] private TextMeshProUGUI _resultLabel;

        // 拍手があと何回必要かを出すデバッグ用の表示。手札と同じ並び（0 がプレイヤー）で、
        // 2人が同時に話すため CPU ごとに分けている。プレイヤーの枠は空のままでよい
        [SerializeField] private TextMeshProUGUI[] _clapChallengeLabels;

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
        // 正面（フェーダーが真ん中）を向いているときのカメラの向き。未設定なら最初に使ったときのカメラの向きを使う
        [SerializeField] private Transform _defaultCameraPoint;

        [Header("Nod Camera")]
        [Tooltip("頷いたときにカメラを下へ傾ける角度。")]
        [SerializeField] private float _nodPitchAngle = 12f;

        [Tooltip("頷いて下を向く／元に戻るのにかける時間（秒）。")]
        [SerializeField] private float _nodDuration = 0.15f;

        [Header("Knock Out")]
        [Tooltip("体力が尽きた CPU が舞い上がる高さ。")]
        [SerializeField] private float _knockOutRiseHeight = 3f;

        [Tooltip("舞い上がるのにかける時間（秒）。")]
        [SerializeField] private float _knockOutRiseDuration = 0.5f;

        [Tooltip("舞い上がった後、落下先へ急降下するのにかける時間（秒）。")]
        [SerializeField] private float _knockOutFallDuration = 0.4f;

        [Tooltip("急降下する先。CPU 同士で同じ場所を指してよい。")]
        [SerializeField] private Transform _knockOutLandingPoint;

        [Tooltip("舞い上がっている間・落下先に着いてからも回り続ける速さ（度/秒）。")]
        [SerializeField] private float _knockOutSpinSpeed = 480f;

        [Tooltip("回転させる軸。ワールド基準。")]
        [SerializeField] private Vector3 _knockOutSpinAxis = Vector3.right;

        private readonly Subject<CardView> _onCardClicked = new Subject<CardView>();
        private readonly CompositeDisposable _cardSubscriptions = new CompositeDisposable();
        private readonly List<CardView> _spawnedCards = new List<CardView>();

        // 選択可能になった瞬間からカードが自分の手札に加わるまで、引く対象の手札をこちらへ向けておくための状態。
        // BeginDrawSelectionAsync で開始し、PlayDrawAsync 側で終了させる。
        private HandView _activeDrawHand;
        private Quaternion _drawHandHomeRotation;

        // 正面を向いているときのカメラの向き。_defaultCameraPoint が未設定でも基準がずれないよう、最初に使った値を覚えておく。
        private Quaternion _cameraHomeRotation;
        private bool _hasCameraHomeRotation;

        // フェーダーで決まった向き。頷きを重ねたり戻したりするたびに作り直せるよう覚えておく。
        private int _facingTargetIndex;
        private float _facingAmount;

        // 頷きの傾き具合（0 が元の向き、1 が下を向き切った状態）。
        // ツマミの1ステップごとに角度が飛ぶとカクついて見えるため、
        // 目標値へ向かって時間をかけて寄せていく。
        private float _nodAmount;
        private float _nodTargetAmount;

        /// <summary>捨て札置き場に積まれた枚数。積み上げる高さの計算に使う。</summary>
        private int _discardStackCount;

        // KO 演出で吹き飛ばしたキャラクターを次の対局で元の位置へ戻すための、変身前の localPosition / localRotation。
        // _characterMorphs と同じ並び。Awake の時点（まだ誰も KO していない）で控えておく。
        private Vector3[] _characterMorphHomeLocalPosition;
        private Quaternion[] _characterMorphHomeLocalRotation;

        /// <summary>引く対象のカードがクリックされた。</summary>
        public Observable<CardView> OnCardClicked => _onCardClicked;

        public int HandCount => _handViews.Length;

        private Camera BoardCamera => _boardCamera != null ? _boardCamera : Camera.main;

        private void Awake()
        {
            CacheCharacterMorphHomeTransforms();
        }

        /// <summary>
        /// 頷きの傾きを毎フレーム目標へ近づける。
        /// 矢印や照準はカメラの向きを見て LateUpdate で位置を決めるので、それより先に動かす。
        /// </summary>
        private void Update()
        {
            if (Mathf.Approximately(_nodAmount, _nodTargetAmount))
            {
                return;
            }

            var step = _nodDuration > 0f ? Time.deltaTime / _nodDuration : 1f;

            _nodAmount = Mathf.MoveTowards(_nodAmount, _nodTargetAmount, step);

            ApplyCameraRotation();
        }

        /// <summary>
        /// 画面を有効化してフェードインする。
        /// </summary>
        public async UniTask ShowAsync(CancellationToken token)
        {
            gameObject.SetActive(true);

            // 前の対局で頷いたまま終わっていても、始まりは元の向きから
            _nodAmount = 0f;
            _nodTargetAmount = 0f;

            if (_bgmSource != null && !_bgmSource.isPlaying)
            {
                _bgmSource.Play();
            }

            if (_selectionArrow != null)
            {
                _selectionArrow.Initialize(BoardCamera);
                _selectionArrow.Hide();
            }

            if (_targetCursor != null)
            {
                _targetCursor.Hide();
            }

            if (_resultLabel != null)
            {
                _resultLabel.gameObject.SetActive(false);
            }

            ClearClapChallengeTexts();

            // 前の対局で変身したままのキャラクターを元に戻す
            ResetCharacterMorphs();

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
        /// 引かれたカードが真上へ素早く抜け、引いた側の手札へ真上から降りてくる演出。
        /// どちらの移動中も、速さに応じた残像が尾を引く。
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

            await cardView.PlayFlyOutAsync(token);

            // 選択可能になった瞬間からこちらへ向けていた手札は、カードが抜けているこの間に戻す。
            // 降りてくるところを引いた側の手札で見せたいので、戻すのは降ろすより前。
            if (_activeDrawHand == fromHand)
            {
                _activeDrawHand = null;
                await PlayHandFacingAsync(fromHand, _drawHandHomeRotation, token);
            }

            // 着地点は手札に加える前に決める。加えたあとだと枚数が増えて位置がずれる。
            var incomingPosition = toHand.GetIncomingWorldPosition();

            // 表裏の切り替えは見えていない間に済ませるので、裏返る瞬間は見えない
            toHand.Add(cardView);
            cardView.SetFaceUp(toHand.IsFaceUp);

            await cardView.PlayFlyInAsync(incomingPosition, token);

            await UniTask.WhenAll(
                toHand.ArrangeAsync(_arrangeDuration, token),
                fromHand.ArrangeAsync(_arrangeDuration, token));
        }

        /// <summary>
        /// 引く対象の手札を選択可能にして、その手札をこちらへ向ける。
        /// カードが引かれて自分の手札に加わるまでその向きを保つ。
        /// カメラの向きはフェーダー（SetCameraFacing）が決めるので、ここでは触らない。
        /// </summary>
        public async UniTask BeginDrawSelectionAsync(int targetIndex, CancellationToken token)
        {
            SetSelectableHand(targetIndex);

            var hand = _handViews[targetIndex];

            if (_drawCameraSettings == null || hand.DrawCameraPoint == null)
            {
                return;
            }

            _activeDrawHand = hand;
            _drawHandHomeRotation = hand.transform.localRotation;

            await PlayHandFacingAsync(hand, hand.DrawFacingRotation, token);
        }

        /// <summary>
        /// 相手の手札を指定の向きへ回す。位置は動かさない。
        /// </summary>
        private UniTask PlayHandFacingAsync(HandView hand, Quaternion localRotation, CancellationToken token)
            => TweenUtility.MoveLocalAsync(
                hand.transform, hand.transform.localPosition, localRotation, _drawCameraSettings.TransitionDuration, token);

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

        /// <summary>
        /// 引くカードを狙う照準を、画面内のランダムな位置に出す。
        /// </summary>
        public void ShowTargetCursor() => _targetCursor?.Show(BoardCamera);

        public void HideTargetCursor() => _targetCursor?.Hide();

        /// <summary>
        /// 照準をツマミ1ステップ分だけ動かす。delta は +X が右、+Y が上。
        /// </summary>
        public void MoveTargetCursor(Vector2 delta) => _targetCursor?.Move(delta);

        /// <summary>
        /// 照準が重なっているカードの、指定した手札の中での位置。
        /// その手札のカードに重なっていなければ NoTargetedCard。
        /// </summary>
        public int GetTargetedCardIndex(int playerIndex)
        {
            var targeted = _targetCursor != null ? _targetCursor.Raycast() : null;

            if (targeted == null || playerIndex < 0 || playerIndex >= _handViews.Length)
            {
                return NoTargetedCard;
            }

            var cards = _handViews[playerIndex].Cards;

            for (var i = 0; i < cards.Count; i++)
            {
                if (cards[i] == targeted)
                {
                    return i;
                }
            }

            return NoTargetedCard;
        }

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
        /// ホーム画面へ戻る際に、この画面を非表示にする。
        /// </summary>
        public void Hide()
        {
            StopAllTalk();
            ClearClapChallengeTexts();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 指定した CPU の声を1つ再生し、その長さ（秒）を返す。
        /// 音源が設定されていなければ何もせず 0 を返すので、待ち時間もそのまま 0 になる。
        /// </summary>
        public float PlayTalkLine(int playerIndex, CpuTalkLine line)
        {
            var source = GetTalkSource(playerIndex);
            var clip = GetTalkClip(line);

            if (source == null || clip == null)
            {
                return 0f;
            }

            // 前の1本が残っていても、次の1本で上書きして続けて聞こえるようにする
            source.Stop();
            source.clip = clip;
            source.Play();

            return clip.length;
        }

        /// <summary>話し途中で画面を離れたときなどに、その CPU の声を止める。</summary>
        public void StopTalk(int playerIndex)
        {
            var source = GetTalkSource(playerIndex);

            if (source != null)
            {
                source.Stop();
            }
        }

        /// <summary>全員の声を止める。画面を閉じるときに使う。</summary>
        public void StopAllTalk()
        {
            if (_talkSources == null)
            {
                return;
            }

            for (var i = 0; i < _talkSources.Length; i++)
            {
                StopTalk(i);
            }
        }

        /// <summary>
        /// フェーダーの向きに合わせて、CPU ごとの話し声の音量を決める。
        /// 正面（amount が 0）ならどちらも同じ音量で、向き切る（amount が 1）ほど
        /// 向いた相手は大きく、反対側の相手は小さくなる。
        /// </summary>
        /// <param name="facingIndex">フェーダーが向いている相手。</param>
        /// <param name="amount">向き具合。0 が正面、1 で向き切っている。</param>
        public void SetTalkVolumes(int facingIndex, float amount)
        {
            if (_talkSources == null)
            {
                return;
            }

            var rate = Mathf.Clamp01(amount);

            for (var i = 0; i < _talkSources.Length; i++)
            {
                var source = _talkSources[i];

                if (source == null)
                {
                    continue;
                }

                var facingVolume = i == facingIndex ? _talkFacingVolume : _talkAwayVolume;
                source.volume = Mathf.Lerp(_talkCenterVolume, facingVolume, rate);
            }
        }

        /// <summary>
        /// その CPU に拍手があと何回必要かのデバッグ表示。空文字を渡すと消える。
        /// </summary>
        public void SetClapChallengeText(int playerIndex, string text)
        {
            var label = GetClapChallengeLabel(playerIndex);

            if (label == null)
            {
                return;
            }

            label.text = text;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>すべてのデバッグ表示を消す。</summary>
        public void ClearClapChallengeTexts()
        {
            if (_clapChallengeLabels == null)
            {
                return;
            }

            for (var i = 0; i < _clapChallengeLabels.Length; i++)
            {
                SetClapChallengeText(i, string.Empty);
            }
        }

        private AudioSource GetTalkSource(int playerIndex)
        {
            if (_talkSources == null || playerIndex < 0 || playerIndex >= _talkSources.Length)
            {
                return null;
            }

            return _talkSources[playerIndex];
        }

        private TextMeshProUGUI GetClapChallengeLabel(int playerIndex)
        {
            if (_clapChallengeLabels == null || playerIndex < 0 || playerIndex >= _clapChallengeLabels.Length)
            {
                return null;
            }

            return _clapChallengeLabels[playerIndex];
        }

        private AudioClip GetTalkClip(CpuTalkLine line)
        {
            switch (line)
            {
                case CpuTalkLine.Talk1:
                    return _talk1Clip;

                case CpuTalkLine.Talk2:
                    return _talk2Clip;

                case CpuTalkLine.Talk3:
                    return _talk3Clip;

                case CpuTalkLine.Finish1:
                    return _talkFinish1Clip;

                case CpuTalkLine.Finish2:
                    return _talkFinish2Clip;

                case CpuTalkLine.Angry:
                    return _talkAngryClip;

                default:
                    return null;
            }
        }

        /// <summary>CUE ボタンで拍手する手が表示中か。</summary>
        public bool IsClapHandsVisible => _clapHandsView != null && _clapHandsView.IsVisible;

        /// <summary>カード選択中（SYNC で照準を出している間）か。CUE を受け付けるかの判定に使う。</summary>
        public bool IsDrawSelectionActive => _activeDrawHand != null;

        /// <summary>
        /// カメラの位置は変えずに、正面と対象プレイヤーの間で向きを補間する。
        /// amount が 0 なら正面、1 なら対象プレイヤーの手札用の向き（カード選択時と同じ DrawCameraPoint）。
        /// フェーダーを動かした分だけ首を振る操作感にしたいので、演出は挟まず即座に反映する。
        /// </summary>
        public void SetCameraFacing(int targetIndex, float amount)
        {
            _facingTargetIndex = targetIndex;
            _facingAmount = Mathf.Clamp01(amount);

            ApplyCameraRotation();
        }

        /// <summary>
        /// フェーダーで向いた先を基準に、カメラを少し下へ傾けて頷きを表す。false なら元の向きへ戻す。
        /// ツマミは1ステップずつ細かく届くため、傾き自体は Update で時間をかけて追従させる。
        /// </summary>
        public void SetCameraNodding(bool isNodding)
        {
            _nodTargetAmount = isNodding ? 1f : 0f;
        }

        /// <summary>
        /// フェーダーの向きに頷きの傾きを重ねて、カメラへ反映する。
        /// </summary>
        private void ApplyCameraRotation()
        {
            var boardCamera = BoardCamera;

            if (boardCamera == null)
            {
                return;
            }

            var home = GetCameraHomeRotation();
            var point = GetFacingCameraPoint(_facingTargetIndex);

            var facing = point == null
                ? home
                : Quaternion.Slerp(home, point.rotation, _facingAmount);

            // 頷きは向いた先を基準にしたいので、カメラのローカル X 軸まわりで下へ傾ける。
            // 動き出しと止まり際をなめらかにしたいので、傾き具合は SmoothStep で均す
            var pitch = _nodPitchAngle * Mathf.SmoothStep(0f, 1f, _nodAmount);

            boardCamera.transform.rotation = facing * Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>
        /// 向く先。カード選択時と同じく、対象プレイヤーの手札に設定された DrawCameraPoint を使う。
        /// </summary>
        private Transform GetFacingCameraPoint(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _handViews.Length)
            {
                return null;
            }

            return _handViews[targetIndex].DrawCameraPoint;
        }

        /// <summary>
        /// 正面を向いているときのカメラの向き。フェーダーを動かすたびに基準がずれないよう、一度だけ確定させる。
        /// </summary>
        private Quaternion GetCameraHomeRotation()
        {
            if (!_hasCameraHomeRotation)
            {
                _cameraHomeRotation = _defaultCameraPoint != null
                    ? _defaultCameraPoint.rotation
                    : BoardCamera.transform.rotation;

                _hasCameraHomeRotation = true;
            }

            return _cameraHomeRotation;
        }

        /// <summary>
        /// CUE ボタンで拍手する手を出し入れする。カメラの向きはフェーダーが決めるため、ここでは触らない。
        /// フェーダーが相手を向き切っていない間は Presenter 側で呼ばれない想定。
        /// </summary>
        public void ToggleClapHands() => _clapHandsView?.Toggle();

        /// <summary>フェーダーが端から離れたときなど、拍手する手を引っ込める。</summary>
        public void HideClapHands() => _clapHandsView?.Hide();

        /// <summary>スクラッチの回転方向に合わせて、拍手する手を閉じる（時計回り）か開く（反時計回り）かを切り替える。</summary>
        public void SetHandsClosed(bool isClosed) => _clapHandsView?.SetHandsClosed(isClosed);

        /// <summary>
        /// 指定プレイヤーのキャラクターを、グリッチで乱れさせながら別のキャラクターへ変える。
        /// 変身先が設定されていない相手や、すでに変身中の相手なら何もしない。
        /// </summary>
        public async UniTask PlayCharacterMorphAsync(int playerIndex, CancellationToken token)
        {
            var morph = GetCharacterMorph(playerIndex);

            if (morph == null || morph.IsPlaying)
            {
                return;
            }

            await morph.PlayAsync(token);
        }

        /// <summary>
        /// 体力が尽きた CPU を、回転させながら舞い上げてから落下先へ急降下させる。
        /// 着地してもホーム画面へ戻る（次の対局の準備で位置が戻る）まで回転し続ける。
        /// 落下先が未設定なら何もしない。
        /// </summary>
        public async UniTask PlayCpuKnockOutAsync(int playerIndex, CancellationToken token)
        {
            var morph = GetCharacterMorph(playerIndex);

            if (morph == null || _knockOutLandingPoint == null)
            {
                return;
            }

            var t = morph.transform;
            var risePosition = t.position + (Vector3.up * _knockOutRiseHeight);

            await UniTask.WhenAll(
                TweenUtility.MoveAsync(t, risePosition, _knockOutRiseDuration, TweenEase.DecelerateOut, token),
                SpinCharacterAsync(t, _knockOutRiseDuration, token));

            await UniTask.WhenAll(
                TweenUtility.MoveAsync(t, _knockOutLandingPoint.position, _knockOutFallDuration, TweenEase.AccelerateIn, token),
                SpinCharacterAsync(t, _knockOutFallDuration, token));

            await SpinCharacterAsync(t, null, token);
        }

        /// <summary>
        /// 指定した時間だけ（null ならキャンセルされるまでずっと）回転させ続ける。
        /// </summary>
        private async UniTask SpinCharacterAsync(Transform target, float? duration, CancellationToken token)
        {
            var elapsed = 0f;

            while (duration == null || elapsed < duration.Value)
            {
                if (target == null)
                {
                    return;
                }

                var deltaTime = Time.deltaTime;
                target.Rotate(_knockOutSpinAxis, _knockOutSpinSpeed * deltaTime, Space.World);
                elapsed += deltaTime;

                await UniTask.Yield(PlayerLoopTiming.Update, token);
            }
        }

        /// <summary>
        /// 変身したキャラクターを変身前の状態へ、KO で吹き飛ばしたキャラクターを元の位置へ戻す。
        /// 次の対局を同じ見た目で始めるために使う。
        /// </summary>
        private void ResetCharacterMorphs()
        {
            if (_characterMorphs == null)
            {
                return;
            }

            for (var i = 0; i < _characterMorphs.Length; i++)
            {
                var morph = _characterMorphs[i];

                if (morph == null)
                {
                    continue;
                }

                morph.ResetToStart();

                if (_characterMorphHomeLocalPosition != null)
                {
                    morph.transform.localPosition = _characterMorphHomeLocalPosition[i];
                    morph.transform.localRotation = _characterMorphHomeLocalRotation[i];
                }
            }
        }

        /// <summary>
        /// KO 演出で動かしたキャラクターを元の位置へ戻せるよう、まだ誰も KO していないこのタイミングで控えておく。
        /// </summary>
        private void CacheCharacterMorphHomeTransforms()
        {
            if (_characterMorphs == null)
            {
                return;
            }

            _characterMorphHomeLocalPosition = new Vector3[_characterMorphs.Length];
            _characterMorphHomeLocalRotation = new Quaternion[_characterMorphs.Length];

            for (var i = 0; i < _characterMorphs.Length; i++)
            {
                if (_characterMorphs[i] == null)
                {
                    continue;
                }

                var t = _characterMorphs[i].transform;
                _characterMorphHomeLocalPosition[i] = t.localPosition;
                _characterMorphHomeLocalRotation[i] = t.localRotation;
            }
        }

        private GlitchMorphEffect GetCharacterMorph(int playerIndex)
        {
            if (_characterMorphs == null || playerIndex < 0 || playerIndex >= _characterMorphs.Length)
            {
                return null;
            }

            return _characterMorphs[playerIndex];
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
        }
    }
}
