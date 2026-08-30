using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.UI;

public class SimpleBlitCombiner : MonoBehaviour
{
    [SerializeField] private ImageTouchHandler _imageTouchHandler; // タッチで操作する場合のみ使用。自動再生のみなら未設定でよい
    [SerializeField] private Texture2D _backgroundTexture; // 初期状態（真っ黒など）
    [SerializeField] private Texture2D _stampTexture; // 中心が赤、フチが緑の画像

    // 2種類のマテリアルを使い分ける
    [SerializeField] private Material _accumulateMaterial; // 「StampAccumulateShaderURP」のマテリアル
    [SerializeField] private Material _displayMaterial; // 「SimpleBlitCombineShaderURP」のマテリアル（隠し画像合成用）

    [SerializeField] private RawImage _resultImage; // 結果を表示するためのUI RawImage

    // 削り跡の解像度。_backgroundTexture は初期状態を流し込むだけの小さな画像でよいので、
    // その大きさに引きずられないようここで明示的に決める。
    [SerializeField] private int _patternResolution = 512;

    private RenderTexture _renderTexture;
    private Vector2 _stampPosition;

    private void Awake()
    {
        // パターンテクスチャ（蓄積用）のRenderTextureを生成
        _renderTexture = new RenderTexture(_patternResolution, _patternResolution, 0);

        // 初期状態として、まずは背景画像（真っ黒など）をRenderTextureにコピーしておく
        Graphics.Blit(_backgroundTexture, _renderTexture);

        // 蓄積用マテリアルにスタンプをセット
        _accumulateMaterial.SetTexture("_StampTex", _stampTexture);

        // 表示用マテリアルに、完成したパターンテクスチャ（RenderTexture）をセット
        _resultImage.material = _displayMaterial; // RawImage自体にマテリアルをセット
        _displayMaterial.SetTexture("_PatternTex", _renderTexture); // 記事にあるような名前（_PatternTex）でセット
    }

    private void Start()
    {
        if (_imageTouchHandler != null)
        {
            _imageTouchHandler.OnTouchPositionAsObservable
                .Subscribe(TouchScreen)
                .AddTo(this);
        }
    }

    private void TouchScreen(Vector2 uv)
    {
        _stampPosition = uv;
        CombineImages();
    }

    // インスペクターのコンポーネントを右クリックで実行できます
    [ContextMenu("Execute Blit (画像を合体)")]
    public void CombineImages()
    {
        Stamp(_stampPosition, new Vector2(0.1f, 0.1f));
    }

    /// <summary>
    /// 削られる側（表面）の絵を差し替える。
    /// ホーム画面では、遷移が始まった瞬間の画面をそのままスクショして流し込んでいる。
    /// </summary>
    public void SetSourceTexture(Texture texture)
    {
        if (_resultImage == null)
        {
            return;
        }

        _resultImage.texture = texture;
    }

    /// <summary>
    /// 蓄積したパターンを初期状態（未スクラッチ）に戻す。
    /// ホーム画面へ戻ってきたときなど、再生前に呼び出す。
    /// </summary>
    public void ResetPattern()
    {
        Graphics.Blit(_backgroundTexture, _renderTexture);
    }

    /// <summary>
    /// タッチ操作の代わりに、画面を横帯に分割してスタンプを自動で走らせながら削っていく。
    /// 上から数えて 1, 3, ... 番目の帯は左から右へ、2, 4, ... 番目の帯は右から左へ進むので、
    /// 帯ごとに逆向きに削られていく見た目になる。
    ///
    /// 進捗（0〜1）が thresholdProgress に達した瞬間に一度だけ onThresholdReached を呼ぶので、
    /// 呼び出し側はそのタイミングで別の演出（CanvasGroup のフェードなど）を重ねて開始できる。
    /// </summary>
    public async UniTask PlayBandWipeAsync(
        int bandCount,
        Vector2 stampScale,
        float duration,
        float thresholdProgress,
        Action onThresholdReached,
        CancellationToken token)
    {
        ResetPattern();

        bandCount = Mathf.Max(1, bandCount);

        // 画面の左右端が削り残らないよう、画面の外から入って外へ抜けるようにする
        var margin = stampScale.x * 0.5f;
        var startU = -margin;
        var endU = 1f + margin;

        var thresholdFired = false;
        var elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            var t = Mathf.Clamp01(elapsedTime / duration);

            StampBands(bandCount, stampScale, t, startU, endU);

            if (!thresholdFired && t >= thresholdProgress)
            {
                thresholdFired = true;
                onThresholdReached?.Invoke();
            }

            await UniTask.Yield(PlayerLoopTiming.Update, token);
        }

        StampBands(bandCount, stampScale, 1f, startU, endU);

        if (!thresholdFired)
        {
            onThresholdReached?.Invoke();
        }
    }

    /// <summary>
    /// 各帯の現在位置へスタンプを 1 個ずつ押す。
    /// </summary>
    private void StampBands(int bandCount, Vector2 stampScale, float t, float startU, float endU)
    {
        for (var i = 0; i < bandCount; i++)
        {
            // i = 0 が一番上の帯。UV は下が 0 なので、上から数えるために反転する
            var v = 1f - ((i + 0.5f) / bandCount);

            // 上から 1, 3 番目（偶数インデックス）は左から、2, 4 番目は右から
            var fromLeft = (i % 2) == 0;
            var u = fromLeft ? Mathf.Lerp(startU, endU, t) : Mathf.Lerp(endU, startU, t);

            Stamp(new Vector2(u, v), stampScale);
        }
    }

    private void Stamp(Vector2 position, Vector2 scale)
    {
        // 蓄積用マテリアルにパラメータをセット
        _accumulateMaterial.SetVector("_StampPos", position);
        _accumulateMaterial.SetVector("_StampScale", scale);

        // 上書きループの処理
        // 1. 一時的なRenderTexture（テンポラリバッファ）を1枚借りる
        RenderTexture tempBuffer = RenderTexture.GetTemporary(_renderTexture.width, _renderTexture.height, 0);

        // 2. 現在の「蓄積された画像(_renderTexture)」を入力とし、
        //    新しいスタンプを重ねた結果を「tempBuffer」に書き込む（ここでBlendOp Maxが効く）
        Graphics.Blit(_renderTexture, tempBuffer, _accumulateMaterial);

        // 3. 結果が入った「tempBuffer」の中身を、本番用の「_renderTexture」にコピーして戻す
        Graphics.Blit(tempBuffer, _renderTexture);

        // 4. 借りた一時的なRenderTextureを返却する（メモリリーク防止のために絶対必要）
        RenderTexture.ReleaseTemporary(tempBuffer);

        // （表示用マテリアルはすでに _renderTexture を参照しているので、自動的に画面が更新されます）
    }

    // 終了時にメモリを解放
    private void OnDestroy()
    {
        if (_renderTexture != null)
        {
            _renderTexture.Release();
        }
    }
}
