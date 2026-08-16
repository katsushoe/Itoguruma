# ADR 0002: main起点の手動Releaseワークフロー

## Status

Accepted

## Context

タグpushをReleaseの起点にすると、`develop`など`main`に含まれないコミットへタグを付けた場合にもワークフローが起動します。従来はワークフロー内で拒否していましたが、失敗通知が発生し、タグの付け直しが必要でした。

## Decision

GitHub Actionsの`Release`を`workflow_dispatch`で手動実行し、入力されたSemantic Versionを検証します。ワークフローは常に`origin/main`と一致するコミットをチェックアウトし、ビルドとテストの成功後に、そのコミットへReleaseタグを作成してGitHub Releaseを公開します。

同じタグで再実行する場合は、タグが同じ`main`コミットを指す場合だけ配布物を更新します。別コミットを指す既存タグは自動変更せず、エラーにします。

## Alternatives

- タグpush起点を維持する案: 誤ったタグでも失敗実行と通知が発生するため採用しません。
- ローカルスクリプトだけでタグ作成を制御する案: GitHub上で制約を一元化できず、手順を迂回できるため採用しません。

## Impact

- Release担当者はGitHub Actionsからバージョンを入力して実行します。
- タグとReleaseはテスト成功後に作成されます。
- `main`以外へ付けたタグではReleaseワークフローが起動しません。

## Security

タグ作成とRelease公開には、ワークフローへ付与された`contents: write`の`GITHUB_TOKEN`だけを使用します。追加の秘密値は使用しません。

## Operations

Release実行前に対象変更を`main`へマージします。実行失敗時は原因を修正し、同じバージョンで再実行できます。既存タグが異なるコミットを指す場合は自動で上書きしません。

## Implementation and verification

- `.github/workflows/release.yml`を手動実行方式へ変更します。
- `README.md`へRelease手順を記載します。
- YAML構文、バージョン検証、既存タグの一致確認、ビルドとテストを検証対象とします。
