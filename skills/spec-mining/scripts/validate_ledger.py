#!/usr/bin/env python3
"""spec-mining 台帳バリデータ。

Usage:
    python validate_ledger.py <claims.yaml | counterexamples.yaml | ledger.yaml> [...]

ファイル名でスキーマを判別し、references/ledger-format.md の必須キー・列挙値・
整合規則を検証する。全ファイル OK なら exit 0、違反があれば違反一覧を出して exit 1。
"""
import re
import sys
from pathlib import Path

try:
    import yaml
except ImportError:
    print("ERROR: PyYAML が必要です。`pip install pyyaml` を実行してください。", file=sys.stderr)
    sys.exit(2)

CATEGORIES = {
    "boundary", "error-handling", "empty-default", "ordering", "trust",
    "cross-boundary", "state", "idempotency", "equivalence", "other",
}
KINDS = {"declared", "implicit"}
CONFIDENCES = {"high", "medium", "low"}
VERIFY_WITH = {"enumeration", "z3", "tla", "manual", "skip"}
VERDICTS = {"proved", "counterexample", "untested", "not-applicable"}
TRACE_STATUSES = {"reproduced", "model-artifact", "pending"}
DISPOSITIONS_CEX = {"bug-candidate", "question-open", "spec-confirmed"}
VERDICT_DISPOSITIONS = {
    "proved": {"contract-locked", "spec-confirmed"},
    "counterexample": {"bug-candidate", "question-open", "spec-confirmed"},
    "untested": {"dropped"},
    "not-applicable": {"dropped"},
}

CLM_RE = re.compile(r"^CLM-\d{3,}$")
CEX_RE = re.compile(r"^CEX-\d{3,}$")
QST_RE = re.compile(r"^QST-\d{3,}$")
TARGET_RE = re.compile(r"^[a-z0-9]+(-[a-z0-9]+)*$")


class Checker:
    def __init__(self, path: Path):
        self.path = path
        self.errors: list[str] = []

    def err(self, where: str, msg: str):
        self.errors.append(f"{self.path}: {where}: {msg}")

    def require(self, obj: dict, key: str, where: str) -> bool:
        if not isinstance(obj, dict) or obj.get(key) in (None, "", []):
            self.err(where, f"必須キー `{key}` が欠落または空")
            return False
        return True

    def enum(self, obj: dict, key: str, allowed: set, where: str):
        if self.require(obj, key, where) and obj[key] not in allowed:
            self.err(where, f"`{key}: {obj[key]}` は不正。許される値: {sorted(allowed)}")


def check_claims(doc, ck: Checker):
    if not ck.require(doc, "target", "top") or not TARGET_RE.match(str(doc["target"])):
        ck.err("top", "target は kebab-case であること")
    if ck.require(doc, "source", "top"):
        for i, s in enumerate(doc["source"]):
            ck.require(s, "path", f"source[{i}]")
            ck.require(s, "revision", f"source[{i}]")
    if not ck.require(doc, "claims", "top"):
        return
    seen = set()
    for i, c in enumerate(doc["claims"]):
        cid = c.get("id", f"claims[{i}]")
        where = str(cid)
        if not CLM_RE.match(str(cid)):
            ck.err(where, "id は CLM-NNN 形式であること")
        if cid in seen:
            ck.err(where, "id が重複")
        seen.add(cid)
        ck.enum(c, "kind", KINDS, where)
        ck.enum(c, "category", CATEGORIES, where)
        ck.require(c, "statement", where)
        ck.enum(c, "confidence", CONFIDENCES, where)
        ck.enum(c, "verify_with", VERIFY_WITH, where)
        if c.get("verify_with") == "skip" and not c.get("skip_reason"):
            ck.err(where, "verify_with: skip には skip_reason が必須")
        if ck.require(c, "evidence", where):
            for j, e in enumerate(c["evidence"]):
                ck.require(e, "path", f"{where}.evidence[{j}]")
                ck.require(e, "lines", f"{where}.evidence[{j}]")
    return {str(c.get("id")) for c in doc["claims"]}


def check_counterexamples(doc, ck: Checker, claim_ids=None):
    ck.require(doc, "target", "top")
    cexs = doc.get("counterexamples") or []
    ids = set()
    for i, c in enumerate(cexs):
        cid = c.get("id", f"counterexamples[{i}]")
        where = str(cid)
        if not CEX_RE.match(str(cid)):
            ck.err(where, "id は CEX-NNN 形式であること")
        ids.add(cid)
        ck.require(c, "check", where)
        ck.require(c, "claim", where)
        if claim_ids is not None and c.get("claim") not in claim_ids:
            ck.err(where, f"claim `{c.get('claim')}` が claims.yaml に存在しない")
        ck.require(c, "witness", where)
        ck.require(c, "summary", where)
        if ck.require(c, "trace", where):
            tr = c["trace"]
            ck.enum(tr, "status", TRACE_STATUSES, f"{where}.trace")
            if tr.get("status") == "reproduced" and not tr.get("steps"):
                ck.err(where, "trace.status: reproduced には steps（file:line の再現手順）が必須")
            if tr.get("status") == "pending":
                ck.err(where, "trace.status: pending のままでは GATE T を通過できない")
        status = (c.get("trace") or {}).get("status")
        if status != "model-artifact":
            ck.enum(c, "disposition", DISPOSITIONS_CEX, where)
            if c.get("disposition") in ("bug-candidate", "question-open"):
                q = c.get("question")
                if not q or not QST_RE.match(str(q)):
                    ck.err(where, "bug-candidate / question-open には question: QST-NNN が必須")
    return ids


def check_ledger(doc, ck: Checker, claim_ids=None, cex_ids=None):
    ck.require(doc, "target", "top")
    ck.require(doc, "updated", "top")
    if not ck.require(doc, "entries", "top"):
        return
    covered = set()
    for i, e in enumerate(doc["entries"]):
        where = f"entries[{i}]({e.get('claim')})"
        ck.require(e, "claim", where)
        covered.add(e.get("claim"))
        if claim_ids is not None and e.get("claim") not in claim_ids:
            ck.err(where, "claims.yaml に存在しない claim")
        ck.enum(e, "verdict", VERDICTS, where)
        v = e.get("verdict")
        if v in ("proved", "counterexample") and not e.get("check"):
            ck.err(where, f"verdict: {v} には check（CHECK_ID）が必須")
        if v == "counterexample":
            cex = e.get("counterexample")
            if not cex or not CEX_RE.match(str(cex)):
                ck.err(where, "verdict: counterexample には counterexample: CEX-NNN が必須")
            elif cex_ids is not None and cex not in cex_ids:
                ck.err(where, f"counterexamples.yaml に `{cex}` が存在しない")
        if v == "untested" and not e.get("untested_reason"):
            ck.err(where, "verdict: untested には untested_reason が必須")
        if v in VERDICT_DISPOSITIONS:
            allowed = VERDICT_DISPOSITIONS[v]
            if e.get("disposition") not in allowed:
                ck.err(where, f"verdict: {v} の disposition は {sorted(allowed)} のいずれか")
    if claim_ids is not None:
        missing = claim_ids - covered
        if missing:
            ck.err("top", f"claims.yaml の claim が台帳に未収載: {sorted(missing)}")


def load(path: Path):
    with open(path, encoding="utf-8") as f:
        return yaml.safe_load(f)


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    paths = [Path(p) for p in argv[1:]]
    all_errors = []
    # 同一ディレクトリの claims/counterexamples を参照整合に使う
    for path in paths:
        if not path.exists():
            all_errors.append(f"{path}: ファイルが存在しない")
            continue
        ck = Checker(path)
        try:
            doc = load(path)
        except yaml.YAMLError as ex:
            all_errors.append(f"{path}: YAML パース失敗: {ex}")
            continue
        name = path.name
        claim_ids = cex_ids = None
        sibling_claims = path.parent / "claims.yaml"
        sibling_cex = path.parent / "counterexamples.yaml"
        if name != "claims.yaml" and sibling_claims.exists():
            try:
                claim_ids = {str(c.get("id")) for c in (load(sibling_claims).get("claims") or [])}
            except yaml.YAMLError:
                pass
        if name == "ledger.yaml" and sibling_cex.exists():
            try:
                cex_ids = {str(c.get("id")) for c in (load(sibling_cex).get("counterexamples") or [])}
            except yaml.YAMLError:
                pass
        if name == "claims.yaml":
            check_claims(doc, ck)
        elif name == "counterexamples.yaml":
            check_counterexamples(doc, ck, claim_ids)
        elif name == "ledger.yaml":
            check_ledger(doc, ck, claim_ids, cex_ids)
        else:
            ck.err("top", "ファイル名は claims.yaml / counterexamples.yaml / ledger.yaml のいずれか")
        all_errors.extend(ck.errors)

    if all_errors:
        print(f"NG: {len(all_errors)} 件の違反\n")
        for e in all_errors:
            print(f"  - {e}")
        return 1
    print(f"OK: {len(paths)} ファイルすべて有効")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
