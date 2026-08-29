using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace MixVerse
{
    /// <summary>
    /// 1 つめのオブジェクトがグリッチのように乱れながら消え、
    /// 入れ替わりで 2 つめのオブジェクトが同じ乱れ方で実体化する演出。
    ///
    /// 見た目は GlitchMorphShader が担当し、ここは両者の _Progress を時間で動かすだけ。
    /// 2 つのオブジェクトは同じ位置に重ねて置くこと（帯の位置がワールド基準でそろう）。
    ///
    /// グリッチ用のマテリアルはこのコンポーネントごとに複製して持つ。
    /// マテリアルのアセットを直接書き換えたり MaterialPropertyBlock に頼ったりすると、
    /// 同じ Prefab から作られた別のキャラクターまで一緒に乱れてしまうため。
    /// </summary>
    public sealed class GlitchMorphEffect : MonoBehaviour
    {
        private const string GlitchShaderName = "Unlit/GlitchMorphShader";

        // GlitchMorphShader の消え具合。0 が実体、1 が完全に消えた状態。
        private static readonly int ProgressPropertyId = Shader.PropertyToID("_Progress");

        // 差し替え元のマテリアルから絵柄を引き継ぐときに見るプロパティ。
        // URP のシェーダーは _BaseMap / _BaseColor、Standard や旧 Unlit は _MainTex / _Color を使う。
        private const string BaseMapPropertyName = "_BaseMap";
        private const string MainTexPropertyName = "_MainTex";
        private const string BaseColorPropertyName = "_BaseColor";
        private const string ColorPropertyName = "_Color";

        [Header("Objects")]
        [Tooltip("変身元。子の Renderer はまとめて対象になる。")]
        [SerializeField] private GameObject _fromObject;

        [Tooltip("変身先。変身元と同じ位置に重ねて置く。")]
        [SerializeField] private GameObject _toObject;

        [Header("Timing")]
        [SerializeField] private float _duration = 1.2f;

        [Tooltip("消えるのと現れるのが重なる割合。0 でちょうど中間で切り替わり、1 で最初から最後まで両方が乱れ続ける。")]
        [Range(0f, 1f)]
        [SerializeField] private float _overlap = 0.4f;

        [SerializeField] private bool _playOnStart;

        [Header("Material")]
        [Tooltip("差し替えに使うマテリアル。グリッチの見た目（帯の細かさ・ズレ量など）はここで調整する。未設定なら既定値で作る。")]
        [SerializeField] private Material _glitchMaterialTemplate;

        private RendererBinding[] _fromBindings;
        private RendererBinding[] _toBindings;
        private CancellationTokenSource _playCts;
        private bool _isGlitchMaterialApplied;

        /// <summary>変身中かどうか。二重に走らせたくない呼び出し側の判定用。</summary>
        public bool IsPlaying => _isGlitchMaterialApplied;

        /// <summary>
        /// 変身元・変身先を差し替える。Prefab から動的に生成した相手にも使えるようにしておく。
        /// </summary>
        public void SetObjects(GameObject fromObject, GameObject toObject)
        {
            ReleaseBindings();

            _fromObject = fromObject;
            _toObject = toObject;

            ResetToStart();
        }

        /// <summary>
        /// 変身前の状態に戻す。変身元だけが実体で、変身先は消えている。
        /// </summary>
        public void ResetToStart()
        {
            CacheBindings();

            SetObjectActive(_fromObject, true);

            // 変身中以外は元のマテリアルに戻っていて _Progress が効かないので、
            // 変身先はグリッチマテリアルを当てるまで出さない（出すと最初から丸見えになる）。
            SetObjectActive(_toObject, _isGlitchMaterialApplied);

            SetProgress(_fromBindings, 0f);
            SetProgress(_toBindings, 1f);
        }

        /// <summary>
        /// 変身を再生する。多重再生しないよう、走っている演出は中断してから始める。
        /// </summary>
        [ContextMenu("Play")]
        public void Play()
        {
            _playCts?.Cancel();
            _playCts?.Dispose();
            _playCts = new CancellationTokenSource();

            PlayAsync(_playCts.Token).Forget();
        }

        /// <summary>
        /// 変身の完了まで待てる版。ゲーム側の演出シーケンスに組み込むときはこちらを使う。
        /// </summary>
        public async UniTask PlayAsync(CancellationToken token)
        {
            ApplyGlitchMaterials();
            ResetToStart();

            try
            {
                // 変身元が消えきる時刻と、変身先が現れ始める時刻。
                // _overlap が 0 なら中間できっちり入れ替わり、大きいほど両方が乱れている時間が延びる。
                var fadeOutEnd = Mathf.Clamp(0.5f + (_overlap * 0.5f), 0.01f, 1f);
                var fadeInStart = Mathf.Clamp(0.5f - (_overlap * 0.5f), 0f, 0.99f);

                await TweenUtility.ValueAsync(0f, 1f, _duration, token, t =>
                {
                    SetProgress(_fromBindings, Mathf.Clamp01(t / fadeOutEnd));
                    SetProgress(_toBindings, 1f - Mathf.Clamp01((t - fadeInStart) / (1f - fadeInStart)));
                });

                SetProgress(_toBindings, 0f);
            }
            finally
            {
                // 中断されたときも元のマテリアルへ戻す。差し替えたまま止まると
                // キャラクターがグリッチ用の簡易ライティングのままになってしまう。
                RestoreOriginalMaterials();
            }

            // 消えたままの変身元は描画も当たり判定も無駄なので消しておく。
            SetObjectActive(_fromObject, false);
        }

        private void Awake()
        {
            ResetToStart();
        }

        private void Start()
        {
            if (_playOnStart)
            {
                Play();
            }
        }

        private void OnDestroy()
        {
            _playCts?.Cancel();
            _playCts?.Dispose();
            _playCts = null;

            ReleaseBindings();
        }

        // ------------------------------------------------------------------
        // Renderer とマテリアルの管理
        // ------------------------------------------------------------------

        private void CacheBindings()
        {
            // 3D モデルは子に複数の Renderer を持ち、Renderer 自体も複数マテリアルを持つので、
            // Renderer 1 つにつき「元のマテリアル配列」と「差し替え用の配列」を組で覚えておく。
            _fromBindings ??= CreateBindings(_fromObject);
            _toBindings ??= CreateBindings(_toObject);
        }

        private RendererBinding[] CreateBindings(GameObject target)
        {
            if (target == null)
            {
                return System.Array.Empty<RendererBinding>();
            }

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            var bindings = new RendererBinding[renderers.Length];

            for (var i = 0; i < renderers.Length; i++)
            {
                var originalMaterials = renderers[i].sharedMaterials;
                var glitchMaterials = new Material[originalMaterials.Length];

                for (var materialIndex = 0; materialIndex < originalMaterials.Length; materialIndex++)
                {
                    glitchMaterials[materialIndex] = CreateGlitchMaterial(originalMaterials[materialIndex]);
                }

                bindings[i] = new RendererBinding
                {
                    Renderer = renderers[i],
                    OriginalMaterials = originalMaterials,
                    GlitchMaterials = glitchMaterials,
                };
            }

            return bindings;
        }

        /// <summary>
        /// 元のマテリアル 1 つに対応するグリッチマテリアルを作る。
        /// このコンポーネント専用の実体なので、_Progress を動かしても他のキャラクターには影響しない。
        /// </summary>
        private Material CreateGlitchMaterial(Material source)
        {
            var material = CreateGlitchMaterialInstance(source);

            // シーンに保存されない一時的なマテリアル。ReleaseBindings で破棄する。
            material.hideFlags = HideFlags.HideAndDontSave;
            material.name = source != null ? source.name + " (Glitch)" : "Glitch";

            return material;
        }

        private Material CreateGlitchMaterialInstance(Material source)
        {
            // 元がすでに GlitchMorphShader なら、調整済みの値をそのまま引き継ぐ
            if (source != null && source.shader != null && source.shader.name == GlitchShaderName)
            {
                return new Material(source);
            }

            var material = _glitchMaterialTemplate != null
                ? new Material(_glitchMaterialTemplate)
                : new Material(Shader.Find(GlitchShaderName));

            if (source == null)
            {
                return material;
            }

            // 絵柄と基本色だけ引き継ぐので、変身中も元のテクスチャのまま乱れる
            var textureName = GetExistingPropertyName(source, BaseMapPropertyName, MainTexPropertyName);
            if (textureName != null)
            {
                material.SetTexture(MainTexPropertyName, source.GetTexture(textureName));
                material.SetTextureScale(MainTexPropertyName, source.GetTextureScale(textureName));
                material.SetTextureOffset(MainTexPropertyName, source.GetTextureOffset(textureName));
            }

            var colorName = GetExistingPropertyName(source, BaseColorPropertyName, ColorPropertyName);
            if (colorName != null)
            {
                material.SetColor(BaseColorPropertyName, source.GetColor(colorName));
            }

            return material;
        }

        private static string GetExistingPropertyName(Material material, string preferred, string fallback)
        {
            if (material.HasProperty(preferred))
            {
                return preferred;
            }

            return material.HasProperty(fallback) ? fallback : null;
        }

        private void ApplyGlitchMaterials()
        {
            CacheBindings();

            if (_isGlitchMaterialApplied)
            {
                return;
            }

            SetMaterials(_fromBindings, useGlitch: true);
            SetMaterials(_toBindings, useGlitch: true);

            _isGlitchMaterialApplied = true;
        }

        private void RestoreOriginalMaterials()
        {
            if (!_isGlitchMaterialApplied)
            {
                return;
            }

            SetMaterials(_fromBindings, useGlitch: false);
            SetMaterials(_toBindings, useGlitch: false);

            _isGlitchMaterialApplied = false;
        }

        private static void SetMaterials(RendererBinding[] bindings, bool useGlitch)
        {
            if (bindings == null)
            {
                return;
            }

            foreach (var binding in bindings)
            {
                var materials = useGlitch ? binding.GlitchMaterials : binding.OriginalMaterials;

                if (binding.Renderer == null || materials == null)
                {
                    continue;
                }

                binding.Renderer.sharedMaterials = materials;
            }
        }

        private void ReleaseBindings()
        {
            RestoreOriginalMaterials();

            DestroyGlitchMaterials(_fromBindings);
            DestroyGlitchMaterials(_toBindings);

            _fromBindings = null;
            _toBindings = null;
        }

        private static void DestroyGlitchMaterials(RendererBinding[] bindings)
        {
            if (bindings == null)
            {
                return;
            }

            foreach (var binding in bindings)
            {
                if (binding.GlitchMaterials == null)
                {
                    continue;
                }

                foreach (var material in binding.GlitchMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    // ContextMenu から編集中に再生することもあるので、両方の破棄経路を持つ
                    if (Application.isPlaying)
                    {
                        Destroy(material);
                    }
                    else
                    {
                        DestroyImmediate(material);
                    }
                }
            }
        }

        private static void SetObjectActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        /// <summary>
        /// 消え具合を設定する。書き込む先はこのコンポーネントが複製したマテリアルだけなので、
        /// 同じマテリアルアセットを使っている他のキャラクターは影響を受けない。
        /// </summary>
        private static void SetProgress(RendererBinding[] bindings, float progress)
        {
            if (bindings == null)
            {
                return;
            }

            foreach (var binding in bindings)
            {
                if (binding.GlitchMaterials == null)
                {
                    continue;
                }

                foreach (var material in binding.GlitchMaterials)
                {
                    if (material != null)
                    {
                        material.SetFloat(ProgressPropertyId, progress);
                    }
                }
            }
        }

        /// <summary>
        /// Renderer 1 つ分の、元のマテリアルとグリッチマテリアルの対応。
        /// </summary>
        private sealed class RendererBinding
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] GlitchMaterials;
        }
    }
}
