export const meta = {
  name: 'spec-mining',
  description: 'C#/WinForms レガシーから仕様を吸い出し、形式検証でバグ候補と確認質問の台帳を作る',
  whenToUse:
    'spec-mining の対象 target が決まっていて（PHASE 0 済み）、複数 target を ' +
    '吸い出し→検証→突き合わせのパイプラインで一括処理したいとき。' +
    'args: { targets: [{ id: "order-qty-guard", entry: "src/Forms/OrderForm.cs btnOK_Click" }, ...] }',
  phases: [
    { title: 'Extract', detail: 'PHASE 1: 実装から claims.yaml を吸い出す（GATE E）' },
    { title: 'Verify', detail: 'PHASE 2-3: 全列挙/Z3/TLA+ で反例探索（GATE V）' },
    { title: 'Triage', detail: 'PHASE 4: 反例の実装再現・質問化・台帳完成（GATE T）' },
    { title: 'Report', detail: '全 target の台帳を集約' },
  ],
}

const targets = (args && args.targets) || []
if (!targets.length) {
  throw new Error(
    'args.targets が空。PHASE 0（対象選定）を先に行い、' +
    '{ targets: [{ id, entry }] } を渡すこと。手順: skills/spec-mining/SKILL.md PHASE 0'
  )
}

const PHASE_RESULT = {
  type: 'object',
  properties: {
    ok: { type: 'boolean', description: 'GATE スクリプトが exit 0 で通過したか' },
    gateOutput: { type: 'string', description: 'GATE スクリプトの最終実行結果（要約）' },
    files: { type: 'array', items: { type: 'string' }, description: '作成/更新したファイル' },
    summary: { type: 'string', description: '成果の要約（呼び出し元がファイルを開かず判断できる粒度）' },
    judgmentCalls: { type: 'array', items: { type: 'string' }, description: '自己判断した点' },
  },
  required: ['ok', 'gateOutput', 'files', 'summary'],
}

log(`spec-mining: ${targets.length} target をパイプライン処理`)

const results = await pipeline(
  targets,
  t =>
    agent(
      `spec-mining ワークフローの PHASE 1（吸い出し）を実行せよ。\n` +
        `target-id: ${t.id}\n入口: ${t.entry}\n` +
        `出力先: spec-mining/${t.id}/claims.yaml\n` +
        `終了条件: python skills/spec-mining/scripts/validate_ledger.py spec-mining/${t.id}/claims.yaml が exit 0。\n` +
        `通らない場合は修正してリトライし、それでも不可なら ok: false で理由を返すこと。`,
      { agentType: 'spec-extractor', phase: 'Extract', label: `extract:${t.id}`, schema: PHASE_RESULT }
    ),
  (prev, t) => {
    if (!prev || !prev.ok) return { skipped: 'extract 失敗', prev }
    return agent(
      `spec-mining ワークフローの PHASE 2-3（形式化と検証）を実行せよ。\n` +
        `入力: spec-mining/${t.id}/claims.yaml（evidence の C# ソースも必ず読み直すこと）\n` +
        `出力先: spec-mining/${t.id}/model/ と counterexamples.yaml（trace.status: pending）\n` +
        `終了条件: python skills/spec-mining/scripts/run_harness.py spec-mining/${t.id}/model が exit 0。`,
      { agentType: 'formal-verifier', phase: 'Verify', label: `verify:${t.id}`, schema: PHASE_RESULT }
    )
  },
  (prev, t) => {
    if (!prev || !prev.ok) return { skipped: 'verify 失敗または extract 失敗', prev }
    return agent(
      `spec-mining ワークフローの PHASE 4（突き合わせ）を実行せよ。\n` +
        `対象: spec-mining/${t.id}/ 一式。全反例を実装に file:line で手トレースし、` +
        `questions.md と ledger.yaml を完成させること。\n` +
        `終了条件: python skills/spec-mining/scripts/validate_ledger.py ` +
        `spec-mining/${t.id}/counterexamples.yaml spec-mining/${t.id}/ledger.yaml が exit 0。`,
      { agentType: 'counterexample-triager', phase: 'Triage', label: `triage:${t.id}`, schema: PHASE_RESULT }
    )
  }
)

phase('Report')
const REPORT_SCHEMA = {
  type: 'object',
  properties: {
    bugCandidates: { type: 'array', items: { type: 'string' } },
    openQuestions: { type: 'array', items: { type: 'string' } },
    contractsLocked: { type: 'array', items: { type: 'string' } },
    untested: { type: 'array', items: { type: 'string' } },
    failedTargets: { type: 'array', items: { type: 'string' } },
  },
  required: ['bugCandidates', 'openQuestions', 'contractsLocked', 'untested', 'failedTargets'],
}
const report = await agent(
  `spec-mining の全 target の台帳を集約して最終報告を作れ。\n` +
    `対象 target: ${targets.map(t => t.id).join(', ')}\n` +
    `各 spec-mining/<id>/ の ledger.yaml / counterexamples.yaml / questions.md を読み、\n` +
    `バグ候補（witness と業務影響つき・ドメイン語彙で）/ 確認質問 / contract-locked / ` +
    `未検証（理由つき）/ 失敗した target を列挙せよ。ファイルを読まずに要約してはならない。`,
  { phase: 'Report', label: 'aggregate-report', schema: REPORT_SCHEMA }
)

return { perTarget: results, report }
