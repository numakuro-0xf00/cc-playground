# .NET Framework 固有の制約と対処

対象コードベースが .NET Framework（4.x 以前 / 非 SDK 形式 csproj / packages.config）の場合に
アナライザー開発・適用で踏む制約のまとめ。実装（PHASE 4）前に必ず確認する。

## 1. アナライザー自体は netstandard2.0 で作る

アナライザーはコンパイラ（VS / MSBuild）内で動くため、**対象コードが .NET Framework でも
アナライザープロジェクトは netstandard2.0 のままでよい**。分析はソースレベルで行われる。
このリポジトリの `Sample_Analyzer/` がそのままテンプレートになる
（Analyzer / CodeFixes / Package / Test / Vsix の5プロジェクト構成）。

## 2. Microsoft.CodeAnalysis のバージョン = 動作可能な最低 VS バージョン

アナライザーが参照する Roslyn バージョンより古い VS ではロードされない。
**チームで最も古い VS に合わせて参照バージョンを決める**こと。

| Microsoft.CodeAnalysis | 最低動作環境 |
|---|---|
| 3.x（Sample_Analyzer は 3.3.1） | VS2019 (16.x) |
| 4.0〜4.4 | VS2022 (17.0〜17.4) |
| 4.8+ | VS2022 17.8+ |

新しい Roslyn API（`IOperation` の拡張、インターセプタ等）を使いたくても、
チームの VS が古ければ使えない。バージョンを上げる前に必ず確認する。

## 3. CodeFix が出力してよい構文 ≦ 対象プロジェクトの LanguageVersion

**最頻の事故**: CodeFix が対象プロジェクトの C# バージョンより新しい構文を生成し、修正後にコンパイルエラーになる。

| 構文 | 必要な C# | .NET Framework での目安 |
|---|---|---|
| `?.` null 条件演算子, `nameof` | 6.0 | VS2015+ ならほぼ可 |
| パターンマッチング `is T x` | 7.0 | csproj の LangVersion 次第 |
| using 宣言（`using var x = ...`） | 8.0 | **.NET Framework では既定で不可**（LangVersion 7.3 が上限扱い） |
| null 許容参照型, switch 式 | 8.0 | 同上 |

対処:
- CodeFix 内で `((CSharpParseOptions)document.Project.ParseOptions).LanguageVersion` を確認し、
  出力する構文を切り替える（例: using **文**は C# 1.0 から使えるので、using **宣言**ではなく文を生成する）
- 迷ったら常に古い構文で生成する。using 文・完全修飾名・明示的型は全バージョンで安全

## 4. 非 SDK 形式 csproj / packages.config への導入

### PackageReference が使える場合（推奨）
`skills/roslyn-analyzer/SKILL.md` STEP 6 の通り。

### packages.config の場合
NuGet 復元でアナライザーは自動配線**されない**。csproj に `<Analyzer>` アイテムを手で追加する:

```xml
<ItemGroup>
  <Analyzer Include="..\packages\MyAnalyzer.1.0.0\analyzers\dotnet\cs\MyAnalyzer.dll" />
  <Analyzer Include="..\packages\MyAnalyzer.1.0.0\analyzers\dotnet\cs\MyAnalyzer.CodeFixes.dll" />
</ItemGroup>
```

プロジェクト数が多い場合は、リポジトリルートの `Directory.Build.props`（非 SDK 形式でも MSBuild 15+ なら有効）に
まとめて書くと全プロジェクトへ一括導入できる:

```xml
<Project>
  <ItemGroup>
    <Analyzer Include="$(MSBuildThisFileDirectory)tools\analyzers\MyAnalyzer.dll" />
  </ItemGroup>
</Project>
```

## 5. 一括修正（FixAll）の手段

| 手段 | 使える条件 | 備考 |
|---|---|---|
| VS の電球メニュー →「ソリューション内のすべての出現箇所を修正」 | 常に可 | 最も確実。BatchFixer 実装が前提 |
| `dotnet format analyzers --diagnostics LEG001` | SDK 形式 csproj のみ | 非 SDK 形式のレガシーでは**使えない** |
| 自作コンソールツール（`MSBuildWorkspace` + `WellKnownFixAllProviders.BatchFixer` を programmatic に駆動） | MSBuild がロードできれば可 | CI で全体適用したい場合の最終手段。工数がかかるので件数が多い時のみ |

非 SDK 形式のレガシーでは実質 **VS の Fix All が主力**。CodeFix 実装時に
`GetFixAllProvider()` が `WellKnownFixAllProviders.BatchFixer` を返すことを必ず確認する。

## 6. severity 制御の手段

- **.editorconfig**（`dotnet_diagnostic.LEG001.severity = warning`）: VS2019 16.3+ でコンパイラが解釈。
  非 SDK 形式プロジェクトでも有効
- **ruleset ファイル**: それ以前の環境向けのレガシー手段。`<CodeAnalysisRuleSet>` で指定
- チームの VS が 16.3 以上なら .editorconfig に統一する

## 7. 生成コード・レガシー特有の除外対象

.NET Framework のレガシーには機械生成ファイルが大量にある。二重に防御する:

1. アナライザー側: `context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)`
2. .editorconfig 側:

```ini
[*.Designer.cs]
dotnet_analyzer_diagnostic.severity = none

[*{.g.cs,.generated.cs}]
dotnet_analyzer_diagnostic.severity = none
```

WinForms の `InitializeComponent`、WebForms の `.aspx.designer.cs`、
データセットの `.Designer.cs`、T4 出力（`.tt` → `.cs`）が典型。

## 8. ビルド環境の注意

- 非 SDK 形式は `dotnet build` でビルドできないことが多い。`MSBuild.exe`（VS 付属）を使う
- 古いテンプレート由来のテストプロジェクトは `netcoreapp3.1` 等の EOL ターゲットのことがある。
  新しい SDK しかない環境では `DOTNET_ROLL_FORWARD=LatestMajor dotnet test ...` で実行できる
  （ビルドは NuGet の参照パックで通るが、**実行**には roll-forward 指定が必要）
- ベースライン計測（PHASE 5）には
  `msbuild MySolution.sln /t:Rebuild /flp:warningsonly;logfile=warnings.log` で警告ログを取り、
  ルール ID ごとに件数を集計する
