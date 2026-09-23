# Endcord macOS

macOS 圖形安裝程式。Apple 晶片與 Intel 的應用程式都在這個倉庫裡。

## 開啟

1. 把這個資料夾複製到 Mac。
2. 將 Endcord 建置產生的 `dist` 資料夾放在 `.app` 旁邊。`dist` 裡需要有 `patcher.js`、`preload.js`、`renderer.js` 與 `renderer.css`。
3. 雙擊 `開啟安裝程式.command`。

腳本會依這台 Mac 的處理器選擇 `EndcordInstaller-arm64.app` 或 `EndcordInstaller-x64.app`，解除下載隔離、做臨時簽章，然後打開安裝視窗。

## 從原始碼建置

需要 .NET 8 SDK。

```bash
dotnet publish installer-mac/EndcordInstaller.Mac.csproj -c Release -r osx-arm64 --self-contained true
```

Intel Mac 把 `osx-arm64` 換成 `osx-x64`。
