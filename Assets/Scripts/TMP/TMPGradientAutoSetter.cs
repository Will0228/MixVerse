using UnityEngine;
using TMPro;
using Object = UnityEngine.Object;

/// <summary>
/// GradationTextShader 用に「テキスト矩形内での正規化座標 (0〜1)」を
/// メッシュの UV2 へ焼き込むコンポーネント。
///
/// TextMeshProUGUI は Canvas のバッチングによって、頂点がテキストのローカル空間ではなく
/// Canvas の空間へ変換された状態でシェーダーへ渡されるため、
/// シェーダー側で頂点座標からテキスト内の位置を求めることができない。
/// （その結果、テキストの画面上の位置によってグラデーションがズレてしまう。）
///
/// UV2 は頂点データなのでバッチングの影響を受けず、
/// 複数の TextMeshPro が同じマテリアルを共有していてもそれぞれ正しい値になる。
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public class TMPGradientAutoSetter : MonoBehaviour
{
    public enum GradientSource
    {
        /// <summary>RectTransform の矩形全体を基準にする</summary>
        RectTransform,
        /// <summary>実際に描画されている文字の範囲を基準にする</summary>
        TextBounds,
    }

    [SerializeField]
    private GradientSource gradientSource = GradientSource.RectTransform;

    // このプロパティを持つマテリアルだけを対象にする。
    // （絵文字やフォールバックフォントのサブメッシュは TMP 標準シェーダーを使っており、
    //   そちらの UV2 には SDF のスケール情報が入っているため上書きしてはいけない）
    private static readonly int PropertyId_GradientAngle = Shader.PropertyToID("_GradientAngle");

    private TMP_Text tmpText;
    private RectTransform rectTransform;

    private void OnEnable()
    {
        tmpText = GetComponent<TMP_Text>();
        rectTransform = GetComponent<RectTransform>();

        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);

        // 既に生成済みのメッシュに対しても適用する
        tmpText.ForceMeshUpdate();
        ApplyGradientUV();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
    }

    private void OnTextChanged(Object obj)
    {
        // メッシュが再生成されると UV2 は TMP の値で上書きされるので、その都度焼き直す
        if (obj == tmpText) ApplyGradientUV();
    }

    // RectTransform のサイズ変更に追従する（通常は再レイアウト経由で TEXT_CHANGED も飛ぶが保険）
    private void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled) ApplyGradientUV();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (isActiveAndEnabled) ApplyGradientUV();
    }
#endif

    private void ApplyGradientUV()
    {
        if (tmpText == null)
        {
            tmpText = GetComponent<TMP_Text>();
            if (tmpText == null)
            {
                return;
            }
        }


        TMP_TextInfo textInfo = tmpText.textInfo;
        if (textInfo == null || textInfo.meshInfo == null)
        {
            return;
        }

        Rect rect = GetSourceRect();
        if (rect.width <= 0f || rect.height <= 0f)
        {
            return;
        }

        float invWidth = 1f / rect.width;
        float invHeight = 1f / rect.height;

        bool modified = false;
        // meshInfo.Lengthは実際TMPが確保している大きさよりも大きい場合があるため小さい方を採用
        int materialCount = Mathf.Min(textInfo.materialCount, textInfo.meshInfo.Length);

        for (int m = 0; m < materialCount; m++)
        {
            // TMP_MeshInfo は構造体だが uvs2 / vertices は配列参照なので、
            // コピーを経由しても元のデータを書き換えられる
            TMP_MeshInfo meshInfo = textInfo.meshInfo[m];

            if (meshInfo.vertices == null || meshInfo.uvs2 == null) continue;
            if (meshInfo.material == null || !meshInfo.material.HasProperty(PropertyId_GradientAngle))
            {
                continue;
            }

            // meshInfo.uvs2.Length : 配列が確保しているスペースであり実際に使用しているスペースではない
            // なのでmeshInfo.vertexCountだけでもよさそう
            int vertexCount = Mathf.Min(meshInfo.vertexCount, meshInfo.uvs2.Length);
            for (int i = 0; i < vertexCount; i++)
            {
                Vector3 vertex = meshInfo.vertices[i];
                meshInfo.uvs2[i] = new Vector2(
                    (vertex.x - rect.xMin) * invWidth,
                    (vertex.y - rect.yMin) * invHeight);
            }

            modified = true;
        }

        // メッシュを再生成せず UV2 だけを転送し直すので、TEXT_CHANGED が再帰的に飛ぶことはない
        if (modified) tmpText.UpdateVertexData(TMP_VertexDataUpdateFlags.Uv2);
    }

    private Rect GetSourceRect()
    {
        if (gradientSource == GradientSource.TextBounds)
        {
            Bounds bounds = tmpText.textBounds;
            if (bounds.size.x > 0f && bounds.size.y > 0f)
            {
                return new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
            }
        }

        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        return rectTransform != null ? rectTransform.rect : new Rect(0f, 0f, 1f, 1f);
    }
}
