using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VerifyCS = Sample_Analyzer.Test.CSharpCodeFixVerifier<
    Sample_Analyzer.ThrowCaughtExceptionAnalyzer,
    Sample_Analyzer.ThrowCaughtExceptionCodeFixProvider>;

namespace Sample_Analyzer.Test
{
    /// <summary>
    /// LEG001: 再送出は元のスタックトレースを保存すること（`throw ex;` → `throw;`）。
    /// 検出ケースはレガシーコードベース（Legacy.Orders.OrderRepository / OrderService）由来の実例。
    /// 反例（検出してはいけないコード）: ラップ再送出・引数なし throw・catch 変数以外の throw・
    /// 再代入された catch 変数・ネストした catch の外側変数・ラムダ内 throw。
    /// </summary>
    [TestClass]
    public class ThrowCaughtExceptionUnitTests
    {
        // ---------------------------------------------------------------
        // 検出ケース（レガシーサンプル由来）
        // ---------------------------------------------------------------

        // OrderRepository.GetOrders 由来: catch (Exception ex) { throw ex; }
        [TestMethod]
        public async Task RethrowOfCaughtVariable_IsFlagged_AndFixedToBareRethrow()
        {
            var test = @"
using System;
using System.Collections;

namespace Legacy.Orders
{
    public class OrderRepository
    {
        public ArrayList GetOrders(string customerId)
        {
            var result = new ArrayList();
            try
            {
                result.Add(customerId);
            }
            catch (Exception ex)
            {
                {|#0:throw ex;|}
            }
            return result;
        }
    }
}";

            var fixedCode = @"
using System;
using System.Collections;

namespace Legacy.Orders
{
    public class OrderRepository
    {
        public ArrayList GetOrders(string customerId)
        {
            var result = new ArrayList();
            try
            {
                result.Add(customerId);
            }
            catch (Exception ex)
            {
                throw;
            }
            return result;
        }
    }
}";

            var expected = VerifyCS.Diagnostic("LEG001").WithLocation(0).WithArguments("ex");
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
        }

        // OrderRepository.ArchiveOrder 由来: ログ出力後の throw ex は検出、
        // 同じメソッド内のラップ再送出 (throw new OrderException(..., ex)) は検出しない。
        [TestMethod]
        public async Task RethrowAfterLogging_IsFlagged_WrappingRethrowInSameMethod_IsNot()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    internal static class Logger { public static void Warn(string m, Exception e) { } }
    internal class OrderException : Exception { public OrderException(string m, Exception e) : base(m, e) { } }

    public class OrderRepository
    {
        public void ArchiveOrder(int orderId)
        {
            try
            {
                Archive(orderId);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(""archive failed"", ex);
                {|#0:throw ex;|}
            }
            catch (Exception ex)
            {
                throw new OrderException(""archive failed for "" + orderId, ex);
            }
        }

        private void Archive(int orderId) { }
    }
}";

            var fixedCode = @"
using System;

namespace Legacy.Orders
{
    internal static class Logger { public static void Warn(string m, Exception e) { } }
    internal class OrderException : Exception { public OrderException(string m, Exception e) : base(m, e) { } }

    public class OrderRepository
    {
        public void ArchiveOrder(int orderId)
        {
            try
            {
                Archive(orderId);
            }
            catch (InvalidOperationException ex)
            {
                Logger.Warn(""archive failed"", ex);
                throw;
            }
            catch (Exception ex)
            {
                throw new OrderException(""archive failed for "" + orderId, ex);
            }
        }

        private void Archive(int orderId) { }
    }
}";

            var expected = VerifyCS.Diagnostic("LEG001").WithLocation(0).WithArguments("ex");
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
        }

        // OrderService.Process 由来 + 複数違反の一括修正（FixAll / BatchFixer の動作確認）
        [TestMethod]
        public async Task MultipleViolations_AreAllFlagged_AndBatchFixed()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (ArgumentException ex)
            {
                {|#0:throw ex;|}
            }
        }

        public void Cancel(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception ex)
            {
                {|#1:throw ex;|}
            }
        }

        private void Validate(int orderId) { }
    }
}";

            var fixedCode = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (ArgumentException ex)
            {
                throw;
            }
        }

        public void Cancel(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private void Validate(int orderId) { }
    }
}";

            var expected = new[]
            {
                VerifyCS.Diagnostic("LEG001").WithLocation(0).WithArguments("ex"),
                VerifyCS.Diagnostic("LEG001").WithLocation(1).WithArguments("ex"),
            };
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixedCode);
        }

        // ---------------------------------------------------------------
        // 反例（検出してはいけないケース）
        // ---------------------------------------------------------------

        // 反例1: 例外のラップ再送出は正当（内部例外としてスタックトレースが保存される）
        [TestMethod]
        public async Task WrappingCaughtException_IsNotFlagged()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    internal class OrderException : Exception { public OrderException(string m, Exception e) : base(m, e) { } }

    public class OrderRepository
    {
        public void ArchiveOrder(int orderId)
        {
            try
            {
                Archive(orderId);
            }
            catch (Exception ex)
            {
                throw new OrderException(""archive failed for "" + orderId, ex);
            }
        }

        private void Archive(int orderId) { }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        // 反例2: 引数なしの `throw;` は既に正しい再送出
        [TestMethod]
        public async Task BareRethrow_IsNotFlagged()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }

        private void Validate(int orderId) { }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        // 反例3: catch 変数以外の throw（catch 内で生成した別の例外、および catch 外のパラメータ throw）
        [TestMethod]
        public async Task ThrowingSomethingOtherThanCaughtVariable_IsNotFlagged()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception ex)
            {
                var wrapped = new InvalidOperationException(""process failed"", ex);
                throw wrapped;
            }
        }

        public void Fail(Exception ex)
        {
            throw ex;
        }

        private void Validate(int orderId) { }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        // 反例4: catch ブロック内で再代入された変数の throw は `throw;` と等価でない
        [TestMethod]
        public async Task ReassignedCaughtVariable_IsNotFlagged()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception ex)
            {
                ex = new InvalidOperationException(""replaced"");
                throw ex;
            }
        }

        private void Validate(int orderId) { }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        // 反例5: ネストした catch から外側の catch 変数を throw
        // （`throw;` に置換すると内側の例外を再送出してしまい、挙動が変わる）
        [TestMethod]
        public async Task ThrowingOuterVariableFromNestedCatch_IsNotFlagged()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception outer)
            {
                try
                {
                    Rollback(orderId);
                }
                catch (Exception inner)
                {
                    throw outer;
                }
            }
        }

        private void Validate(int orderId) { }
        private void Rollback(int orderId) { }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        // 反例6: catch 内のラムダからの throw（ラムダ内では `throw;` が不正）
        [TestMethod]
        public async Task ThrowInsideLambdaWithinCatch_IsNotFlagged()
        {
            var test = @"
using System;

namespace Legacy.Orders
{
    public class OrderService
    {
        public void Process(int orderId)
        {
            try
            {
                Validate(orderId);
            }
            catch (Exception ex)
            {
                Action rethrow = () => { throw ex; };
                rethrow();
            }
        }

        private void Validate(int orderId) { }
    }
}";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }
    }
}
