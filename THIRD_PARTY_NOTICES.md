# 実装の由来と依存関係

0.2.0では、出典を特定できなかった以下の6ファイルの旧実装を廃止し、新しい実装へ置き換えました。公開履歴に旧実装は含めていません。互換性のためクラス名・公開設定・metaのGUIDを維持しています。

- Editor/PmxImporter.cs（形式の読み取りは新設PmxDocument.csへ分離）
- Runtime/PmxPhysicsController.cs
- Runtime/PmxFaceController.cs
- Editor/PmxFaceControllerEditor.cs
- Editor/PmxSphereTextureImporter.cs
- Shaders/MMDToon.shader

PMXのデータ配置の参考資料は [PMX 2.1形式資料](https://gist.github.com/felixjones/f8a06bd48f9da9a4539f) および [OpenMMDの形式資料](https://github.com/dskjal/OpenMMD/blob/main/docs/specification/pmx2.1-specification.md) です。インポート、メッシュ、剛体、ジョイント、Editor UI、URP描画はUnity公式APIを利用します。

- [ScriptedImporter](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/AssetImporters.ScriptedImporter.html)
- [Mesh](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Mesh.html)
- [Rigidbody](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Rigidbody.html)
- [ConfigurableJoint](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ConfigurableJoint.html)
- [URPパスタグ](https://docs.unity3d.com/6000.3/Documentation/Manual/urp/urp-shaders/urp-shaderlab-pass-tags.html)

形式資料の文章やサンプル実装、MMD Toolsのコードは同梱していません。Unity、URPはpackage.jsonの依存関係として導入され、それぞれのライセンスに従います。本リポジトリのコードはLICENSE.mdに記載するMITライセンスです。

モデル、画像、モーション、音源は含まれません。検証に使用した第三者モデルも配布物から除外しています。
