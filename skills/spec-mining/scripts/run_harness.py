#!/usr/bin/env python3
"""spec-mining 検証ハーネスランナー（self-check + broken-variant）。

Usage:
    python run_harness.py <model-dir>            # 例: spec-mining/order-qty-guard/model

<model-dir>/checks/chk_*.py と <model-dir>/broken/brk_*.py を発見して実行する。

check モジュールの契約:
    CHECK_ID  = "INV-001"
    CLAIMS    = ["CLM-001"]
    EXPECTED  = "HOLDS" | "VIOLATES"
    def run() -> ("HOLDS", None) | ("VIOLATES", witness_dict)

broken モジュールの契約:
    GUARDS    = "INV-001"        # 守る対象の CHECK_ID
    def run() -> 同上             # わざと壊したモデルに対する検査。VIOLATES であること

合格条件（すべて満たすと exit 0）:
  1. 全 check の結果が EXPECTED と一致（self-check）
  2. 全 broken-variant が VIOLATES を返す（検査が load-bearing である証明）
  3. EXPECTED: HOLDS の check には broken-variant が最低1つ存在する
     （broken-variant の無い緑は「何も検証していない緑」）

人間可読な結果表を stdout に、機械可読な結果を exit code に出す。CI とローカルで
これ1本だけを実行経路にすること。
"""
import importlib.util
import sys
import traceback
from pathlib import Path

RESULTS = ("HOLDS", "VIOLATES")


def load_module(path: Path):
    spec = importlib.util.spec_from_file_location(path.stem, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def run_module(path: Path):
    """(result, witness, error) を返す。契約違反・例外は error に入る。"""
    try:
        mod = load_module(path)
        out = mod.run()
        if not (isinstance(out, tuple) and len(out) == 2 and out[0] in RESULTS):
            return None, None, f"run() の戻り値が契約違反: {out!r}"
        return out[0], out[1], None
    except Exception:
        return None, None, traceback.format_exc(limit=5)


def main(argv):
    if len(argv) != 2:
        print(__doc__)
        return 2
    model_dir = Path(argv[1])
    checks_dir = model_dir / "checks"
    broken_dir = model_dir / "broken"
    check_files = sorted(checks_dir.glob("chk_*.py")) if checks_dir.is_dir() else []
    broken_files = sorted(broken_dir.glob("brk_*.py")) if broken_dir.is_dir() else []

    if not check_files:
        print(f"NG: {checks_dir} に chk_*.py が1つもない")
        return 1

    failures = []
    holds_ids = set()
    guarded_ids = set()

    print(f"== checks ({len(check_files)}) ==")
    for path in check_files:
        try:
            mod = load_module(path)
            check_id = getattr(mod, "CHECK_ID", None)
            expected = getattr(mod, "EXPECTED", None)
            claims = getattr(mod, "CLAIMS", None)
        except Exception:
            failures.append(f"{path.name}: import 失敗\n{traceback.format_exc(limit=3)}")
            print(f"  ERROR  {path.name} (import 失敗)")
            continue
        problems = []
        if not check_id:
            problems.append("CHECK_ID 未定義")
        if expected not in RESULTS:
            problems.append(f"EXPECTED が不正: {expected!r}")
        if not claims:
            problems.append("CLAIMS 未定義（対象 claim ID を宣言すること）")
        if problems:
            failures.append(f"{path.name}: " + "; ".join(problems))
            print(f"  ERROR  {path.name}: " + "; ".join(problems))
            continue
        result, witness, error = run_module(path)
        if error:
            failures.append(f"{path.name} ({check_id}): 実行エラー\n{error}")
            print(f"  ERROR  {check_id}  {path.name}")
            continue
        ok = result == expected
        mark = "PASS " if ok else "FAIL "
        print(f"  {mark}  {check_id}  expected={expected} actual={result}  {path.name}")
        if result == "VIOLATES" and witness is not None:
            print(f"         witness: {witness}")
        if not ok:
            failures.append(
                f"{path.name} ({check_id}): EXPECTED={expected} だが実際は {result}。"
                + (f" witness={witness}" if witness else "")
            )
        if expected == "HOLDS":
            holds_ids.add(check_id)

    print(f"\n== broken-variants ({len(broken_files)}) ==")
    for path in broken_files:
        try:
            mod = load_module(path)
            guards = getattr(mod, "GUARDS", None)
        except Exception:
            failures.append(f"{path.name}: import 失敗\n{traceback.format_exc(limit=3)}")
            print(f"  ERROR  {path.name} (import 失敗)")
            continue
        if not guards:
            failures.append(f"{path.name}: GUARDS 未定義（守る対象の CHECK_ID を宣言すること）")
            print(f"  ERROR  {path.name}: GUARDS 未定義")
            continue
        result, witness, error = run_module(path)
        if error:
            failures.append(f"{path.name} (guards {guards}): 実行エラー\n{error}")
            print(f"  ERROR  guards={guards}  {path.name}")
            continue
        ok = result == "VIOLATES"
        mark = "RED  " if ok else "FAIL "
        print(f"  {mark}  guards={guards}  actual={result}  {path.name}")
        if ok:
            guarded_ids.add(guards)
        else:
            failures.append(
                f"{path.name}: broken-variant が {result} を返した。壊したのに緑 = "
                f"検査 {guards} は何も検証していない可能性が高い"
            )

    unguarded = holds_ids - guarded_ids
    for cid in sorted(unguarded):
        failures.append(
            f"{cid}: EXPECTED=HOLDS だが broken-variant が存在しない。"
            "broken/ にガードを壊した variant を追加すること"
        )

    print()
    if failures:
        print(f"NG: {len(failures)} 件")
        for f in failures:
            print(f"  - {f}")
        return 1
    print(f"OK: checks={len(check_files)} broken={len(broken_files)} — "
          "全 self-check 一致、全 broken-variant 赤")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
