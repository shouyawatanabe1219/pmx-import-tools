# PMX Import & Animation Tools

Unity 6.3 / URP向けのPMXインポーターと、リグ・ポーズ・アニメーション作成ツールです。MITライセンスで改変・再配布できます。

## 導入

Package Managerの **＋ → Install package from git URL** に以下を入力します。

```text
https://github.com/shouyawatanabe1219/pmx-import-tools.git#v0.2.0
```

Releasesの com.pmximport.tools-0.2.0.tgz を **Install package from tarball** から導入することもできます。ソースフォルダーは **Install package from disk** で package.json を選びます。

Unity 6000.3、URP 17.3.0が対象です。必要なモジュールは依存関係として解決します。描画設定はURPプロジェクトを使ってください。旧Assets版がある場合はバックアップして削除してから導入してください。同じクラスとGUIDを含むため、Assets版とUPM版の併用はできません。

## 使い方

- .pmxとテクスチャをAssetsへ入れ、インポートされたモデルをHierarchyへ配置します。
- ZIPはProjectビューへドラッグ、または **Tools → PMX → ZIPからモデルを読み込む** で読み込みます。
- **Tools → PMX → ポーズ・アニメーション作成** を開き、対象モデルと作業データを指定します。
- Sceneビューでリグを操作し、フレームを記録して .anim に書き出します。

詳細は [アニメーション](Documentation~/Animation.md)、[ZIP読み込み](Documentation~/ZipImport.md)、[物理](Documentation~/Physics.md) を参照してください。

## 対応範囲と制限

頂点・ボーン・材質、頂点モーフとグループモーフ、表情制御、球面テクスチャ、URP Toon描画、剛体・ジョイント、16個のPMX衝突グループ、編集用2ボーンIK、ポーズ記録、AnimationClip書き出しを収録しています。移動後の物理の合わせ直しと、モデル自身のRigidbodyとの衝突除外を備えています。

物理はUnity PhysXによる近似です。PMXの非対称な移動制限・Y/Z回転制限やバネは完全には再現できません。SDEF/QDEFは通常のボーンウェイトへ変換します。PMX固有のIK・付与、骨・UV・材質モーフ、SoftBody、VMD再生、頂点編集、PMXへの書き戻し、実行中のZIP読み込みは未対応です。球面テクスチャはPNG/JPEG、非圧縮BMPに対応します。

モデル・テクスチャ・モーション・音源は同梱していません。各素材の利用条件に従ってください。

## 開発への参加

mainは公開版、codex/developは次の版の統合先です。誰でもForkし、好きなブランチを作って改変し、プルリクエストを送れます。詳しくは [CONTRIBUTING.md](CONTRIBUTING.md) を参照してください。

0.2.0では出典不明だった6ファイルを形式資料とUnity APIに基づく新規実装へ置き換えました。公開リポジトリは新規実装から始まる履歴です。参考資料と依存関係は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、利用条件は [LICENSE.md](LICENSE.md) を参照してください。
