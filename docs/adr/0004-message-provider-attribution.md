# ADR 0004: 送信元Providerを受付時に確定して保存する

## Status

Accepted

## Context

Hataoriはプロジェクト宛てメッセージの送信元Providerを使って、同じProvider上の対象プロジェクトを優先します。受信時にAgent登録を再照会すると、送信後の登録変更により判定結果が変わります。送信者にProvider入力を許すと、登録内容との不一致や改変が可能になります。

## Decision

- `agent_type`をAgent登録時のProviderとし、ASCII英数字とハイフンへ制限して小文字へ正規化します。
- `send_message`はトランザクション内で送信元Agentの`agent_type`を解決し、メッセージの`provider`へ保存します。
- `provider`は送信APIの入力に含めず、CLIの`send --provider`も拒否します。
- Providerが未登録、空、または互換用識別値`unknown`の場合、メッセージを保存せず`provider_not_registered`として失敗させます。
- Inbox、再配送、Hook、Thread履歴、Viewerは保存済み`provider`を返し、現在のAgent登録から再計算しません。
- DB schema version 4で`messages.provider`を追加します。既存メッセージは推測せず`unknown`とし、既存Agentの`agent_type`はtrim・小文字化します。

## Alternatives

- 送信者からProviderを受け取る案は、信頼境界を越えた改変を許すため不採用とします。
- 受信時にAgent登録を参照する案は、配送後に値が変化し得るため不採用とします。
- 既存メッセージを送信元Agentから補完する案は、送信時点の値を保証できないため不採用とします。

## Impact

メッセージDTO、SQLite schema、MCP/CLI出力、Hook、Viewer、テスト、利用者文書が対象です。新規送信には有効な`agent_type`が必要です。既存メッセージは`provider=unknown`として引き続き取得できます。

## Security conditions

Providerは登録済み送信元Agentからのみ解決し、送信要求による指定・上書きを受け付けません。認証情報やProvider以外のAgentメタデータはメッセージへ複製しません。

## Operational conditions

クライアントは送信前に、プロジェクトIDを`agent_id`、実行Providerを`agent_type`として登録または更新します。schema version 4への移行は起動時に単一トランザクションで実行します。

## Implementation and verification

Coreの送信・取得経路を共通実装とし、MCPとCLIは同じ`MessagingService`を使用します。正規化、永続化、lease再配送、履歴、既存DB移行、Provider欠落、不正上書き拒否を自動テストします。
