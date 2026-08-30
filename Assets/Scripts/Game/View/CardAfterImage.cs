using UnityEngine;
using UnityEngine.Rendering;

namespace MixVerse.Game.View
{
    /// <summary>
    /// カードの残像。
    ///
    /// 過去の姿勢に置いたカードの複製を数枚並べ、速く動いているときほど濃く重ねる。
    /// 複製はカード本体と同じマテリアルの複製を使い、_TrailGhost を立てることで
    /// CardDissolveShader の AfterImage パス（半透明）だけが描かれるようにしている。
    /// 半透明なので、複製のマテリアルだけ描画順を Transparent へずらして
    /// カードを全部描き終えたあとに重ねる。
    /// </summary>
    internal sealed class CardAfterImage
    {
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int TrailGhostPropertyId = Shader.PropertyToID("_TrailGhost");
        private static readonly int TrailAlphaPropertyId = Shader.PropertyToID("_TrailAlpha");

        private readonly Transform _owner;
        private readonly MeshRenderer[] _sources;
        private readonly int _ghostCount;
        private readonly float _sampleInterval;
        private readonly float _referenceSpeed;
        private readonly float _maxAlpha;

        // 直近の姿勢。0 が最新で、添字が大きいほど古い。
        private readonly Pose[] _history;

        private Ghost[] _ghosts;
        private Material[] _materials;

        private bool _isActive;
        private float _sampleTimer;
        private Vector3 _lastSamplePosition;

        /// <param name="ghostCount">同時に出す残像の枚数。</param>
        /// <param name="sampleInterval">姿勢を記録する間隔（秒）。フレームレートで尾の長さが変わらないよう時間で刻む。</param>
        /// <param name="referenceSpeed">残像が最も濃くなる速さ（ワールド単位/秒）。</param>
        /// <param name="maxAlpha">いちばん手前の残像の最大の濃さ。</param>
        public CardAfterImage(
            Transform owner,
            MeshRenderer[] sources,
            int ghostCount,
            float sampleInterval,
            float referenceSpeed,
            float maxAlpha)
        {
            _owner = owner;
            _sources = sources;
            _ghostCount = Mathf.Max(1, ghostCount);
            _sampleInterval = Mathf.Max(0.001f, sampleInterval);
            _referenceSpeed = Mathf.Max(0.001f, referenceSpeed);
            _maxAlpha = maxAlpha;

            // 残像は 1 サンプル前から使うので、今の姿勢の分だけ余分に持つ
            _history = new Pose[_ghostCount + 1];
        }

        /// <summary>
        /// 残像の表示を始める／終える。終えると複製は破棄する。
        /// </summary>
        public void SetActive(bool active)
        {
            if (active == _isActive)
            {
                return;
            }

            _isActive = active;

            if (!active)
            {
                DestroyGhosts();
                return;
            }

            BuildGhosts();

            // 動き出す前は全部が今の姿勢。速さ 0 なので見えないところから始まる。
            var pose = new Pose(_owner.position, _owner.rotation);

            for (var i = 0; i < _history.Length; i++)
            {
                _history[i] = pose;
            }

            _lastSamplePosition = pose.position;
            _sampleTimer = 0f;

            Apply(0f);
        }

        /// <summary>
        /// 毎フレーム呼ぶ。トゥイーンが位置を書いたあとに見たいので LateUpdate から呼ぶこと。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_isActive || _ghosts == null || _owner == null)
            {
                return;
            }

            _sampleTimer += deltaTime;

            if (_sampleTimer < _sampleInterval)
            {
                return;
            }

            var position = _owner.position;
            var speed = Vector3.Distance(position, _lastSamplePosition) / _sampleTimer;

            _lastSamplePosition = position;
            _sampleTimer = 0f;

            PushHistory(new Pose(position, _owner.rotation));
            Apply(Mathf.Clamp01(speed / _referenceSpeed));
        }

        /// <summary>
        /// 複製をまとめて片付ける。カードが破棄されるときに呼ぶ。
        /// </summary>
        public void Dispose()
        {
            _isActive = false;
            DestroyGhosts();
        }

        private void PushHistory(Pose pose)
        {
            for (var i = _history.Length - 1; i > 0; i--)
            {
                _history[i] = _history[i - 1];
            }

            _history[0] = pose;
        }

        /// <param name="speedRate">0 が停止、1 が referenceSpeed 以上。</param>
        private void Apply(float speedRate)
        {
            for (var i = 0; i < _ghosts.Length; i++)
            {
                // _history[0] は今の姿勢（カード本体が居る場所）なので、残像は 1 つ前から
                var pose = _history[i + 1];
                var ghost = _ghosts[i];

                ghost.Root.SetPositionAndRotation(pose.position, pose.rotation);

                // 古い残像ほど薄い
                var fade = 1f - (i / (float)_ghosts.Length);
                ghost.SetAlpha(_maxAlpha * speedRate * fade);
            }
        }

        private void BuildGhosts()
        {
            if (_ghosts != null)
            {
                return;
            }

            _materials = new Material[_sources.Length];

            var sourceBlock = new MaterialPropertyBlock();

            for (var i = 0; i < _sources.Length; i++)
            {
                var material = new Material(_sources[i].sharedMaterial);
                material.SetFloat(TrailGhostPropertyId, 1f);
                material.renderQueue = (int)RenderQueue.Transparent;

                // 絵柄はカードごとに MaterialPropertyBlock で差し込まれているので、
                // マテリアルを複製しただけでは付いてこない。複製にも同じ絵柄を移す。
                _sources[i].GetPropertyBlock(sourceBlock);
                var faceTexture = sourceBlock.GetTexture(MainTexPropertyId);

                if (faceTexture != null)
                {
                    material.SetTexture(MainTexPropertyId, faceTexture);
                }

                _materials[i] = material;
            }

            // 親を持たせるとその親が動いたときに残像まで付いてきてしまうので、
            // ルート直下に置いてワールド座標で姿勢を与える。
            var scale = _owner.lossyScale;
            _ghosts = new Ghost[_ghostCount];

            for (var g = 0; g < _ghostCount; g++)
            {
                var root = new GameObject("CardAfterImage");
                root.transform.localScale = scale;

                var renderers = new MeshRenderer[_sources.Length];

                for (var i = 0; i < _sources.Length; i++)
                {
                    var copy = Object.Instantiate(_sources[i].gameObject, root.transform, false);
                    copy.name = _sources[i].name;

                    var meshCollider = copy.GetComponent<Collider>();
                    if (meshCollider != null)
                    {
                        Object.Destroy(meshCollider);
                    }

                    var meshRenderer = copy.GetComponent<MeshRenderer>();
                    meshRenderer.sharedMaterial = _materials[i];
                    meshRenderer.enabled = false;
                    renderers[i] = meshRenderer;
                }

                _ghosts[g] = new Ghost(root.transform, renderers);
            }
        }

        private void DestroyGhosts()
        {
            if (_ghosts != null)
            {
                foreach (var ghost in _ghosts)
                {
                    ghost.Destroy();
                }

                _ghosts = null;
            }

            if (_materials == null)
            {
                return;
            }

            foreach (var material in _materials)
            {
                if (material != null)
                {
                    Object.Destroy(material);
                }
            }

            _materials = null;
        }

        /// <summary>
        /// 残像 1 枚分。カードの表裏と同じ板を持ち、濃さだけを差し替える。
        /// </summary>
        private sealed class Ghost
        {
            private readonly MeshRenderer[] _renderers;
            private MaterialPropertyBlock _propertyBlock;

            public Ghost(Transform root, MeshRenderer[] renderers)
            {
                Root = root;
                _renderers = renderers;
            }

            public Transform Root { get; }

            public void SetAlpha(float alpha)
            {
                var visible = alpha > 0.001f;
                _propertyBlock ??= new MaterialPropertyBlock();

                foreach (var meshRenderer in _renderers)
                {
                    if (meshRenderer == null)
                    {
                        continue;
                    }

                    meshRenderer.enabled = visible;

                    if (!visible)
                    {
                        continue;
                    }

                    meshRenderer.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetFloat(TrailAlphaPropertyId, alpha);
                    meshRenderer.SetPropertyBlock(_propertyBlock);
                }
            }

            public void Destroy()
            {
                if (Root != null)
                {
                    Object.Destroy(Root.gameObject);
                }
            }
        }
    }
}
