# AppLayout

Windowsのアプリの位置・サイズを登録し、保存した配置へ自動で戻す常駐アプリです。

- 行のON/OFFで配置を即時登録・解除
- モニタごとの一覧、配置の編集、全解除
- Windowsのライト／ダーク配色、文字サイズ、モニタごとのDPIに対応
- 専用スクロールバー、トレイメニュー、トレイ収納時のアニメーション
- 同じWindowsセッションでの多重起動を防止

## 起動

`AppLayout.exe` を実行します。旧ファイル名の `AppLayout_A.exe`、`AppLayout_B.exe`、`AppLayout_C.exe`、`AppLayout_basic.exe` も、同じ完成版です。

使い方の詳細は [使い方.txt](使い方.txt)、動作の仕様は [仕様書](docs/仕様書.md) を参照してください。

配置は実行ファイルと同じフォルダの `layout.json` に保存します。書き込めない場合は `%APPDATA%\AppLayout` を使用します。個人用の配置設定とテスト生成物は、現在のソースには含めません。

## ビルド

Windows標準の.NET Framework 4.xコンパイラでビルドします。C# 5、WinFormsを使用し、NuGetは不要です。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

既定の画面は `variants/Ledger`、共通処理は `src/core` にあります。旧画面案のソースは `variants` に保存しています。

## 検証

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\_dev\verification\run.ps1
```

専用の設定ファイルとテストウィンドウを使い、個人の配置設定を変更せずに確認します。
