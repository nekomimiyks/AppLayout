# UI 案向け API

`variants\<名前>\` に `public static class UiFactory` と `public static Form CreateMainForm(AppController c)` を用意する。`Shell.Run` が画面とコントローラーの開始・終了、トレイ、二重起動を管理する。UI 側では `Start` / `Stop` を呼ばない。

- 初期表示と `RowsChanged` で `GetRows()` を呼び一覧を描画する。イベントは UI スレッドで発火する。更新前に選択行の `Key` を保持し、更新後に同じキーを再選択する。
- 一覧は保存順の保存済み行に続いて未保存ウィンドウを返す。`Saved`、`Window`、`StatusKind` を使って表示を分ける。保存済みでも未起動や複数該当の場合は `Window` が null になる。
- ボタンは選択中の `WindowRow` を `SaveCurrent(rows, owner)`、`Unsave(rows)`、`RestoreNow(rows)` に渡す。復元は空リストなら保存済みすべてが対象。操作後は仕様に従い保存済み行の選択を揃える。編集は `Edit(row, owner)` を呼ぶ。操作結果は `LastMessage` から表示する。
- チェックボックスは `Enabled`、`AutoStart`、`LaunchMissing`、`MinimizeToTrayOnClose` を読み書きする。初期値を設定してから変更イベントを接続する。各ラベルと説明はそれぞれ `Texts` の同名定数と `Tip` を付けた定数を使う。ボタンと列の文言も `Texts` にある。
- サイズ指定は `Ui.S` / `Ui.Sz` を使う。ビルドは `powershell -ExecutionPolicy Bypass -File .\build.ps1 -Variant <名前> -Out <exe名>`。
