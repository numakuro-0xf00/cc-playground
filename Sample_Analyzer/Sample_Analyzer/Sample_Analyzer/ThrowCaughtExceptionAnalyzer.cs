using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Sample_Analyzer
{
    /// <summary>
    /// LEG001: catch した例外変数をそのまま `throw ex;` で再送出するコードを検出する。
    /// 不変条件: 再送出は元のスタックトレースを保存すること。
    /// `throw ex;` はスタックトレースを throw 地点で上書きするため、元の発生箇所が失われる。
    /// `throw;`（bare rethrow）への置換は、catch 変数が再代入されていない限り等価変換（分類 A）。
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThrowCaughtExceptionAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "LEG001";

        private const string Title = "再送出は元のスタックトレースを保存すること";
        private const string MessageFormat =
            "'throw {0};' はスタックトレースを破壊します。'throw;' で再送出してください (LEG001)";
        private const string Description =
            "catch した例外変数をそのまま 'throw ex;' で再送出するとスタックトレースが throw 地点で上書きされ、" +
            "例外の元の発生箇所が失われる。'throw;' は元のスタックトレースを保存したまま再送出する等価な構文である。";
        private const string Category = "Usage";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticId,
            Title,
            MessageFormat,
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(AnalyzeThrowStatement, SyntaxKind.ThrowStatement);
        }

        private static void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
        {
            var throwStatement = (ThrowStatementSyntax)context.Node;

            // フィルタ1: 式が単純な識別子であること。
            // `throw;`（引数なし）や `throw new WrapperException(..., ex);`（正当なラップ）を除外する。
            var identifier = throwStatement.Expression as IdentifierNameSyntax;
            if (identifier == null)
            {
                return;
            }

            // フィルタ2: `throw;` に置換して意味が保存される catch 句の直下にあること。
            // finally / ラムダ / ローカル関数の境界を越える場合は `throw;` 自体が不正か意味が変わるため除外。
            var catchClause = FindEnclosingCatchClause(throwStatement);
            if (catchClause == null || catchClause.Declaration == null)
            {
                return;
            }

            // 安価な字面チェック: catch 変数名と throw されている識別子名が一致しなければ対象外
            // （ネストした catch で外側の変数を throw しているケースもここで落ちる）。
            var catchIdentifier = catchClause.Declaration.Identifier;
            if (catchIdentifier.IsKind(SyntaxKind.None)
                || catchIdentifier.ValueText != identifier.Identifier.ValueText)
            {
                return;
            }

            // フィルタ3: 意味レベルの照合 — throw されているのがその catch 句の例外変数そのものであること。
            var semanticModel = context.SemanticModel;
            var thrownSymbol = semanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol as ILocalSymbol;
            if (thrownSymbol == null)
            {
                return;
            }

            var catchSymbol = semanticModel.GetDeclaredSymbol(catchClause.Declaration, context.CancellationToken);
            if (catchSymbol == null || !SymbolEqualityComparer.Default.Equals(thrownSymbol, catchSymbol))
            {
                return;
            }

            // フィルタ4: catch ブロック内で例外変数が再代入されていないこと。
            // 再代入後の `throw ex;` は元の例外と異なるオブジェクトを投げるため、`throw;` への置換は等価でない。
            var dataFlow = semanticModel.AnalyzeDataFlow(catchClause.Block);
            if (dataFlow == null || !dataFlow.Succeeded)
            {
                return;
            }

            foreach (var written in dataFlow.WrittenInside)
            {
                if (SymbolEqualityComparer.Default.Equals(written, catchSymbol))
                {
                    return;
                }
            }

            var diagnostic = Diagnostic.Create(
                Rule, throwStatement.GetLocation(), identifier.Identifier.ValueText);
            context.ReportDiagnostic(diagnostic);
        }

        /// <summary>
        /// throw 文を包含する最も内側の catch 句を返す。
        /// ただし finally 句・ラムダ・ローカル関数・メンバー境界を越える場合は null
        /// （それらの内側では `throw;` が不正、または再送出対象が変わるため）。
        /// </summary>
        private static CatchClauseSyntax FindEnclosingCatchClause(SyntaxNode node)
        {
            for (var current = node.Parent; current != null; current = current.Parent)
            {
                if (current is CatchClauseSyntax catchClause)
                {
                    return catchClause;
                }

                if (current is FinallyClauseSyntax
                    || current is AnonymousFunctionExpressionSyntax
                    || current is LocalFunctionStatementSyntax
                    || current is MemberDeclarationSyntax)
                {
                    return null;
                }
            }

            return null;
        }
    }
}
