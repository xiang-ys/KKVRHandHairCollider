# KKVR ヘア・衣装インタラクション

[English](README.md) | [简体中文](README.zh-CN.md) | **日本語**

これはオリジナルの `KoikatuVR.exe` 向け BepInEx プラグインです。
Quest/SteamVR コントローラーを、ゲームおよび導入済み Mod が**すでに持って
いる可動物理**へ接続します。対象は髪、物理アクセサリー、スカートや衣装の
DynamicBone チェーン、および Unity Cloth を使用する衣装です。

DynamicBone や Unity Cloth を置き換えたり、静的モデルへ物理を自動生成
したりはしません。ゲームと Mod に元から存在する可動部分をできる限り
コントローラー操作へ参加させ、作者が設定したボーン、スキニング、物理
ソルバーを維持することが目的です。

現在のバージョンは **1.0** です。39 件の自動テスト、Release ビルド、
アセンブリ検査、オリジナル VR の起動テスト、標準衣装と Mod 衣装を使った
ゲーム内テストを完了しています。

- [最新 GitHub Release をダウンロード](https://github.com/xiang-ys/KKVRHandHairCollider/releases/latest)
- [変更履歴](CHANGELOG.md)
- [詳細な検証記録](VALIDATION.md)
- [現行バージョンの SHA-256](SHA256SUMS.txt)

本プロジェクトは非公式のコミュニティプロジェクトであり、Illusion とは
関係ありません。ゲーム、BepInEx、VRTK、DynamicBone は含まれません。

## 機能範囲

- オリジナル Koikatu VR の左右 VRTK コントローラーを検出します。
- 髪とアクセサリーへ小さなコントローラー DynamicBone コライダーを追加します。
- 衣装専用の少し広いローカルコライダーを使用し、髪の既存の感触を変えずに
  隣接するスカートチェーンへ接触しやすくします。
- 標準衣装と Mod 衣装が持つ物理チェーンを検出し、標準スカートのボーン名を
  使用しない Mod にも対応します。
- `DynamicBone`、`DynamicBone_Ver01`、`DynamicBone_Ver02` に対応します。
- 既存の Unity Cloth へコントローラー SphereCollider を追加し、衣装側の
  コライダー配列は置き換えません。
- 現行 MoreAccessories の追加スロットと、衣装からアクセサリーへ変換された
  物理ルートに対応します。
- 制限付き速度応答、静止接触、グリップによる引っ張り、過伸長時の自動解除を
  提供します。
- 髪用の頭部カプセルと、スカート用の太ももコライダーを再利用または作成します。
- 着替え、シーン変更、機能無効化時に追加バインドを削除し、一時的な力を戻します。
- ゲーム標準の胸・臀部などの身体物理を除外し、そのパラメーターを変更しません。

## インストール

[GitHub Releases](https://github.com/xiang-ys/KKVRHandHairCollider/releases/latest)
から `KKVRHandHairCollider.dll` をダウンロードし、次の場所へ配置します。

```text
Koikatu/
  KoikatuVR.exe
  BepInEx/
    plugins/
      KKVRHandHairCollider/
        KKVRHandHairCollider.dll
```

オリジナルの `KoikatuVR.exe` を起動します。初回ロード後、次の設定ファイルが
生成されます。

```text
BepInEx/config/local.kkvr.handhaircollider.cfg
```

本プラグインはオリジナルの VRTK ベース Koikatu VR を対象としています。
CharaStudio、VRGIN ビルド、その他の KKVR 派生版では未検証です。

## 動作方式

コントローラーは次のオリジナル API から取得します。

1. `VRTK_DeviceFinder.GetControllerLeftHand/RightHand`。
2. フォールバックとして `VRViveControllerManager.GetTransform(0/1)`。

プラグインは次のオブジェクトをスキャンします。

- `ChaControl.objHair` 以下の髪物理。
- KKAPI アクセサリーオブジェクトと `ChaControl.cusAcsCmp` の和集合。
- `ChaControl.objClothes` のトップ・ボトム衣装スロット。

衣装スロット内で有効かつ物理ルートを持つ DynamicBone はすべて対象です。
標準身体物理と明確に判定されたコンポーネントだけを除外します。無効、ルート
なし、または no-shake のコンポーネントは変更しません。

髪とアクセサリーは従来の `0.035 m` コントローラー球を使用します。衣装は
独立した `0.065 m` 球を使用するため、スカートの判定範囲を広げても髪の
操作感へ影響しません。アクセサリーと衣装ではボーン間の連続線分距離も計算し、
離れたボーン節点の間に判定の空白が生じることを防ぎます。

## 現在の既定設定

```ini
[General]
Enabled = true
Include accessory Dynamic Bones = true
Include skirt Dynamic Bones = true
Scan interval seconds = 1
Tuning version = 7

[Controller collision]
Enabled = true
Radius meters = 0.035

[Controller force]
Enabled = true
Contact padding meters = 0.008
Strength = 0.018
Maximum force = 0.04
Minimum speed meters per second = 0.15
Velocity smoothing = 0.35

[Accessory force]
Enabled = true
Strength = 0.015
Maximum force = 0.03
Stationary contact push = 0.006
Contact padding meters = 0.012

[Clothing collision]
Radius meters = 0.065

[Clothing force]
Enabled = false
Strength = 0.012
Maximum force = 0.025
Stationary contact push = 0.006

[Grab interaction]
Enabled = true
Strength = 0.2
Maximum force = 0.04
Dead zone meters = 0.005
Maximum stretch meters = 0.22

[Head collision]
Enabled = true
Radius meters = 0.075
Height meters = 0.1
Center Y meters = 0.015

[Skirt body collision]
Enabled = true

[Unity Cloth]
Enabled = true
```

衣装チェーン全体へ加える追加 Force は既定で無効です。ローカル衝突へ反応しない
特殊な衣装向けのフォールバックですが、有効化するとチェーン全体が同時に動き、
ローカル衝突より不自然になる場合があります。

キャラクター手コライダーは通常経路ではなくフォールバック機能のため、既定で
無効です。

```ini
[Character hands]
Include character hand colliders = false
Create fallback hand colliders = true
```

## 物理上の制限

- 既存の DynamicBone または Unity Cloth を持つ髪、アクセサリー、衣装だけが
  反応します。静的スキンメッシュを自動的に布へ変換することはできません。
- DynamicBone のルートは固定アンカーです。衣装上部が固定ルートまたは身体
  ボーンだけへウェイトされ、裾だけが動的な子ボーンへウェイトされている場合、
  プラグインで動かせるのは裾だけです。
- 複数のスカートチェーン間には、本物の布のような横方向制約がありません。
  広い衣装コライダーは隣接チェーンへ同時接触できますが、連続した布面を再構築
  するものではありません。
- グリップ操作は既存 DynamicBone の Force を通してチェーン全体を引きます。
  VRChat PhysBone の粒子単位ポージング、摩擦、布の破断とは異なります。
- 実際の見た目は衣装作者が設定したボーン階層、スキンウェイト、剛性、減衰に
  依存します。
- `dictDynamicBoneBust` はスキャンも変更もしません。胸部と臀部の物理はゲーム
  または導入済み身体物理プラグインが管理します。

## ビルドと検証

ターゲットは `net35` です。ローカル Koikatu インストール内のゲームおよび
プラグインアセンブリが必要です。

```powershell
$env:KOIKATU_GAME_DIR = 'D:\Games\Koikatu'
dotnet run --project '.\tests\KKVRHandHairCollider.Tests.csproj' -c Release
dotnet build '.\src\KKVRHandHairCollider.csproj' -c Release
```

1.0 の検証結果：

- 39/39 テスト成功。
- Release ビルドは警告 0、エラー 0。
- アセンブリバージョン `1.0`、CLR `Net_2_0`。
- BepInEx がオリジナル `KoikatuVR.exe` で正常ロード。
- テスト衣装の Mod DynamicBone 20/20 コンポーネントを登録。
- 標準スカート、Mod スカート、物理アクセサリーを手動確認。

詳細な証拠と過去のハッシュは [VALIDATION.md](VALIDATION.md) を参照してください。

## ライセンス

ソースコードは [MIT License](LICENSE) で公開されています。ゲームおよび
第三者アセンブリは再配布しません。
