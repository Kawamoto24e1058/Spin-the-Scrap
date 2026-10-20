# Spin the Scrap

借金を背負った仲間 1〜4 人が廃墟でガラクタを回収し、そのガラクタをシンボルにしたスロットを回して返済を目指す、一人称 3D の協力ローグライト。

> 拾ったガラクタが、そのまま一攫千金のリールになる

- エンジン：Unity 6（URP）/ C#
- マルチプレイ（予定）：FishNet + FishySteamworks（ホスト権威型）
- 要件書：Spin the Scrap 要件書（2026-10-20 版）

## 現在の到達点

要件書「12.2 次のアクション」の 1・2・4 に対応しています。

| 項目 | 状態 |
| --- | --- |
| Unity 6 プロジェクト / Git LFS 設定 | ✅ |
| フェーズ 0：一人称移動、拾う・運ぶ・投げる・落とす（1 部屋・ソロ） | ✅ 実装済み（Unity 上での手触り調整はこれから） |
| 施設の自動生成（潜行ごとに間取りが変わる。入口・ホール・倉庫・中 2 階、4 部屋 + サイクルごとに 1 部屋） | ✅ 奥の部屋ほど珍しいガラクタが多い |
| 小・大・特大・割れ物の運搬差、重さによる速度・ジャンプ・段差の低下 | ✅ |
| 脱出 → 店（即売）/ リール投入 → スロット → 返済日 のループ（仮 UI） | ✅ |
| スロットのロジック（3×5、タグ、効果、ライン、四隅、レリック、インフレ表示） | ✅ エンジン非依存 + テスト 46 件 |
| シンボル 10 種・レリック 5 種（MVP） | ✅ [docs/mvp-content.md](docs/mvp-content.md) |
| リール期待値シミュレーター | ✅ `Tools/Simulator` |
| フェーズ 1：協力プレイ（FishNet / Steam P2P、共同運搬、死体運搬） | 未着手 |
| 敵・ハザード・道具（台車など）・ピン | 未着手 |

## はじめかた

1. Unity Hub で **Unity 6（6000.4.6f1）** をインストールする
2. Unity Hub →「Add project from disk」でこのリポジトリのルートを開く
3. `Assets/SpinTheScrap/Scenes/Phase0.unity` を開いて Play

URP Asset（`Assets/SpinTheScrap/Settings/URP.asset`）、入力方式（Input System）、シーンは設定済みです。作り直すときはメニューから実行します。

- **Spin the Scrap → Set Up Project (URP + Input System)**：URP Asset を作って Graphics・Quality に割り当て、入力方式を Input System にする。仮マテリアルの元になる `Resources/Materials/PrototypeLit.mat` も作る（どのアセットからも参照されないシェーダーはビルドから外れてピンクになるため）
- **Spin the Scrap → Create Phase 0 Scene**：`Phase0.unity` を作り直す

### 操作（フェーズ 0）

| 操作 | キー |
| --- | --- |
| 移動 / 走る / ジャンプ | WASD / Shift / Space |
| 拾う・抱える・離す | E または 左クリック |
| 投げる | 右クリック長押しでためて、離すと投げる（持っている物、またはインベントリで選択中の物）。0.8 秒で最大 |
| 落とす | G |
| インベントリ枠の選択 | 1〜4 |
| 運び出す（緑の床の上で） | F |
| 懐中電灯 | L |
| 拠点メニュー（店・リール・闇市・スロット・返済） | Tab |
| カーソル解放 | Esc（クリックで戻る） |

- **小**：インベントリ枠に入る（4 枠）。枠が埋まっていても手で持てる
- **大**：両手で抱える。走れず、重さぶん遅くなる
- **特大**：つかんだ端を引っ張る。持ち上げる力は 1 人 260N で重さ（392N）に届かず、2 人分で持ち上がる設計（フェーズ 1 で検証）。水平に引く力は 300N で、1 人でもゆっくり引きずれる（移動速度 35%、つかんだ点から 1.9m 以上は離れられない）
- **割れ物**：3m/s 以上の衝突で傷み、9m/s で割れる。状態が売値に反映される

## 構成

```
Assets/SpinTheScrap/
  Scripts/Core/       エンジン非依存のゲームロジック（noEngineReferences）
                        SlotMachine  抽選と役判定 / RunState  ランの進行
                        MvpContent   シンボル・レリック定義 / NumberFormatter  万・億・兆、K・M・B
  Scripts/Runtime/    Unity 側（移動・運搬・脱出・仮 UI・ScriptableObject）
  Scripts/Editor/     メニュー「Spin the Scrap」
  Resources/Localization/strings.csv   日英の文字列テーブル
  Tests/EditMode/     Core のテスト（Unity と dotnet の両方で実行）
Tools/
  CoreTests/          Unity なしで Core のテストを実行する .NET プロジェクト
  Simulator/          リール構成ごとの期待値シミュレーター
docs/                 MVP コンテンツ一覧など
```

設計の要点:

- **ゲームロジックは `SpinTheScrap.Core` に分離**しています（Unity の規約変更リスクへの対策、要件書 11）。Core は UnityEngine を参照できない設定です。
- 乱数は状態 1 個（`ulong`）の決定的な実装で、ホストが抽選して結果だけを配信する同期方針（要件書 8.2）に合わせています。
- シンボルの効果は「条件 × 処理」の組み合わせです。新しいシンボルの多くはコードを書かずに作れます。
- シンボル・レリックは **Spin the Scrap → Generate MVP Content Assets** で ScriptableObject に書き出せます。`RunSession` の *Content Asset* に割り当てると、インスペクターで調整した値が使われます。

## テスト・シミュレーション

Unity の Test Runner（EditMode）からも実行できますが、Unity を起動せずに .NET 8 SDK だけでも回せます。

```bash
dotnet test Tools/CoreTests

# 初期リールに各シンボルを 1 個 / 3 個足したときの 1 スピンの分布
dotnet run --project Tools/Simulator -- --all
# 任意の構成
dotnet run --project Tools/Simulator -- --reel empty_can:5,rusty_screw:3 --relics magnet_charm --spins 20000
```

## 注意事項

- スロットはゲーム内通貨のみで回します。現実のお金で回す・購入する手段や有料ランダムアイテムは実装しません（要件書 5.6）。
- 実在の企業名・商標・既存作品のキャラクターは使いません。外部素材はライセンス・出典・クレジット要否を一覧で管理します。
- 数値はすべて調整前の初期値です。
