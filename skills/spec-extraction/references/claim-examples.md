# C# コード → claim の実例集

「実装のこの形を見たら、こう claim に落とす」の対訳集。statement は肯定形・決定的・
ドメイン語彙、evidence は必ず実在の行に紐づける（以下の行番号は例）。

## 例1: 入力ガード（エラー畳み込み×否定 / 境界）

```csharp
// OrderForm.cs
private void btnOK_Click(object sender, EventArgs e)          // L140
{
    int qty;
    int.TryParse(txtQty.Text, out qty);                       // L143: 失敗しても続行
    if (chkExpress.Checked && qty > 100)                      // L145
    {
        MessageBox.Show("特急便は100個まで");                  // L147
        return;
    }
    if (!_service.IsOverStock(qty))                           // L150
        _service.Register(_orderNo, qty);                     // L151
}

// OrderService.cs
public bool IsOverStock(int qty)                              // L85
{
    try { return qty > GetStock(); }                          // L87
    catch { return false; }                                   // L88: エラー時 false
}
```

```yaml
claims:
  - id: CLM-001
    kind: declared
    category: boundary
    statement: 特急便の注文は 100 個以下のみ受理される（境界 100 は含む: > 100 で拒否）。
    evidence: [{path: src/OrderForm.cs, lines: "145-148", note: "ガードとメッセージ"}]
    questions_applied: [4]
    confidence: high
    verify_with: enumeration
    invariant: "ExpressCap == express => qty <= 100"

  - id: CLM-002
    kind: implicit
    category: empty-default
    statement: >
      数量欄が空・非数値のまま登録すると数量 0 として扱われ、以降の全チェックを通過して
      0 個の注文が登録される。
    evidence: [{path: src/OrderForm.cs, lines: "143", note: "TryParse 失敗を無視"}]
    questions_applied: [1, 2]
    confidence: high
    verify_with: enumeration

  - id: CLM-003
    kind: implicit
    category: error-handling
    statement: >
      在庫数の取得に失敗した場合、在庫超過とは判定されない（fail-open）。呼び出し側は
      否定（!IsOverStock）で使っているため、取得エラー時ほど登録が通りやすくなる。
    evidence:
      - {path: src/OrderService.cs, lines: "87-88", note: "catch で false"}
      - {path: src/OrderForm.cs, lines: "150", note: "否定で使用"}
    questions_applied: [2, 3]
    confidence: high
    verify_with: enumeration
    invariant: "FailClosed == stock_error => !registered"
```

ポイント: CLM-003 が**エラー畳み込み×否定**の典型。「不一致とエラーを同じ false に畳む」×
「呼び出し側の否定」で、壊れた入力ほど通る。問い 2 と 3 をセットで投げると機械的に見つかる。

## 例2: 並行（判定と記録の分離 / TOCTOU）

```csharp
// InvoiceForm.cs
private void btnIssue_Click(object sender, EventArgs e)       // L200
{
    if (_dao.CountToday() >= _dailyCap) return;               // L202: 読んで判定
    _worker.RunWorkerAsync();                                 // L204: 書くのは後で非同期
}
private void Worker_DoWork(object s, DoWorkEventArgs e)
{
    _dao.InsertInvoice();                                     // L210: ここで記録
}
```

```yaml
claims:
  - id: CLM-004
    kind: declared
    category: boundary
    statement: 請求書の発行は1日あたり _dailyCap 件を超えない。
    evidence: [{path: src/InvoiceForm.cs, lines: "202", note: "上限ガード"}]
    questions_applied: [4, 5]
    confidence: medium     # ← 実際は破れる疑いがあるので medium
    verify_with: tla
    invariant: "NeverOverCap == issued <= dailyCap"

  - id: CLM-005
    kind: implicit
    category: ordering
    statement: >
      上限判定（CountToday）と記録（InsertInvoice）が分離しており、判定後〜記録前の間に
      ボタンが再度押せる。連打または複数端末の同時操作で上限を超えて発行され得る。
    evidence:
      - {path: src/InvoiceForm.cs, lines: "202-204", note: "read-then-act"}
      - {path: src/InvoiceForm.cs, lines: "210", note: "書き込みは worker"}
    questions_applied: [5, 9]
    confidence: high
    verify_with: tla
```

ポイント: declared（上限がある）と implicit(その上限は破れる構造)を**両方**立てる。
PHASE 3 では CLM-004 の invariant `NeverOverCap` を主張し、TLC に反例を出させる
（反例志向）。反例の witness がそのまま CLM-005 の証拠になる。

## 例3: 化石コメント

```csharp
// 前システムの仕様に合わせて 0 は除外（理由不明）                // L55
if (code == 0) continue;                                       // L56
```

```yaml
  - id: CLM-006
    kind: declared
    category: other
    statement: コード 0 の明細は集計から除外される（理由はコード上不明 — 化石コメント）。
    evidence: [{path: src/Aggregator.cs, lines: "55-56"}]
    questions_applied: [1]
    confidence: low        # 化石 → 要ドメイン確認
    verify_with: manual    # 機械検証でなくドメイン質問行き
```

化石は検証しても意味がない（実装通りに動くことしか証明できない）。
`verify_with: manual` にして PHASE 4 で「コード 0 除外は今も意図ですか?」の質問に直行させる。
