# Spin the Scrap — 開発メモ

Unity 6 / C# の協力ローグライト。要件書の内容は README.md と docs/ を参照。

## ルール

- ゲームルール（スロット・ラン進行・数値）は `Assets/SpinTheScrap/Scripts/Core` に書く。Core は `noEngineReferences: true` なので UnityEngine を使わない。
- Core と `Tests/EditMode` は Unity と dotnet の両方でコンパイルされる。C# 9 まで（file-scoped namespace、record、global using は不可）。
- テストは NUnit と Core だけに依存させる。
- 表示文字列は `Resources/Localization/strings.csv`（key,ja,en）に追加し、コードに直書きしない。
- MonoBehaviour / ScriptableObject はファイル名とクラス名を一致させる。
- Unity 6 の API 名を使う（`Rigidbody.linearVelocity` / `angularDamping`、`FindObjectsByType`）。
- シーンやアセットの YAML は手書きせず、`Scripts/Editor/SpinTheScrapMenu.cs` のようにエディタで生成する。

## コマンド

```bash
dotnet test Tools/CoreTests                              # Core のテスト
dotnet run --project Tools/Simulator -- --all            # リール期待値
```
