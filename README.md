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
| 小・大・特大・割れ物の運搬差、重さによる速度・ジャンプ・段差の低下 | ✅ |
| 脱出 → 店（即売）/ リール投入 → スロット → 返済日 のループ（仮 UI） | ✅ |
| スロットのロジック（3×5、タグ、効果、ライン、四隅、レリック、インフレ表示） | ✅ エンジン非依存 + テスト 46 件 |
| シンボル 10 種・レリック 5 種（MVP） | ✅ [docs/mvp-content.md](docs/mvp-content.md) |
| リール期待値シミュレーター | ✅ `Tools/Simulator` |
| フェーズ 1：協力プレイ（FishNet / Steam P2P、共同運搬、死体運搬） | 未着手 |
| 敵・ハザード・道具（台車など）・ピン | 未着手 |

## はじめかた

1. Unity Hub で **Unity 6（6000.0 系）** をインストールする
2. Unity Hub →「Add project from disk」でこのリポジトリのルートを開く
   - `ProjectVersion.txt` と違うバージョンを使う場合は、Hub の案内どおりに開いて問題ありません
   - Input System を有効にするか聞かれたら「Yes」（無効のままでも旧 Input Manager で動きます）
3. メニュー **Spin the Scrap → Create Phase 0 Scene** を実行
   → `Assets/SpinTheScrap/Scenes/Phase0.unity` が作られて開くので、Play

URP を使う場合は、Project ウィンドウで *Create → Rendering → URP Asset (with Universal Renderer)* を作り、*Project Settings → Graphics* に割り当ててください。未設定の間は Built-in レンダラーで描画されます（マテリアルはコードで作っており、どちらでも色が付きます）。

### 操作（フェーズ 0）

| 操作 | キー |
| --- | --- |
| 移動 / 走る / ジャンプ | WASD / Shift / Space |
| 拾う・抱える・離す | E または 左クリック |
| 投げる | 右クリック（持っている物、またはインベントリで選択中の物） |
| 落とす | G |
| インベントリ枠の選択 | 1〜4 |
| 運び出す（緑の床の上で） | F |
| 懐中電灯 | L |
| 拠点メニュー（店・リール・闇市・スロット・返済） | Tab |
| カーソル解放 | Esc（クリックで戻る） |

- **小**：インベントリ枠に入る（4 枠）。枠が埋まっていても手で持てる
- **大**：両手で抱える。走れず、重さぶん遅くなる
- **特大**：つかんだ端を引っ張る。1 人の力（260N）では重さ（392N）を持ち上げられず、引きずるのが限界。2 人分の力で持ち上がる設計（フェーズ 1 で検証）
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
