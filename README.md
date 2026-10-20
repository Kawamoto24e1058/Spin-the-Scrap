# Spin the Scrap

仲間 1〜4 人で廃墟の地下へ階層を下っていく、一人称 3D の協力アクション・ローグライト。
拾ったガラクタを換金してスロット台を回し、武器と強化を手に入れて、ボスをぶっ飛ばす。

> 拾ったガラクタが、そのまま一攫千金のリールになる

- エンジン：Unity 6（URP）/ C#
- マルチプレイ（予定）：FishNet + FishySteamworks（ホスト権威型）
- 設計：[docs/floor-design.md](docs/floor-design.md)（2026-10-21 改訂。要件書 2026-10-20 版の「借金を返す」ループから方向転換した）

## 現在の到達点（試作）

| 項目 | 状態 |
| --- | --- |
| 一人称移動、拾う・運ぶ・投げる（長押しでためる）・落とす、小・大・特大・割れ物の運搬差 | ✅ |
| 階層の進行：通常 → 通常 → ボス（試作は 3 階層、本番 9 階層）、残機 3、倒れると階層をやり直す | ✅ |
| 施設の自動生成（階層ごとに間取りが変わる。入口・ホール・倉庫・中 2 階） | ✅ |
| スロット台 4 種（武器・強化・改造・道具）。台ごとに別のリール、回すと音で敵が寄ってくる | ✅ 入口に武器・強化・道具、奥にもう 1 台、ボス階層に改造 |
| 盤面の値をタグごとに合計 → 系統と質で出る物が決まる（隣接効果やラインで伸ばすほど良い物） | ✅ エンジン非依存 + テスト |
| 武器 11 種（鉄パイプ・釘打ち機・リベット砲・スタンガン・放電銃・花火筒・呪いの包丁など）、改造 3 つまで | ✅ |
| 強化：タグのレベル（攻撃・ひるみ・体力・会心・速さ・爆発範囲・呪い・換金レート） | ✅ |
| 敵：這う者（群れ、湧き直す）・徘徊者（音と光に寄る）・目の見えない巨体（ボス、音へ突進して壁でひるむ） | ✅ ボス専用の階層はこれから |
| シンボル 20 種・レリック 10 種 | ✅ [docs/mvp-content.md](docs/mvp-content.md) |
| 体力・落下ダメージ・電気が流れる水たまり（金属を投げ込むとショート） | ✅ |
| 台車・電池・包帯・爆竹・絶縁ブーツ（道具スロットで出る） | ✅ |
| 効果音（コードで合成した仮素材） | ✅ 外部素材なし |
| ボス専用の階層、改造スロットを奥にも置く | 未着手 |
| フェーズ 1：協力プレイ（FishNet / Steam P2P、特大の共同運搬、倒れた仲間の運搬） | 未着手 |

## はじめかた

1. Unity Hub で **Unity 6（6000.4.6f1）** をインストールする
2. Unity Hub →「Add project from disk」でこのリポジトリのルートを開く
3. `Assets/SpinTheScrap/Scenes/Phase0.unity` を開いて Play

URP Asset（`Assets/SpinTheScrap/Settings/URP.asset`）、入力方式（Input System）、シーンは設定済みです。作り直すときはメニューから実行します。

- **Spin the Scrap → Set Up Project (URP + Input System)**：URP Asset を作って Graphics・Quality に割り当て、入力方式を Input System にする。仮マテリアルの元になる `Resources/Materials/PrototypeLit.mat` も作る（どのアセットからも参照されないシェーダーはビルドから外れてピンクになるため）
- **Spin the Scrap → Create Phase 0 Scene**：`Phase0.unity` を作り直す

### 操作

| 操作 | キー |
| --- | --- |
| 移動 / 走る / ジャンプ | WASD / Shift / Space |
| 攻撃（押し続けると武器の間隔で続けて攻撃） | 左クリック |
| 拾う・抱える・離す / スロット台を使う / エレベーターに乗る | E |
| 台車を押す・離す | 台車を見て E（離すのは E または G） |
| 投げる | 右クリック長押しでためて、離すと投げる（持っている物、またはインベントリで選択中の物）。0.8 秒で最大 |
| 落とす | G |
| インベントリ枠の選択 | 1〜4 |
| 懐中電灯（電池が切れたら予備に自動で交換） | L |
| 包帯を使う / 爆竹を投げる | H / Q |
| スロット台のメニュー | Tab（台を見て E でも開く。持っている物を台に入れる） |
| カーソル解放 | Esc（クリックで戻る） |

- **小**：インベントリ枠に入る（4 枠）。枠が埋まっていても手で持てる
- **大**：両手で抱える。走れず、重さぶん遅くなる
- **特大**：つかんだ端を引っ張る。持ち上げる力は 1 人 260N で重さ（392N）に届かず、2 人分で持ち上がる設計（フェーズ 1 で検証）。水平に引く力は 300N で、1 人でもゆっくり引きずれる（移動速度 35%、つかんだ点から 1.9m 以上は離れられない）
- **割れ物**：3m/s 以上の衝突で傷み、9m/s で割れる。状態が換金のメダルに反映される

## 構成

```
Assets/SpinTheScrap/
  Scripts/Core/       エンジン非依存のゲームロジック（noEngineReferences）
                        SlotMachine  抽選と役判定 / Machines  スロット台の種類と結果の読み取り
                        RunState  ランの進行（階層・残機・メダル・リール・装備）
                        Weapons / Perks  武器と改造・強化と戦闘の計算
                        EnemyBrain  敵の判断 / FacilityLayout  施設の間取り
                        MvpContent   シンボル・レリック定義 / NumberFormatter  万・億・兆、K・M・B
  Scripts/Runtime/    Unity 側（移動・運搬・戦闘・敵・スロット台・仮 UI・効果音）
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

- スロットはゲーム内通貨（メダル）のみで回します。現実のお金で回す・購入する手段や有料ランダムアイテムは実装しません（要件書 5.6）。
- 実在の企業名・商標・既存作品のキャラクターは使いません。外部素材はライセンス・出典・クレジット要否を一覧で管理します。
- 数値はすべて調整前の初期値です。
