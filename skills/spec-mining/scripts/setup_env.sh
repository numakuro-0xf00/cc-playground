#!/usr/bin/env bash
# spec-mining 検証環境の宣言的セットアップ。
# 「ネットワークとローカル環境に依存せず誰でも同じ結果」を担保するため、
# バージョンをここに固定する。ローカルと CI の両方でこのスクリプトだけを使うこと。
set -euo pipefail

PYYAML_VERSION="6.0.2"
Z3_SOLVER_VERSION="4.13.3.0"
TLA2TOOLS_VERSION="1.8.0"   # TLC (The TLA+ model checker)
TLA2TOOLS_URL="https://github.com/tlaplus/tlaplus/releases/download/v${TLA2TOOLS_VERSION}/tla2tools.jar"

TOOLS_DIR="${SPEC_MINING_TOOLS_DIR:-.spec-mining-tools}"
mkdir -p "$TOOLS_DIR"

echo "== python deps =="
python3 -m pip install --quiet "pyyaml==${PYYAML_VERSION}" "z3-solver==${Z3_SOLVER_VERSION}"
python3 -c "import yaml, z3; print('pyyaml', yaml.__version__, '/ z3', z3.get_version_string())"

echo "== tla2tools (TLC) =="
JAR="$TOOLS_DIR/tla2tools-${TLA2TOOLS_VERSION}.jar"
if [ ! -f "$JAR" ]; then
    curl -sSL -o "$JAR" "$TLA2TOOLS_URL"
fi
if command -v java >/dev/null 2>&1; then
    java -cp "$JAR" tlc2.TLC -h >/dev/null 2>&1 || true
    echo "TLC: $JAR (java $(java -version 2>&1 | head -1))"
else
    echo "WARN: java が見つからない。TLA+ を使う check は実行できない（Z3/全列挙のみなら不要）"
fi

echo "OK: 環境準備完了。TLC の jar パスは環境変数 TLA2TOOLS_JAR=$JAR として check から参照すること"
