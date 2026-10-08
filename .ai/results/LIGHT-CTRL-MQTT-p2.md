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

- **1303/1303 xanh** có bật end-to-end với EMQX thật (352 + 45 + 46 + 860); không bật thì 2 test đó bỏ qua. `-warnaserror` 0 cảnh báo.
- End-to-end: lệnh → thiết bị nhận → `receipt` (⇒ `delivered`) → `ack` → `reply` `applied`, chế độ ghi đúng; sai bí mật / mượn
  client id không kết nối được; subscribe / publish sang topic thiết bị khác ⇒ bị ngắt, lệnh của thiết bị kia không đổi.
- Phá thử nới ACL thiết bị → `BrokerAuthTests` (literal) **và** `MqttEndToEndTests` (broker thật) cùng đỏ.
- OpenAPI không đổi (callback ẩn khỏi spec).

## Chưa làm / giới hạn

- TLS 8883 + chứng chỉ cho triển khai thật (M-11) — compose hiện chỉ có 1883 bind loopback cho dev.
- Ngắt phiên khi thu hồi là best-effort (M-15); dev không cấu hình `Mqtt:AdminUrl` nên không ngắt.
- Telemetry (IOT-09) chưa ingest; topic đã dành.
- Gửi Đạt hợp đồng bản tin (đã soạn ở hội thoại — Mỹ gửi).
