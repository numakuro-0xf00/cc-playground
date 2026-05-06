# ユニットテストパターン集

## テストの基本構造

```csharp
using VerifyCS = MyAnalyzer.Test.CSharpCodeFixVerifier<
    MyAnalyzer.MyAnalyzerAnalyzer,
    MyAnalyzer.MyAnalyzerCodeFixProvider>;

[TestClass]
public class MyAnalyzerUnitTest
{
    // 警告が出るべきケース + コードフィックスの確認
    [TestMethod]
    public async Task ShouldWarn_WhenXxx()
    {
        await VerifyCS.VerifyCodeFixAsync(
            @"/* [|対象コード|] を含むコード */",
            @"/* 修正後のコード */"
        );
    }

    // 警告が出てはいけないケース
    [TestMethod]
    public async Task ShouldNotWarn_WhenXxx()
    {
        await VerifyCS.VerifyAnalyzerAsync(
            @"/* 対象外のコード */"
        );
    }
}
```

## マークアップ記法

```
[|int i = 0;|]         → この範囲に診断が出ることを期待
{|MY001:int i = 0;|}   → 特定のDiagnosticIdの診断を期待
```

## 必ずテストすべきエッジケース

### カテゴリ1: 既に条件を満たしているケース（警告不要）

```csharp
// すでに const → 警告不要
const int i = 0;
Console.WriteLine(i);
```

```csharp
// すでに対象のキーワードがある → 警告不要
static readonly int i = 0;
```

### カテゴリ2: 初期化に関するケース（警告不要）

```csharp
// 初期化子なし → 警告不要
int i;
i = 0;
Console.WriteLine(i);
```

```csharp
// 初期化子が実行時値 → 警告不要
int i = DateTime.Now.DayOfYear;
Console.WriteLine(i);
```

### カテゴリ3: 後から変更されるケース（警告不要）

```csharp
// 後で代入される → 警告不要
int i = 0;
i = 1;
Console.WriteLine(i);
```

```csharp
// インクリメントされる → 警告不要
int i = 0;
Console.WriteLine(i++);
```

### カテゴリ4: 複数変数宣言（重要）

```csharp
// 片方だけ定数でない → 文全体として警告不要
int i = 0, j = DateTime.Now.DayOfYear;
Console.WriteLine(i);
Console.WriteLine(j);
```

```csharp
// 両方定数 → 警告あり
[|int i = 0, j = 1;|]
Console.WriteLine(i);
Console.WriteLine(j);
```

### カテゴリ5: スコープ関連

```csharp
// クロージャにキャプチャされる → 要確認
int i = 0;
Action a = () => Console.WriteLine(i);
```

## NuGetバージョン競合の解消

テストプロジェクトでビルドエラーが出た場合：

1. `MyAnalyzer.Test` プロジェクトを右クリック → 「NuGetパッケージの管理」
2. 「インストール済み」タブで `Microsoft.CodeAnalysis.CSharp.Workspaces` を確認
3. エラーメッセージで要求されているバージョンに更新する

例：
```
MakeConst.Test -> MakeConst.CodeFixes -> Microsoft.CodeAnalysis.CSharp.Workspaces (>= 5.3.0)
→ MakeConst.Test の Microsoft.CodeAnalysis.CSharp.Workspaces を 5.3.0 に更新
```

## テスト実装の進め方

1. まず「警告が出るべき最もシンプルなケース」を1つ書いてパスさせる
2. 次に「警告が出てはいけないケース」を上のリストから順に追加していく
3. 失敗したテストが出たら `AnalyzeNode` にそのケースへの対処を追加する
4. すべてパスしたら完成

この順番で進めると、アナライザーの実装が自然と堅牢になる。
