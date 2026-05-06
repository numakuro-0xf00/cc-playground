---
name: roslyn-analyzer
description: >
  Roslyn SDK（.NET Compiler Platform）を使ってC#のカスタムアナライザーとコードフィックスを作成・テスト・配布するスキル。
  「アナライザーを作りたい」「カスタムルールを追加したい」「コーディング規約を自動チェックしたい」「コードフィックスを実装したい」
  「NuGetでアナライザーを配布したい」「CIでコード品質をチェックしたい」などのリクエストに対して必ずこのスキルを使用すること。
  テンプレートから任意のカスタムルールを作成し、ユニットテストで動作を保証するところまで一貫してサポートする。
---

# Roslyn Analyzer スキル

## 概要

このスキルでは以下を一貫してサポートします：

1. **アナライザープロジェクトのセットアップ**（テンプレートから）
2. **カスタムルールの実装**（アナライザー + コードフィックス）
3. **ユニットテストで動作保証**（エッジケースまでカバー）
4. **配布・運用**（NuGet / CI連携）

---

## STEP 1: プロジェクトセットアップ

### 前提条件

- Visual Studio（.NET Compiler Platform SDK インストール済み）
  - Visual Studio Installer → 「Visual Studio 拡張機能の開発」ワークロード
  - オプションコンポーネントで「.NET Compiler Platform SDK」にチェック

### ソリューション作成

1. Visual Studio → 新規プロジェクト
2. 「Analyzer with Code Fix (.NET Standard)」テンプレートを選択
3. プロジェクト名を決める（例：`MyAnalyzer`）

### 生成される5つのプロジェクト

| プロジェクト | 役割 |
|---|---|
| `MyAnalyzer` | アナライザー本体 |
| `MyAnalyzer.CodeFixes` | コードフィックス |
| `MyAnalyzer.Package` | NuGetパッケージ生成 |
| `MyAnalyzer.Test` | ユニットテスト ← **最重要** |
| `MyAnalyzer.Vsix` | 動作確認用（F5で第2VSを起動） |

> ⚠️ **注意**: スタートアッププロジェクトが `MyAnalyzer.Vsix` になっているか確認すること。なっていない場合は右クリック→「スタートアッププロジェクトに設定」

---

## STEP 2: ルール設計（実装前に必ず決める）

実装前にユーザーと以下を確認すること：

```
□ 何を検出したいか？（例：const にできる変数、命名規則違反）
□ どのSyntaxKindに反応させるか？（LocalDeclarationStatement, MethodDeclaration など）
□ Syntax解析だけで十分か、Semantic解析も必要か？
□ コードフィックスで自動修正できるか？
□ エッジケースは何か？（複数変数宣言、already const、など）
```

詳細は → `references/rule-design.md`

---

## STEP 3: アナライザー実装

### `MyAnalyzerAnalyzer.cs` の基本構造

```csharp
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MyAnalyzerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "MY001";

    // リソースファイル（Resources.resx）から文字列を取得
    private static readonly LocalizableString Title = ...;
    private static readonly LocalizableString MessageFormat = ...;
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId, Title, MessageFormat, "Usage",
        DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // ← ここで「何に反応するか」を登録
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        // ← ここに検出ロジックを書く
    }
}
```

### 登録アクションの選び方

| アクション | 用途 |
|---|---|
| `RegisterSyntaxNodeAction` | 特定の構文ノードに反応（最もよく使う） |
| `RegisterSymbolAction` | 型・メソッドなどのシンボルに反応 |
| `RegisterSemanticModelAction` | ファイル全体の意味解析 |
| `RegisterCodeBlockAction` | メソッド本体など |

詳細は → `references/analysis-apis.md`

---

## STEP 4: コードフィックス実装

### `MyAnalyzerCodeFixProvider.cs` の基本構造

```csharp
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MyAnalyzerCodeFixProvider)), Shared]
public class MyAnalyzerCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds
        => ImmutableArray.Create(MyAnalyzerAnalyzer.DiagnosticId);

    public override FixAllProvider GetFixAllProvider()
        => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // 対象ノードを探す（AnalyzerのSyntaxKindに合わせる）
        var declaration = root.FindToken(diagnosticSpan.Start).Parent
            .AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().First();

        context.RegisterCodeFix(
            CodeAction.Create(
                title: CodeFixResources.CodeFixTitle,
                createChangedDocument: c => ApplyFixAsync(context.Document, declaration, c),
                equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
            diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        LocalDeclarationStatementSyntax localDeclaration,
        CancellationToken cancellationToken)
    {
        // ← ここにコード変換ロジックを書く
        // Triviaの移動に注意（インデント・空白）
        // 詳細は references/code-fix-patterns.md 参照
    }
}
```

詳細は → `references/code-fix-patterns.md`

---

## STEP 5: ユニットテスト（最重要）

> ⚠️ **カスタムアナライザーは本番で意図しない動作をすることが多い。**
> テストで動作を保証してから配布すること。

### NuGetバージョン競合への対処

テストプロジェクトでバージョン競合エラーが出た場合：
- `MyAnalyzer.Test` の `Microsoft.CodeAnalysis.CSharp.Workspaces` を
  `MyAnalyzer.CodeFixes` が要求するバージョンに合わせて更新する

### テストの書き方

```csharp
// 警告が出るべきケース
[TestMethod]
public async Task ShouldWarn_WhenXxx()
{
    await VerifyCS.VerifyCodeFixAsync(
        @"/* before: [|対象コード|] */",
        @"/* after: 修正後コード */"
    );
}

// 警告が出てはいけないケース
[TestMethod]
public async Task ShouldNotWarn_WhenXxx()
{
    await VerifyCS.VerifyAnalyzerAsync(@"/* 対象外コード */");
}
```

### 必ずテストすべきエッジケース一覧

詳細は → `references/test-patterns.md`

---

## STEP 6: 配布・運用

### NuGetパッケージとして配布

```bash
# パッケージビルド
dotnet build MyAnalyzer.Package -c Release
# → bin/Release/MyAnalyzer.1.0.0.nupkg が生成される
```

### 利用側の設定（PackageReference形式）

```xml
<PackageReference Include="MyAnalyzer" Version="1.0.0">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
</PackageReference>
```

### .NET Framework (packages.config) の場合

```xml
<!-- .csproj に直接追加 -->
<ItemGroup>
  <Analyzer Include="..\packages\MyAnalyzer.1.0.0\analyzers\dotnet\cs\MyAnalyzer.dll" />
</ItemGroup>
```

### CI連携（GitHub Actions）

```yaml
- name: Build & Analyze
  run: dotnet build --warnaserror
  # アナライザー警告をエラーとして扱い、マージをブロック
```

---

## 実装チェックリスト

```
□ Resources.resx の文字列を更新した（Title / MessageFormat / Description）
□ Category を適切に設定した（"Usage" / "Naming" / "Performance" など）
□ Initialize で正しいSyntaxKindを登録した
□ AnalyzeNode に早期リターンを入れた（パフォーマンス配慮）
□ コードフィックスのTriviaを正しく移動した
□ ユニットテストで警告ケース・非警告ケース両方を網羅した
□ NuGetバージョン競合を解消した
□ F5動作確認を行った
```
