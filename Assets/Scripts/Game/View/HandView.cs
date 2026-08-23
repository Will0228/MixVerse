using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MixVerse.Game.View
{
    /// <summary>
    /// プレイヤー1人分の手札の並び。
    /// カードはこの Transform の子になり、ローカル座標で横一列に整列する。
    /// </summary>
    public sealed class HandView : MonoBehaviour
    {
        [SerializeField] private bool _isFaceUp;
        [SerializeField] private float _cardSpacing = 0.5f;
        [SerializeField] private float _maxWidth = 6.0f;
        [SerializeField] private float _depthStep = 0.004f;

        [Header("Draw Camera (相手の手札を引く演出用。未設定なら演出なし)")]
        [SerializeField] private Transform _drawCameraPoint;
        [SerializeField] private Vector3 _drawFacingEuler;

        private readonly List<CardView> _cards = new List<CardView>();

        public IReadOnlyList<CardView> Cards => _cards;

        /// <summary>この手札を表向きで並べるか。自分の手札だけ true。</summary>
        public bool IsFaceUp => _isFaceUp;

        /// <summary>
        /// この手札から引く際に使う専用カメラの位置・向き。未設定ならカメラ演出は行わない。
        /// </summary>
        public Transform DrawCameraPoint => _drawCameraPoint;

        /// <summary>
        /// 引かれている間、この手札がこちらを向くように取る回転（ローカル）。
        /// </summary>
        public Quaternion DrawFacingRotation => Quaternion.Euler(_drawFacingEuler);

        public int Count => _cards.Count;

        public void Add(CardView card)
        {
            if (card == null || _cards.Contains(card))
            {
                return;
            }

            _cards.Add(card);
            card.transform.SetParent(transform, true);
            card.SetFaceUp(_isFaceUp);
        }

        public void Remove(CardView card)
        {
            _cards.Remove(card);
        }

        public void Clear()
        {
            _cards.Clear();
        }

        /// <summary>
        /// 指定位置のカードを取り出す。ドロー時に使う。
        /// </summary>
        public CardView TakeAt(int index)
        {
            var card = _cards[index];
            _cards.RemoveAt(index);
            return card;
        }

        /// <summary>
        /// 手札の何番目がどのワールド座標に来るかを返す。
        /// カードを飛ばす先の座標を知るために使う。
        /// </summary>
        public Vector3 GetSlotWorldPosition(int index, int totalCount)
            => transform.TransformPoint(GetSlotLocalPosition(index, totalCount));

        /// <summary>
        /// 現在の枚数で整列したときの、次に追加されるカードの着地点。
        /// </summary>
        public Vector3 GetIncomingWorldPosition() => GetSlotWorldPosition(_cards.Count, _cards.Count + 1);

        /// <summary>
        /// アニメーション付きで整列する。
        /// </summary>
        public async UniTask ArrangeAsync(float duration, CancellationToken token)
        {
            var tasks = new List<UniTask>(_cards.Count);

            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                card.HandIndex = i;

                tasks.Add(TweenUtility.MoveLocalAsync(
                    card.transform,
                    GetSlotLocalPosition(i, _cards.Count),
                    card.GetLocalRotation(),
                    duration,
                    token));
            }

            await UniTask.WhenAll(tasks);
        }

        /// <summary>
        /// 即座に整列する。配札直後などに使う。
        /// </summary>
        public void ArrangeImmediate()
        {
            for (var i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                card.HandIndex = i;
                card.transform.localPosition = GetSlotLocalPosition(i, _cards.Count);
                card.transform.localRotation = card.GetLocalRotation();
            }
        }

        /// <summary>
        /// この手札のカードを選択できる状態にするか。
        /// </summary>
        public void SetSelectable(bool selectable)
        {
            foreach (var card in _cards)
            {
                card.IsSelectable = selectable;
                card.SetRaycastEnabled(selectable);
            }
        }

        /// <summary>
        /// 枚数が増えるほど間隔を詰めて、一定の幅に収まるようにする。
        /// </summary>
        private Vector3 GetSlotLocalPosition(int index, int totalCount)
        {
            if (totalCount <= 1)
            {
                return Vector3.zero;
            }

            var spacing = _cardSpacing;
            var fullWidth = spacing * (totalCount - 1);

            if (fullWidth > _maxWidth)
            {
                spacing = _maxWidth / (totalCount - 1);
                fullWidth = _maxWidth;
            }

            var x = (-fullWidth * 0.5f) + (spacing * index);

            // 重なり順が安定するよう、後ろのカードほどわずかに手前へ出す。
            // 手札の Rotation により表面はローカル +Z を向くので、+Z 側がカメラに近い。
            return new Vector3(x, 0f, _depthStep * index);
        }
    }
}
