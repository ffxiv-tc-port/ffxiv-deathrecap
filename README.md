# Death Recap

[Kouzukii/ffxiv-deathrecap](https://github.com/Kouzukii/ffxiv-deathrecap) 的台服移植版。
快速回顧自己或隊友的死因：顯示死亡前的受到傷害、治療、buff／debuff 與護盾紀錄。

## 功能

- 死亡回顧視窗：時間軸呈現死亡前的傷害、治療、增益／減益、護盾事件
- 可分別設定「自己」「小隊」「其他玩家」是否擷取、以彈出視窗或聊天訊息通知、僅限副本內、PvP 中停用
- 死亡時可自動彈出視窗，並可設定自動關閉秒數
- 傷害／治療對戰長條圖（Combat Histogram）
- 事件篩選器，可調整要記錄的事件種類

## 使用

- `/deathrecap` 或 `/dr` 開啟視窗

## 台服版的修改

- 死亡時自動彈出的視窗可在指定秒數後自動關閉，滑鼠點過就不關
- 遊戲改版後某條特徵碼對不上時，插件照常載入、只少對應的那一類事件，並在死亡回顧視窗寫明少了什麼
- 多處降低記憶體用量
- 介面繁體中文化

## 安裝

在 Dalamud 設定的「自訂插件庫」加入
`https://raw.githubusercontent.com/ffxiv-tc-port/DalamudPluginsTC/main/repo.json` 並啟用，
再從插件列表安裝。

原作者：[Kouzukii](https://github.com/Kouzukii)
