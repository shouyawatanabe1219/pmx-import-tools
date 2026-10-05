# PMXの剛体とジョイント

0.2.0のインポーターバージョンは7です。PMXの剛体をUnityのRigidbodyとColliderへ、ばね付き6DOF(type 0)と6DOF(type 1)ジョイントをConfigurableJointへ変換します。Unity PhysXによる近似です。

## 設定

PMXのInspectorで Import Physics と Import Joints を有効にし、モデルをシーンへ配置します。Physics/Rigid_... の各オブジェクトに PmxRigidBody が付きます。

- Bone、Mode、Local Pos/Rot：対応ボーン、駆動方式、初期オフセット。Play前に設定します。
- Collision Group：PMXの0～15グループ。
- Excluded PMX Groups：衝突させないグループのマスク。Unityレイヤーの割り当ては不要です。
- 質量・減衰はRigidbody、寸法はColliderで変更します。
- コントローラーで重力倍率・ソルバー・停止・再開・リセットを設定します。

PmxRigidBodyの設定を正本とし、旧rigidsフィールドも互換用に保持します。実行中に衝突設定を変更するときは SetCollisionFilter(group, mask) またはコントローラーの ReapplyCollisionFilter() を呼びます。駆動モードや対応ボーンは実行前に設定してください。

球・箱・カプセル、3種類の駆動方式、ボーンなし剛体、質量・減衰・反発・摩擦を読み込みます。位置と形状にインポート倍率を適用し、Z軸の反転に合わせて巻き順・回転・制限を変換します。剛体はPlay中にモデルの階層から切り離し、終了・破棄時に片付けます。

## 本体のRigidbodyとの衝突

Ignore Owner Collisions は標準で有効です。モデル本体または親のRigidbodyに属するColliderと、PMX剛体の衝突を除外し、追従剛体が本体を押し続ける現象を防ぎます。床・壁・別のRigidbodyとの衝突は維持します。

本体のColliderなどを追加・変更したら ReapplyCollisionFilter() を呼んでください。無効化・破棄時は、この機能が変更した除外を適用前の状態へ戻します。

## 移動・リグ操作後の合わせ直し

Pose Jump Distance の初期値は0.25m、Pose Jump Angle は25度です。1回の物理更新間にこれを超えてモデルや追従ボーンが変化した場合、剛体を現在のボーンへ合わせ、速度をゼロにします。通常の連続した動きでは物理の揺れを維持します。

FixedUpdateとLateUpdateの両方で変更を確認します。判定を無効にするには各値を0にします。スクリプトで大きく姿勢を変更した場合は ResetPhysics() も利用できます。

## 近似と未対応

Unityの移動制限は共通の半径、Y/Z回転制限は対称値となり、PMXの軸別・非対称制限を完全には表現できません。角度バネはモデルの倍率に応じて変換する近似です。特殊なジョイントtype 2～5は警告してスキップし、元の制限とバネはPmxJointMetadataへ記録します。

PMX固有のIK、付与、SoftBody、Bullet物理の完全再現は未対応です。モデルによって剛体・ジョイントの調整が必要です。非一様スケールを使うモデル全体の拡縮は、Colliderやジョイントの挙動を実モデルで確認してください。

検証結果は [Validation.md](Validation.md) を参照してください。
