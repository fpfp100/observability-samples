# Python Migration Test Results

Tracks testing progress against [migration-test-plan.md](migration-test-plan.md) for 4 configurations:
- **BM** — Base SDK + Manual
- **BA** — Base SDK + Auto
- **DM** — Distro + Manual
- **DA** — Distro + Auto

## Test Progress (as of 2026-04-26)

| # | Test Area | Status | Notes |
|---|-----------|--------|-------|
| 1 | Scopes (InvokeAgent, Inference, ExecuteTool, Output) | NOT STARTED | |
| 2 | Error Handling on Scopes | NOT STARTED | |
| 3 | BaggageBuilder | NOT STARTED | |
| 4 | Baggage Middleware | NOT STARTED | |
| 5 | BatchSpanProcessor | NOT STARTED | |
| 6 | Exporter | NOT STARTED | |
| 7 | TokenResolver | NOT STARTED | |
| 8 | Auth (OBO/S2S) | NOT STARTED | |
| 9a | Auto-instrumentation - Semantic Kernel | NOT STARTED | `SemanticKernelInstrumentor` |
| 9b | Auto-instrumentation - OpenAI | NOT STARTED | `OpenAIAgentsTraceInstrumentor` |
| 9c | Auto-instrumentation - LangChain | NOT STARTED | `CustomLangChainInstrumentor` |
| 10 | Resource Attributes | NOT STARTED | |
| 11 | Configuration Options | NOT STARTED | |
| 12 | Edge Cases | NOT STARTED | |
| 13 | Store Publishing Validation | NOT STARTED | |

## Issues Found

None yet.

## Captured Output Files

| Config | File |
|--------|------|
