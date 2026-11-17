# 画面・共通処理の API

`src/ui/Ledger.cs` の `UiFactory.CreateMainForm(AppController c)` が配置台帳画面を生成する。`Shell.Run` が画面とコントローラーの開始・終了、トレイ、二重起動を管理する。UI 側では `Start` / `Stop` を呼ばない。

- 初期表示と `RowsChanged` で `GetRows()` を呼び一覧を描画する。イベントは UI スレッドで発火する。更新前に選択行の `Key` を保持し、更新後に同じキーを再選択する。
- 一覧は保存順の保存済み行に続いて未保存ウィンドウを返す。`Saved`、`Window`、`StatusKind` を使って表示を分ける。保存済みでも未起動や複数該当の場合は `Window` が null になる。
- `SetTarget(row, enabled)` は対象1行だけを即時登録／解除し、保存する。成功はtrue、ONにできない場合はfalseとLastMessage。I/O失敗時は例外を返し、メモリ上の登録は元に戻す。既存ONの再通知では座標を変えない。
- 表示文言は `L.T(日本語原文)`、値を挿入する文言は `L.F(原文, args)` を使う。`src/locales/*.json` のKey/Value配列をexeへ埋め込む。`L.Attach(form, refresh)` は固定ラベルを再翻訳し、入力欄は変更しない。状態の内部キーとタイトル条件は翻訳しない。
- `AppController.Language` は `auto` または対応言語コードを保存してから表示を切り替える。保存失敗時は元の設定と表示を維持する。翻訳の検証は `_dev/localization/verify.ps1`、言語切替の検証は `_dev/verification/run.ps1 -LocalizationOnly`。
- `SaveCurrent(rows, owner)` は選択中を登録／更新し、非選択の登録済み行を解除する一括API。現在の画面では `SetTarget` を使う。
- `UnsaveAll()` は全登録を解除する。UI側で事前に確認画面を出す。`Unsave(rows)` は指定した登録済み行を解除するAPI。
- `RestoreNow(new List<WindowRow>())` は登録済みすべてに配置を実行する。編集は `Edit(row, owner)`、結果は `LastMessage` から表示する。
- `ToggleSwitch` はON/OFF文字付きの設定スイッチ。表のセルは `SwitchRenderer.Draw` を使い、登録行背景は `AppTheme.ActiveRow` とする。
- `ThemedScrollBar` は独自描画のControl。Maximumは実効的な最大Value、SetRangeで範囲を設定する。GridScrollHostではDataGridView.ScrollBars=Noneを保ち、編集領域はThemedScrollPanel.Contentに配置する。ネイティブバーへの上描きは使わない。
- チェックボックスは `Enabled`、`AutoStart`、`LaunchMissing`、`MinimizeToTrayOnClose` を読み書きする。初期値を設定してから変更イベントを接続する。各ラベルと説明はそれぞれ `Texts` の同名定数と `Tip` を付けた定数を使う。ボタンと列の文言も `Texts` にある。
- サイズ指定は `Ui.S` / `Ui.Sz` を使う。新しい画面は `DpiAwareForm` を継承し、表の列幅・行高などは `OnUiScaleChanged` で再計算する。配色は `AppTheme` を使う。
- `AppTheme.Apply(form)` はテーマ変更を購読し、コントロール・表の配色・タイトルバーを更新する。画面破棄時に購読を解除する。描画色は動的getterを参照し、固定値のキャッシュを作らない。`RefreshFromWindows()` は最新設定の再読込、`Changed` は更新通知（別スレッドの場合あり）。
- `TrayMenu` は `ContextMenuStrip` 派生でWindowsシェル配色を使う。メニュー描画は `MenuSurface` / `MenuForeground` / `MenuHover` 等を使用し、アプリの配色とは分ける。
- 文字は `UiTypography.BodyFont(relativeSize, style, dpiScale)` / `MenuFont(dpiScale)` を使用する。返されるフォントはPixel単位でDPI反映済み。測定時や描画時に再度DPIを掛けない。`BodyPoints` はWindows標準の論理ポイント値。
- `AppTheme.Apply` は `UiTypography.Attach` も行い、フォント変更はControlと表スタイルの旧値を採取してから一括反映する。DataGridViewのFont伝播に文字倍率を重ねない。`OnTextMetricsChanged` では独自描画用フォントと実測寸法を更新する。
- ビルドは `powershell -ExecutionPolicy Bypass -File .\build.ps1`。`src/core` と `src/ui` から `AppLayout.exe` を生成する。出力先を変更する場合は `-Out <パス>` を指定する。
- 統合確認は `powershell -ExecutionPolicy Bypass -File .\_dev\verification\run.ps1`。専用設定とテストウィンドウを使い、常駐の統合検証も自動配置OFFで行う。Windows側の配色を変更せずに明暗の変更通知を模擬し、画面とメニューを確認する。
