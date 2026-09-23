# Endcord

Endcord 是 Discord **桌面版**的修改程式。裝好之後，可以在 Discord 裡使用外掛和主題，並減少分析與當機回報。

請先從 [Discord 官網](https://discord.com/download) 安裝桌面版。穩定版、Canary、PTB 都可以。瀏覽器裡的 Discord 不能用這份安裝程式。

## macOS

1. 打開 [github.com/ddg1174/Endcord-macOS](https://github.com/ddg1174/Endcord-macOS)。
2. 按綠色的 **Code**，再按 **Download ZIP**。下載完成後解壓縮。
3. 進入解出來的 `publish` 資料夾。
4. 雙擊 `開啟安裝程式.command`。
   - Apple 晶片（M1、M2、M3、M4）會打開 `EndcordInstaller-arm64.app`。
   - Intel Mac 會打開 `EndcordInstaller-x64.app`。
   - 不用自己挑，腳本會看這台電腦決定。
5. 若 macOS 顯示無法打開、或提示無法驗證開發者：在 `開啟安裝程式.command` 上按右鍵，選「打開」，再按一次「打開」。也可以到「系統設定 → 隱私權與安全性」，按「仍要打開」。
6. 視窗打開後，勾選你有安裝的 Discord，按「安裝」。
7. 安裝完成後重新打開 Discord。進入使用者設定，就可以看到 Endcord。

同一個視窗裡也可以按「修復」或「移除」。

若移除之後 Discord 打不開，到 Discord 官網重新安裝一次 Discord。

## Windows

在解壓縮後的資料夾裡雙擊 `EndcordInstaller.exe`。選好 Discord，按安裝，然後重新打開 Discord。

## 自己修改程式

需要 [Node.js 22](https://nodejs.org/) 和 pnpm。

```bash
pnpm install
pnpm build
```

建置結果會出現在 `dist`。macOS 安裝程式讀的是 `publish/dist` 裡的檔案，所以改完後要把新的 `patcher.js`、`preload.js`、`renderer.js`、`renderer.css`（含同名的 `.map`）複製到 `publish/dist`。

若要重新做出 macOS 安裝程式，還需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)。

```bash
dotnet publish installer-mac/EndcordInstaller.Mac.csproj -c Release -r osx-arm64 --self-contained true
```

Intel Mac 把 `osx-arm64` 改成 `osx-x64`。

## 授權

本程式以 GPL-3.0 授權。完整條文見 [LICENSE](LICENSE)。
