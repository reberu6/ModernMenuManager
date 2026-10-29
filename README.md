# ModernMenuManager

Windows 11 新式右鍵選單（按右鍵第一層看到的那個選單）的管理工具：列出所有項目，可以隱藏或恢復。

## 下載與使用

1. 到 [Releases](../../releases/latest) 下載 `ModernMenuManager.exe`，直接執行（不用安裝，Windows 11 內建的 .NET Framework 就能跑）。
2. 清單中 **打勾 = 顯示**，**取消打勾 = 隱藏**。
3. 按「套用變更」，再按「重新啟動檔案總管」。

如果某個項目隱藏後還在，對它按右鍵選「隱藏給所有使用者（需要管理員）」。

## 原理

- App 加到新式選單的項目，都寫在該 App 套件的 `AppxManifest.xml`（`windows.fileExplorerContextMenus`）裡，每個項目有一個 CLSID。本工具會讀取所有已安裝 App 的 manifest 把它們列出來，並向該項目詢問實際顯示的文字。
- 隱藏 = 把 CLSID 寫進 Windows 的封鎖清單
  `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked`
  （「所有使用者」則是 `HKEY_LOCAL_MACHINE` 下的同一個位置）。恢復 = 把它刪掉。
- 萬一工具打不開，也可以手動用登錄編輯程式刪掉上面那個機碼裡的值來全部恢復。
- 掃描記錄在 `%LOCALAPPDATA%\ModernMenuManager\log.txt`。

## 做不到的

- 剪下、複製、重新命名、刪除、內容等系統核心項目無法移除。
- 「顯示其他選項」裡的舊式項目請用 [ContextMenuManager](https://github.com/BluePointLilac/ContextMenuManager)。
- 新增自訂項目請用 Microsoft Store 上的 [Custom Context Menu](https://github.com/ikas-mc/ContextMenuForWindows11)。

## 授權

GPL-3.0。做法參考 ContextMenuManager（GPL-3.0）與 Custom Context Menu（LGPL-3.0）。
