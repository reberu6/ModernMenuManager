# ModernMenuManager 工作規範

適用於本 repo。使用者（FX）本次明確指示優先於本檔。用台灣繁體中文回報，先講結論，交代已驗證與未驗證項目，不要把預期行為寫成實測結果。

## 專案

- Win11 新式右鍵選單管理工具：.NET Framework 4.8 WinForms，單一 exe，免安裝。原始碼在 `src/ModernMenuManager/`。
- 預設分支是 `claude/project-thread-h5mi3z`（沒有 `main`）。
- FX 電腦沒有 Visual Studio，**建置只靠 GitHub Actions**（`.github/workflows/build.yml`）。產物：artifact `ModernMenuManager`（exe）。
- 執行記錄在 `%LOCALAPPDATA%\ModernMenuManager\log.txt`。
- **注意 CI**：在審查第 1 輪修好以前，推送到任何分支都會自動發一版公開 Release（`v0.1.<run_number>`）。只改文件的推送，commit 訊息要加 `[skip ci]`。

## 必守

- **推送前要先問 FX**（每次都問，FX 的「好」只涵蓋當次問的那一項）。不合併預設分支、不打 tag、不刪 Release，除非 FX 明確要求。
- 不向上游或其他 repo 開 PR。
- 只照審查計畫的步驟做，不順手改步驟沒提到的東西。

## 審查 → 執行流程

- 入口：`docs/審查/目前.md`（計畫，**只有審查者改**）＋ `docs/審查/結果.md`（**執行者只往下追加**）。FX 說「執行目前的審查」＝讀目前.md 照做，結果追加到結果.md。
- 審查者可以是 Claude 或另一個 GPT 對話，**審查和執行不要是同一個對話**。
- 分工：只改程式的步驟由**網頁 GPT** 做（讀 GitHub、推分支）；要在 FX 電腦上做的（下載 exe、試用）由 FX 或 Codex 做。
- 執行者規則：
  - 每次開始都重新讀 `AGENTS.md`、`目前.md`、`結果.md`（不要相信對話裡記得的內容）。計畫檔可能在 `claude/` 開頭的分支上，目前.md 開頭會寫。
  - `目前.md` 開頭有「輪次：第 N 輪」。若 `結果.md` 已有「第 N 輪」，回覆「沒有新的審查（第 N 輪已做完）」就停。
  - 不要修改 `目前.md`；結果只追加到 `結果.md`（標明輪次，30 行內）。
  - 每個程式 commit 都要 Actions 綠燈。**判斷條件不成立才停**：停下時寫做到第幾步、看到什麼、錯誤訊息原文。
  - 長的 log 不要整段貼進 md，寫 run id 或檔名即可。
- 收尾（審查者）：讀 `結果.md`，結論寫進 README 或程式註解，再把兩份檔案合併移到 `docs/審查/封存/<yyyyMMdd>-<主題>.md`（最多留 5 份）。不要在 `docs/` 另開新的回報／handoff 檔。
