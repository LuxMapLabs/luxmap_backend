# LIGHT-CTRL — Phase 2: kênh MQTT cho thiết bị · 08/10/2026

Nhánh `docs/LIGHT-CTRL-mqtt-p1` (Phase 1 + Phase 2 chung PR, theo lựa chọn của Mỹ). Quyết định M-1…M-15: `LIGHT-CTRL-MQTT-p1.md`,
drift LC-12. ⚠️ SELF-SIGNED.

## Đã làm

| Phần | File |
|---|---|
| Dịch vụ | `LightingCommandService`: tách lõi `DeliverableAsync` (poll HTTP đánh dấu đã giao, MQTT thì không); `DispatchAsync`, `ReceiptAsync`, `TouchAsync`; `AckAsync(deliverIfPending)`; kiểm `seq` / `result` / `error` chuyển vào service |
| Adapter | `Telemetry/Mqtt/`: `MqttLightingHandler` (logic, không phụ thuộc thư viện MQTT), `MqttLightingChannel` (MQTTnet 5.2, kết nối lại có backoff + jitter, subscribe trước khi gửi, gửi lại lệnh mở mỗi `ResendInterval`), `LightingScopeFactory`, `MqttMessages` (topic + payload `schema_version: 1`, ≤ 4 KiB) |
| Broker | `BrokerAuthController` (callback, ACL theo client), scheme `Broker` (`BrokerAuthenticationHandler`), `IMqttBrokerAdmin` (EMQX REST, best-effort) |
| Chọn kênh | `Lighting:Channel = http \| mqtt`; `mqtt` ⇒ `/device/commands` 404, `Mqtt:*` bắt buộc đủ lúc khởi động |
| Hạ tầng | `docker-compose.yml`: EMQX 5.10.5 (profile `mqtt`, digest manifest list, healthcheck); `.env.example`; CI bật EMQX + `LUXMAP_MQTT_E2E=1` |
| Test | `MqttChannelTests` (11), `BrokerAuthTests` (6), `MqttEndToEndTests` (2, broker thật); `LightingTestRig` tách từ `LightingCommandTests` |
| Kéo theo | `CapabilityPolicyCoverageTests.MachineEndpoints`; `docs/development.md`; `CLAUDE.md` |

## Bằng chứng

- **1309/1309 xanh** (sau sửa review) có bật end-to-end với EMQX thật (352 + 45 + 46 + 866); không bật thì 2 test đó bỏ qua. `-warnaserror` 0 cảnh báo.
- End-to-end: lệnh → thiết bị nhận → `receipt` (⇒ `delivered`) → `ack` → `reply` `applied`, chế độ ghi đúng; sai bí mật / mượn
  client id không kết nối được; subscribe / publish sang topic thiết bị khác ⇒ bị ngắt, lệnh của thiết bị kia không đổi.
- Phá thử nới ACL thiết bị → `BrokerAuthTests` (literal) **và** `MqttEndToEndTests` (broker thật) cùng đỏ.
- OpenAPI không đổi (callback ẩn khỏi spec).

## Codex review code — 0 P1 · 5 P2 · 1 P3, đối chiếu code, cả 6 đúng, sửa hết

| # | Sửa | Canh bằng (phá thử → đỏ) |
|---|---|---|
| P2 | ACK hợp lệ không cập nhật `last_report_at` (trái M-8) | `An_ack_marks_the_channel_alive` |
| P2 | Transport bỏ qua mã SUBACK, không timeout riêng | Subscribe bị từ chối ⇒ ngắt + thử lại, không gửi lệnh; mọi thao tác MQTT có timeout 10 s; CONNECT bị từ chối (MQTTnet trả mã, không ném) ⇒ thử lại |
| P2 | Cấu hình cho phép danh tính backend mà callback từ chối | Bỏ `BackendClientId` (client id = `BackendUsername`); kiểm như callback — `A_backend_identity_the_callback_would_refuse_stops_startup` |
| P2 | `reply` lặp `command_id` tuỳ ý ⇒ vượt giới hạn gói | `command_id` phải `CMD-` + 6–20 chữ số, khác thì bỏ — ca mới trong `Junk_is_dropped…` |
| P2 | `DispatchAsync` quét cả DB dùng chung trong test | Overload theo danh sách thiết bị; test chỉ quét thiết bị của mình. Host E2E vẫn quét cả DB test — **chấp nhận** (chỉ lưu hết hạn cho lệnh đã quá hạn) |
| P3 | Ca "mượn client id" chưa thật sự mượn | `client_id` khác `username` trong test E2E |



- TLS 8883 + chứng chỉ cho triển khai thật (M-11) — compose hiện chỉ có 1883 bind loopback cho dev.
- Ngắt phiên khi thu hồi là best-effort (M-15); dev không cấu hình `Mqtt:AdminUrl` nên không ngắt.
- Telemetry (IOT-09) chưa ingest; topic đã dành.
- Gửi Đạt hợp đồng bản tin (đã soạn ở hội thoại — Mỹ gửi).
