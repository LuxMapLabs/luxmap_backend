# LIGHT-CTRL — Phase 1: kênh MQTT cho thiết bị IoT · 08/10/2026

Mỹ chốt **IoT dùng MQTT** (08/10) và uỷ quyền: *"hãy cùng codex làm và đưa ra quyết định tốt nhất cho đồ án, Đạt sẽ theo"*.
Phase 1 = đặc tả + quyết định, **chưa code**. Nền: LC-1 (dịch vụ lệnh độc lập kênh), LC-2 (bí mật thiết bị), LC-10/LC-11 (đã merge).

## 1. Không đổi

- `LightingCommandService` giữ trọn vòng đời: tạo lệnh, `seq`, khoá thiết bị, hết hạn, thay lệnh, ACK, audit. Adapter MQTT **gọi đúng
  các hàm đó** — không có luồng lệnh thứ hai (LC-1).
- API cho người dùng (`/lighting/preview`, `/lighting/commands`, `/assets/iot-nodes`) và **WP5 không đổi**.
- Hợp đồng nghiệp vụ với firmware: `command_id` khử trùng lặp, `seq` lưu NVS và bỏ lệnh cũ, bỏ lệnh quá hạn, ACK **sau khi** thực thi
  với `{seq, result, reported_mode, error?}`. QoS của MQTT **không** thay ACK nghiệp vụ.
- Chế độ rơ-le chỉ lấy theo báo cáo thiết bị, chỉ khi `seq` mới hơn và chưa có lệnh mới hơn được giao (D-10).

## 2. Đề xuất nháp (TRƯỚC phản biện — bản chốt ở mục 4)

| Mã | Câu hỏi | Đề xuất nháp |
|---|---|---|
| M-1 | Broker | **Mosquitto 2.x** trong `docker-compose`, ảnh ghim digest manifest list (khuôn MinIO). Nhẹ, chuẩn, thư viện ESP32 dùng tốt |
| M-2 | Xác thực thiết bị trên broker | Plugin **dynamic security** của Mosquitto, backend là client quản trị: lúc cấp / xoay bí mật (LC-2) backend tạo / đổi mật khẩu client `node_id`; xoá thiết bị → xoá client. DB vẫn chỉ lưu SHA-256; broker lưu băm riêng của nó |
| M-3 | ACL | Mỗi thiết bị một role: **chỉ** subscribe `luxmap/v1/nodes/{node_id}/relays/+/command`, **chỉ** publish `luxmap/v1/nodes/{node_id}/{ack,status,telemetry}`. Backend: client riêng, toàn quyền `luxmap/v1/#`. Test tự động bằng broker thật |
| M-4 | Topic lệnh | **Retained, QoS 1, mỗi rơ-le một topic**: `…/relays/{relay_no}/command`. Retained = "lệnh mới nhất mỗi rơ-le, giao lại tới khi đóng" — đúng ngữ nghĩa poll hiện tại; thiết bị kết nối lại tự nhận. Lệnh đóng (applied / failed / expired / superseded) → backend xoá retained (payload rỗng) |
| M-5 | "delivered" nghĩa là gì | Broker đã nhận bản publish (PUBACK QoS 1) — **không** phải thiết bị đã nhận. Ghi rõ trong drift; thiết bị không phải gửi thêm "đã nhận" |
| M-6 | Đẩy lệnh ra broker | **Outbox**: worker nền mỗi ~1 s lấy lệnh `pending`, publish, rồi đánh dấu `delivered` dưới khoá thiết bị; cùng worker lưu hết hạn + xoá retained. Process chết giữa commit và publish thì lượt sau publish lại — không mất lệnh |
| M-7 | ACK | Thiết bị publish `…/{node_id}/ack` (QoS 1). `node_id` lấy từ **topic** — ACL bảo đảm thiết bị chỉ publish được topic của chính nó — rồi gọi `AckAsync`. Không có topic trả lời; lỗi chỉ ghi log + audit |
| M-8 | Thiết bị còn sống | LWT `…/{node_id}/status` = `offline` (retained); lúc kết nối publish `online`. Mỗi ACK / status cập nhật `last_report_at` (chỉ tiến) |
| M-9 | Đồng hồ thiết bị | Thiết bị **phải** đồng bộ giờ qua SNTP để so `expires_at` — retained có thể đến muộn nên `server_time` trong payload không tin được. Backend xoá retained khi hết hạn nên lệnh cũ không nằm lâu |
| M-10 | Kênh HTTPS poll đã merge | **Một kênh tại một thời điểm**: `Lighting:Channel = mqtt \| http`. Endpoint `/device/commands` trả 404 khi chọn `mqtt`. Giữ code để test và làm dự phòng |
| M-11 | TLS | Cổng 8883 TLS khi deploy; 1883 không TLS **chỉ** bind `127.0.0.1` cho dev. ESP32 nhúng CA |
| M-12 | Telemetry (IOT-09) | Cùng broker, topic dành sẵn `…/{node_id}/telemetry`; ingest là ticket của Đạt |
| M-13 | Thư viện .NET | **MQTTnet** (MIT) — package mới, phải xin như thay đổi dependency |
| M-14 | Phạm vi xã trong worker | Context riêng với phạm vi tường minh = xã của thiết bị (khuôn `SurveyProcessor.JobScope`); guard `SaveChanges` + audit vẫn chạy |

## 3. KHÔNG làm

- Không điều khiển gì ngoài thiết bị testbed `supports_remote_control = true` (D-R7). Không ingest telemetry (IOT-09).
- Không cho thiết bị publish vào topic của thiết bị khác; không dùng một tài khoản broker chung cho mọi thiết bị.

## 4. ✅ CHỐT 08/10/2026 — Claude + Codex, theo uỷ quyền của Mỹ · SELF-SIGNED

Codex (80k token) đồng ý M-10…M-14, đề nghị đổi M-1…M-9. Claude nhận 9 đề nghị đổi, chỉnh thêm 3 điểm (ACL bằng file placeholder,
scheme `Broker` thay vì ẩn danh, kick khi thu hồi là best-effort). Lý do chính của mỗi thay đổi ghi ở cột cuối.

| Mã | Quyết định | Vì sao (khác nháp) |
|---|---|---|
| **M-1** | **EMQX, một node**, `docker-compose`, ghim digest manifest list. Kiểm điều khoản giấy phép của bản ghim ở Phase 2 (BSL cho bản mới, một node dùng miễn phí) | Có sẵn xác thực HTTP + ACL placeholder ⇒ backend không phải đồng bộ tài khoản |
| **M-2** | **Xác thực = callback HTTP về backend**, so bằng đúng `DeviceSecret.Matches` trên `iot_node.credential_hash`; `username = client_id = node_id`, `password = secret`. DB là **nguồn duy nhất**. Endpoint callback dùng scheme riêng **`Broker`** (khoá dùng chung trong env, header), **không** `[AllowAnonymous]`, chỉ mở trong mạng nội bộ; tắt cache xác thực của EMQX; backend lỗi ⇒ từ chối. Không log mật khẩu | Dynamic security giữ kho băm thứ hai không dựng lại được từ SHA-256 — mất volume broker là phải cấp lại mọi bí mật |
| **M-3** | **ACL file của EMQX, mặc định deny, placeholder `${username}`**: thiết bị chỉ subscribe `luxmap/v1/nodes/${username}/relays/+/command` và `…/reply`; chỉ publish `…/${username}/{receipt,ack,heartbeat,status,telemetry}`; cấm retained trừ `status`. Backend: một client nghiệp vụ (publish command / reply, subscribe phần còn lại), tài khoản quản trị broker tách riêng | Không cần HTTP authz cho mỗi publish; quy tắc nhìn thấy được trong repo |
| **M-4** | **Lệnh không retained, QoS 1, clean session.** Worker đọc DB, **gửi lại** lệnh mở mới nhất mỗi rơ-le còn hạn mỗi 2 s tới khi đóng | MQTT không xoá retained có điều kiện — lệnh xoá của lệnh cũ có thể xoá nhầm lệnh mới; DB đã là hàng đợi đúng |
| **M-5** | **`delivered` = thiết bị gửi `receipt`** `{command_id, seq}` sau khi nhận và kiểm hạn. ACK hợp lệ đến trước receipt cũng tính là đã nhận (chuyển `pending → applied/failed` nguyên tử) | PUBACK chỉ chứng minh broker nhận; UI "thiết bị đã nhận" phải là thật |
| **M-6** | **Worker nền là adapter**: nguồn là `lighting_command` (không thêm bảng outbox); quét `pending` + `delivered`, lưu hết hạn qua service; chọn lệnh dưới `LockDeviceAsync`, **không** giữ khoá DB lúc chờ broker; publish ngay sau commit chỉ là tối ưu | Process chết giữa commit và publish thì lượt quét sau gửi lại |
| **M-7** | **ACK QoS 1 → backend `AckAsync` → commit → `reply`** `{command_id, status, code?, mode_recorded?}` trên `…/reply`. Firmware lưu kết quả vào NVS và **gửi lại ACK mỗi 5 s tới khi có reply**. `node_id` lấy từ topic (ACL bảo đảm) | PUBACK không chứng minh backend đã lưu |
| **M-8** | **Heartbeat 30 s** `…/heartbeat` cập nhật `last_report_at` (cùng receipt / ACK hợp lệ). LWT `…/status = offline` (retained) chỉ để quan sát, **không** đụng `last_report_at`; `node_status` vẫn tính bằng `IotOptions` | Một nguồn trạng thái sống |
| **M-9** | **Giờ là điều kiện vận hành**: SNTP có timeout / retry, NTP trong LAN cho demo; **chưa có giờ tin cậy thì không thực thi lệnh từ xa** (AUTO cục bộ vẫn chạy). Kiểm `expires_at` ngay trước khi đóng / ngắt rơ-le | Bản tin có thể đến trễ; `server_time` trong payload không chứng minh độ mới |
| **M-10** | **Một kênh tại một thời điểm**: `Lighting:Channel = mqtt \| http`; chế độ `mqtt` tắt hai endpoint `/device/commands`. Không tự fallback song song | Giữ HTTP để chẩn đoán / rollback |
| **M-11** | TLS **8883**, firmware kiểm CA + hostname; 1883 không TLS chỉ cho dev, bind `127.0.0.1`. Dashboard / API quản trị EMQX và callback chỉ mạng nội bộ | — |
| **M-12** | Topic `…/telemetry` dành sẵn; schema, QoS, store-and-forward thuộc **IOT-09** (Đạt) | — |
| **M-13** | **MQTTnet** (MIT), ghim phiên bản — dependency mới, Phase 2 nêu rõ | — |
| **M-14** | Context riêng, phạm vi = xã của thiết bị (khuôn `SurveyProcessor.JobScope`) + lọc `node_id`; guard ghi + audit vẫn chạy. Tra thiết bị không lọc **chỉ** để lập danh tính | — |
| **M-15** | Thu hồi / xoay bí mật hay xoá thiết bị ⇒ backend gọi API quản trị EMQX **ngắt phiên đang mở** — best-effort, có log; thất bại thì phiên cũ sống tới lần kết nối lại. **Giới hạn đã biết**, chấp nhận cho testbed | Codex muốn retry bền vững — quá tay cho pilot |

### Hợp đồng bản tin (MQTT 3.1.1, JSON snake_case, `schema_version: 1`, ≤ 4 KiB, thời gian UTC `Z`)

Gốc `luxmap/v1/nodes/{node_id}`. Mọi bản tin nghiệp vụ QoS 1, **không retained** (trừ `status`).

| Topic | Chiều | Payload |
|---|---|---|
| `…/relays/{relay_no}/command` | server → thiết bị | `{schema_version, command_id, seq, relay_no, mode: on\|off\|auto, expires_at}` — lệnh **SET**, không phải đảo trạng thái |
| `…/receipt` | thiết bị → server | `{schema_version, command_id, seq}` |
| `…/ack` | thiết bị → server | `{schema_version, command_id, seq, result: applied\|failed, reported_mode, error?}` |
| `…/reply` | server → thiết bị | `{schema_version, command_id, status, code?, mode_recorded?}` — `code` = `COMMAND_CLOSED`, `COMMAND_NOT_FOUND`, `VALIDATION_FAILED`… |
| `…/heartbeat` | thiết bị → server | `{schema_version, uptime_s, firmware_version}` mỗi 30 s |
| `…/status` | thiết bị (LWT) | `online` / `offline`, retained |
| `…/telemetry` | thiết bị → server | IOT-09 |

**Firmware:** khử trùng lặp theo `command_id` — lệnh trùng đã chạy thì **gửi lại ACK đã lưu**; bỏ lệnh `seq` ≤ `seq` đã chạy của rơ-le
(NVS); bỏ lệnh quá `expires_at` hoặc khi chưa có giờ; gửi `receipt` → thực thi → lưu kết quả NVS → gửi `ack` tới khi có `reply`.
Client ID cố định = `node_id`, kết nối lại có backoff + jitter, subscribe lại và chờ SUBACK.

### Kiểm chứng ở Phase 2

Broker thật trong test (Testcontainers / compose): ACL chéo thiết bị, client ID giả, wildcard, publish retained trái phép, thu hồi khi
đang kết nối; broker / backend khởi động lại; mất ACK; receipt và ACK đảo thứ tự; hết hạn và lệnh mới đua lệnh cũ.
