# 段階的ロールアウト戦略

アナライザーをレガシーコードベースへ導入し、定着させるための運用ガイド（PHASE 5）。

## 原則

- **警告の洪水を起こさない**。数百件の warning が一度に出ると、チームは警告全体を無視するようになり、
  アナライザー導入自体が失敗する
- **既存違反の解消**と**新規違反の防止**を分けて扱う（ラチェット方式）
- 誤検知の報告経路を最初に用意する。誤検知を放置すると信頼が消える

## Phase 0: ベースライン計測

導入前にルールごとの違反件数を記録する。改善の証跡になり、経営層/リーダーへの報告にも使える。

```bash
# SDK 形式:
dotnet build MySolution.sln -clp:warningsonly -flp:logfile=warnings.log;warningsonly
# 非 SDK 形式（.NET Framework）:
msbuild MySolution.sln /t:Rebuild /flp:warningsonly;logfile=warnings.log

# ルールIDごとの件数集計
grep -oE "LEG[0-9]+" warnings.log | sort | uniq -c | sort -rn
```

記録先はリポジトリ内の Markdown（例: `doc/analyzer-baseline.md`）に日付つきで残す。

## Phase 1: suggestion で観測（1〜2週間）

```ini
# .editorconfig
dotnet_diagnostic.LEG001.severity = suggestion
```

- ビルドは汚さず、IDE 上でのみ見える状態にする
- この期間の目的は**誤検知の収集**。チームに「おかしい指摘を見つけたら報告してほしい」と明示的に依頼する
- 誤検知が出たらルールのフィルタ条件を改善して再配布。反例をユニットテストに追加してから直す

## Phase 2: 既存違反の一括修正

- **1ルール = 1PR**。レビュー可能な粒度を保つ。件数が多ければディレクトリ/プロジェクト単位で分割
- 分類 A のルールは VS の「ソリューション内のすべての出現箇所を修正」（FixAll）で機械適用する。
  手で直さない — 手修正はばらつきと事故のもと
- PR の必須条件: 既存テストが全部通ること。テストがない領域の修正は、変換の等価性を
  PR 説明で明示する（「throw ex → throw は catch 変数の再送出であり等価」）
- 分類 B のルールの既存違反は、一括修正せず「違反リスト」を issue 化して計画的に潰す

## Phase 3: warning + ラチェット（新規違反ブロック）

既存違反がゼロになったルールから順に warning へ昇格し、CI でエラー扱いにする:

```ini
dotnet_diagnostic.LEG001.severity = warning
```

```yaml
# CI（例: GitHub Actions）。特定 ID のみエラー化して他の警告に影響させない
- run: dotnet build -warnaserror:LEG001,LEG002
# 非 SDK 形式:
- run: msbuild MySolution.sln /p:WarningsAsErrors=LEG001;LEG002
```

既存違反が残っているルールをラチェットしたい場合の選択肢:
1. **ファイル単位の除外**（.editorconfig のセクションで旧コードのパスを suggestion に落とす）— 単純で追いやすい
2. ベースラインファイル方式（違反件数を記録し「増えたら失敗」）— スクリプトが必要だが柔軟

## Phase 4: error 昇格と定着

- warning で数週間安定したら error へ。この時点でルールは「規約」になる
- 新ルールの追加はこのサイクル（Phase 1→4）を再度回す。**新ルールをいきなり error にしない**

## 抑制（suppression)の運用ルール

正当な違反例外は必ず**理由つき**で抑制させる:

```csharp
#pragma warning disable LEG002 // テーブル名は列挙型からの生成で外部入力は混入しない
cmd.CommandText = BuildQuery(table);
#pragma warning restore LEG002
```

- 理由コメントのない `#pragma warning disable` はレビューで却下する運用にする
- 抑制の数を定期的に集計する。特定ルールの抑制が多発するなら、それはルールの欠陥（誤検知）であり、
  ルール側を直すシグナル

## 定着後のメンテナンス

- ルールカタログ（PHASE 3 の表）をリポジトリに置き、ID・意図・経緯を残す
- アナライザーのバージョンアップは NuGet バージョンで管理し、新ルール追加をリリースノートに書く
- 四半期ごとにベースライン集計を再実行し、違反件数の推移を記録する
