# CI (GitHub Actions)

| ワークフロー | ファイル | トリガ | 内容 |
| --- | --- | --- | --- |
| WebGL Build | [`.github/workflows/webgl-build.yml`](../.github/workflows/webgl-build.yml) | `master` への push / PR / 手動 | Addressable key の自動付与 → WebGL ビルド → 成果物アップロード |
| Unity Activation File | [`.github/workflows/unity-activation.yml`](../.github/workflows/unity-activation.yml) | 手動のみ | Unity ライセンス（`.ulf`）発行用のリクエストファイルを作る |

---

## 1. 最初にやること: Unity ライセンスの登録

CI 上の Unity はライセンス無しでは起動しない。**リポジトリの Settings → Secrets and variables → Actions** に登録する。

### Unity Personal の場合

1. Actions タブから **Unity Activation File** を手動実行する。
2. 完了後、成果物 `Unity_v6000.3.23f1.alf` をダウンロードして展開する。
3. https://license.unity3d.com/manual に `.alf` をアップロードし、`Unity_v6000.x.ulf` をダウンロードする。
4. `.ulf` の**中身をそのまま**シークレット `UNITY_LICENSE` に貼り付ける。
5. `UNITY_EMAIL` / `UNITY_PASSWORD` に Unity ID の資格情報を登録する。

### Unity Pro / Plus の場合

`UNITY_LICENSE` の代わりに `UNITY_SERIAL` を登録し、`webgl-build.yml` の `env:` に
`UNITY_SERIAL: ${{ secrets.UNITY_SERIAL }}` を足す（`UNITY_EMAIL` / `UNITY_PASSWORD` は共通）。

## 2. ビルドの流れ

1. **チェックアウト**（Git LFS 込み）
2. **ディスク解放** — Android SDK 等を消す。WebGL の中間ファイルでランナーが溢れるため。
3. **NuGet 復元** — `Assets/Packages` は `.gitignore` 済みなので、`Assets/packages.config` から
   [NuGetForUnity CLI](https://github.com/GlitchEnzo/NuGetForUnity) で復元する。これが無いと R3 が解決できずコンパイルが落ちる。
4. **`Library` のキャッシュ** — 初回は 1 時間近くかかるが、2 回目以降は大幅に短縮される。
5. **ビルド** — `game-ci/unity-builder` が `MixVerse.CI.CIBuild.BuildWebGL` を実行する。
   この中で Addressable key の付与 → `BuildPipeline.BuildPlayer` を行う。
6. **Addressable key のコミット** — 差分があれば `Assets/AddressableAssetsData` を push し返す。
   PR ビルドでは push しない。
7. **成果物アップロード** — `MixVerse-WebGL-<sha>` という名前で `build/WebGL` を丸ごと上げる。

> Addressables のコンテンツ（`.bundle` / catalog）は Addressables の
> *Build Addressables on Player Build* 設定に従い、プレイヤービルドと同時に生成される。

## 3. Addressable key の付与ルール

`Assets/CI/Editor/AddressableKeyAssigner.cs`。

**key = `親フォルダ名` + `_` + `拡張子を除いたアセット名`**

| アセット | key |
| --- | --- |
| `Assets/Sound/talk_1.wav` | `Sound_talk_1` |
| `Assets/Prefabs/Card.prefab` | `Prefabs_Card` |
| `Assets/Materials/TableMaterial.mat` | `Materials_TableMaterial` |

- 対象フォルダは `DefaultTargetFolders`（`Data` / `Materials` / `Prefabs` / `Shaders` / `Sound` / `Textures`）。
  サブフォルダも再帰的に走査し、key にはその**直上の**フォルダ名を使う。
- `.cs` / `.asmdef` / `.asmref` / `.dll` / `.rsp` / `.preset` は対象外。
- エントリは Addressables の **Default Local Group** に入る。
- key が衝突した場合（別フォルダの同名アセットなど）、先に見つかった方が勝ち、
  もう一方は `Debug.LogError` で報告される。ビルド自体は止めない。

現時点の対象アセットは 42 件で、key の衝突は無い。

### 実行方法

| 用途 | 方法 |
| --- | --- |
| エディタから手動 | メニュー **Tools → MixVerse → Assign Addressable Keys** |
| CI のビルド中 | `CIBuild.BuildWebGL` が自動で呼ぶ |
| CI で単体実行 | `-executeMethod MixVerse.CI.AddressableKeyAssigner.Run`（key 衝突があれば exit 1） |

対象フォルダはコマンドラインからも上書きできる。

```bash
-executeMethod MixVerse.CI.AddressableKeyAssigner.Run -addressableTargetFolders "Assets/Sound,Assets/Prefabs"
```

## 4. 既知の制限

- **`Assets/HIVEMIND`（有料アセット）は CI に存在しない。**
  `.gitignore` 済みなので、これを参照しているプレハブ / シーンは CI ビルドでは
  Missing 参照になる。ビルド自体は通るが、成果物の見た目はローカルと一致しない。
  一致させたい場合は、ライセンス上問題ない範囲で Git LFS に入れるか、
  ビルド前に社内ストレージから展開するステップを足す必要がある。
- **key はフォルダ階層を畳まない。**
  `Assets/Shaders/Materials/CardDissolveMaterial.mat` の key は `Shaders_Materials_...` ではなく
  `Materials_CardDissolveMaterial` になる（直上のフォルダ名しか使わないため）。
  別階層に同名フォルダ・同名アセットがあると衝突する。
- **アセット名がそのまま key になる。**
  `Assets/Textures/noise (2).jpg` は `Textures_noise (2)` になる。
  key を綺麗にしたい場合はアセット側をリネームする。
- **Unity Personal のライセンスは同時アクティベーション数に上限がある。**
  ローカルとの取り合いになる場合は Pro シリアルへの切り替えを検討する。

## 5. よくある変更

<details>
<summary>Addressable key の対象フォルダを変える</summary>

`Assets/CI/Editor/AddressableKeyAssigner.cs` の `DefaultTargetFolders` を編集する。
</details>

<details>
<summary>key の自動 push をやめる</summary>

`webgl-build.yml` の `Commit Addressable keys` ステップを削除し、
`permissions: contents: write` を `contents: read` に戻す。
</details>

<details>
<summary>GitHub Pages に自動デプロイする</summary>

リポジトリ設定で Pages のソースを **GitHub Actions** にしたうえで、
`build/WebGL/MixVerse` を `actions/upload-pages-artifact` → `actions/deploy-pages` に流すジョブを足す。
WebGL のビルド圧縮は Pages が対応する **Disabled** か **Gzip + Decompression Fallback** にしておくこと。
</details>

<details>
<summary>Unity のバージョンを上げる</summary>

`ProjectSettings/ProjectVersion.txt` と `webgl-build.yml` の `env.UNITY_VERSION`、
`unity-activation.yml` の `unityVersion` を揃える。
対応するイメージが [unityci/editor](https://hub.docker.com/r/unityci/editor/tags) にあることも確認する。
</details>
