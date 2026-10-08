# LIGHT-CTRL — Phase 1: điều khiển ON / OFF / AUTO trên testbed + đăng ký thiết bị và rơ-le · 08/10/2026

**Phase 1 = khảo sát + đặc tả. Chưa sửa code, chưa migration.** Mỹ chốt D-item ở cuối rồi mới sang Phase 2.
Chạm schema + API + một kênh xác thực mới (thiết bị) ⇒ SELF-SIGNED, ghi drift khi chốt, nền tạm tới FW.
Bản này đã áp review Codex (gpt-6.1-sol, 83k token): 5 P1 + 4 P2 + 1 P3 — `.ai/reviews/LIGHT-CTRL-by-codex.md`.

## 1. Vì sao

- Phiếu v1.4 cam kết **không điều kiện**: *"Control supported streetlights (ON, OFF, AUTO)"* (`:101`), *"implement Remote Lighting
  Control (Force ON / Force OFF / AUTO)"* (`:264`), giao diện điều khiển (`:265`).
- Model đã sẵn: trụ → thiết bị (`iot_node`, ≤ 1 / trụ) → rơ-le (`feeder_control`) → mạch → cột (CABINET, TOPO-INFER). Nhưng
  **thiết bị và rơ-le hiện chỉ tạo được bằng SQL** (`seed_mock_set.py`) — nhóm IoT không tự dựng testbed được.
- **FE đã có nút ON / OFF mô phỏng** (`luxmap-web` `origin/dev` `GisDrawerPanel.tsx:740`, `useElectricalCascade.ts:7,25,42`): gọi theo
  `cabinet_id`, đổi ngay state React (`active ↔ fault`, `220 ↔ 0 V`), **không gọi API**. Chưa có AUTO, chưa có quản lý rơ-le / bí mật.
  Khi tích hợp: thay handler mock bằng preview → gửi → theo dõi; chỉ báo "đã thực thi" sau ACK; **không** dùng `fault` để biểu diễn tắt cưỡng chế.

## 2. Đã chốt từ trước — ticket này KHÔNG mở lại

| Nguồn | Nội dung |
|---|---|
| **D-R7** (`contract-drift.md:99`) | Chỉ **Quản lý** (`ControlLighting`, đã khai sẵn — Contract §2 `:239`); chỉ thiết bị `supports_remote_control = true` (testbed); thiết bị ngoài thực địa luôn `false`; **mọi lệnh ghi audit**; tài liệu viết *"supported lighting devices (testbed demo)"* |
| **I-6** | Chế độ hiện tại (`on \| off \| auto`) **do thiết bị báo**, nằm trên `feeder_control.control_mode` + `mode_reported_at` (đã có, CHECK cặp). Lệnh ở bảng riêng + audit. Không đặt `control_mode` chỉ vì đã gửi lệnh |
| **I-12 / I-13** | Rơ-le = một mạch. Testbed: 1 thiết bị, 2 mạch (đèn lẻ / chẵn) trên **cùng** một tuyến. Chọn **theo tuyến** (mọi rơ-le cấp cho tuyến) **hoặc theo mạch** (một rơ-le) |
| **I-14** | Lệnh ghi **theo rơ-le**. Mạch cấp cho **nhiều tuyến** → **cảnh báo** (liệt kê tuyến bị kéo theo) **rồi vẫn gửi**. Cột không thuộc rơ-le nào → "N cột không điều khiển được" |
| **I-17** | Thiết bị chỉ biết `relay_no`; mọi thứ gửi lên mang `node_id` + `relay_no`, backend tra ra `feeder_id` |
| **OPS-SCHEMA D-S10** | Một dòng lệnh **mỗi rơ-le** + nhóm cho một lần bấm; tách mode yêu cầu với mode báo về; dedup / expiry / ACK rõ; **không retry lệnh hết hạn** |

**Chưa chốt — chờ nhóm IoT (Đạt):** kênh truyền + ACK (`erd.md:43`, OPS-SCHEMA `:775`). Đặc tả ĐỀ XUẤT một kênh (D-1) để Đạt
xác nhận hoặc thay; phần còn lại không phụ thuộc kênh.

## 3. Đề xuất

### 3.1 Kênh thiết bị (D-1)

**Chốt 08/10: dịch vụ lệnh KHÔNG phụ thuộc kênh truyền** (`LightingCommandService`: tạo, giao, ACK, hết hạn, thay — toàn bộ vòng đời,
quyền, audit); **adapter đầu tiên là poll HTTPS**. Nếu Đạt đã dùng MQTT, chỉ thêm adapter MQTT gọi đúng các hàm đó — không có luồng lệnh
thứ hai. QoS của MQTT **không thay** ACK nghiệp vụ.

| | Poll HTTPS (đề xuất) | MQTT | WebSocket |
|---|---|---|---|
| Hạ tầng | Không thêm gì | Thêm broker + ACL theo thiết bị | Giữ kết nối, khó với ESP32 + 4G chập chờn |
| Độ trễ | = chu kỳ poll (testbed 2–5 s) | Tức thì | Tức thì |
| Mạng chập chờn (NFR `:181`) | Poll sau lấy lại lệnh còn hạn (3.4) | QoS 1, phải lo phiên | Tự reconnect |
| Phân quyền | Ở API, qua query filter như mọi thứ khác | ACL broker — lớp phân quyền thứ hai, ngoài `commune_id` | Như poll |

Vài giây trễ chấp nhận được cho demo; không thêm dịch vụ, không lớp phân quyền thứ hai. **Nếu Đạt đã chọn MQTT cho telemetry
(IOT-09)** thì nên dùng chung — D-1 chờ xác nhận.

### 3.2 Xác thực thiết bị (D-2) — sửa sau review (P1, P2)

- **Bí mật:** 32 byte ngẫu nhiên → **base64url** (43 ký tự) — khuôn `AccessTokenIssuer.cs:60`. Lưu `credential_hash` = **SHA-256 của
  chuỗi UTF-8 → hex**. Header `Authorization: Device <node_id>.<secret>`, dài tối đa 128; so **digest 32 byte** bằng
  `CryptographicOperations.FixedTimeEquals`. Sai định dạng / không khớp / thiết bị chưa cấp bí mật → 401 như nhau.
- **Pipeline:** scheme `Device` (**không** phải mặc định — Bearer vẫn là mặc định) + **policy `DeviceOnly`** = scheme `Device` +
  `RequireAuthenticatedUser`. Hai endpoint thiết bị gắn `[Authorize(Policy = DeviceOnly)]` — **KHÔNG `[AllowAnonymous]`** (nó bỏ toàn
  bộ authorization). Bearer gửi tới endpoint thiết bị → 401; header `Device` gửi tới endpoint người dùng → không ai đọc ⇒ 401.
- **Phạm vi xã:** handler tra `iot_node` theo `node_id` — **bỏ filter, chỉ cho lượt so bí mật** (lúc đó chưa có phạm vi) — rồi cấp
  principal: `commune_ids = [xã của thiết bị]` (không bao giờ `*`), `device_node_id`, **không** `role`.
  `CommuneScopeAccessor.FromPrincipal` (`CommuneScopeAccessor.cs:21`) có sẵn ⇒ `ForCommunes([xã])` ⇒ query filter +
  `CommuneWriteGuard` áp như mọi request (khuôn `SurveyProcessor.JobScope`). Không role ⇒ không capability nào nhận nó.
- **Mọi truy vấn lệnh còn lọc thêm `node_id = device_node_id`** — cùng xã không có nghĩa là được đọc / ACK lệnh của thiết bị khác.
- **Replay:** header là bí mật tĩnh, như bearer. Chấp nhận cho testbed: **HTTPS + ACK idempotent**; không chữ ký / nonce (D-9).

### 3.3 Lược đồ — sửa sau review (P1 FK, P1 dedup, P3)

```
iot_node
+ credential_hash   text NULL        -- NULL = chưa cấp bí mật
+ credential_set_at timestamptz NULL -- CHECK: cùng null

feeder_control
+ mode_seq          bigint NULL      -- seq của lệnh đã đặt control_mode lần cuối (D-10); NULL khi chưa ACK lần nào

lighting_request                       ← MỚI — một lần bấm của Quản lý (nhóm)
  request_id        uuid PK
  client_op_id      uuid NOT NULL UNIQUE, request_hash text NOT NULL   -- khuôn sync / BE-41
  commune_id        → administrative_unit, ICommuneScoped
  target_kind       feeder | segment;  target_id text
  requested_mode    on | off | auto
  requested_by → app_user (Restrict), requested_at
  affected_segment_ids text[] NOT NULL  -- ảnh chụp lúc gửi (I-14)
  excluded jsonb NOT NULL               -- rơ-le bị loại + lý do (D-8)
  uncontrollable_pole_count int NOT NULL

lighting_command                       ← MỚI — một dòng mỗi rơ-le
  command_id        PK, prefix CMD độ rộng 6 (D-3)
  request_id        → lighting_request (Restrict)
  node_id           FK ghép (node_id, commune_id) → iot_node      -- DANH TÍNH, không FK tới feeder_control
  feeder_id         FK ghép (feeder_id, commune_id) → feeder
  relay_no, cabinet_id   -- ẢNH CHỤP lúc tạo; rơ-le nối lại sau đó không làm hỏng dòng lịch sử
  data_source       ảnh chụp từ thiết bị (calibration_rig | simulated) — OPS S18
  commune_id, requested_mode
  seq               bigint NOT NULL UNIQUE  -- tăng dần, server cấp (D-10)
  status            pending | delivered | applied | failed | expired | superseded
  expires_at, delivered_at, completed_at
  reported_mode     NULL | on | off | auto ;  error text NULL (≤ 500)
  CHECK thứ tự thời điểm; CHECK status ↔ cột (applied ⇒ completed_at + reported_mode = requested_mode; failed ⇒ completed_at + error)
```

**Vì sao không FK tới `feeder_control`** (Codex P1): `feeder_control` là ánh xạ **hiện tại** (PK `feeder_id`, unique
`(node_id, relay_no)` — `IotConfigurations.cs:62,86`). FK tới nó sẽ làm cả lệnh đã xong chặn việc tháo / nối lại rơ-le. Lệnh giữ
**ảnh chụp**; ánh xạ được **kiểm lại** lúc tạo, lúc giao và lúc ACK.

### 3.4 Vòng đời + đồng thời (D-4) — sửa sau review (P1 race, P2 mất response, P2 ACK)

```
pending ──(poll)──► delivered ──(ACK applied)──► applied   → feeder_control.control_mode = reported_mode
                       │  └──(ACK failed)──► failed
   ├── quá expires_at (chưa ACK) ──► expired           ← không giao, không retry
   └── lệnh mới cùng rơ-le / rơ-le bị tháo hay nối lại ──► superseded   (cả pending LẪN delivered)
```

- 🔴 **Khoá theo thiết bị.** Tạo lệnh, poll, ACK, hết hạn, tháo / nối rơ-le đều mở transaction và **khoá hàng `iot_node`**
  (`SELECT 1 … FOR UPDATE`, khuôn `FaultLocks`) **trước** khi đọc lại trạng thái. Một lần bấm chạm nhiều thiết bị → khoá theo
  thứ tự `node_id` (ordinal). Khoá chỉ tuần tự hoá các lượt ghi ở server; **thứ tự thực thi** do `seq` quyết (D-10, ngay dưới).
- **Poll trả lệnh `pending` VÀ `delivered` còn hạn** — mới nhất mỗi rơ-le, cùng `command_id` — để response bị rơi không làm mất lệnh.
  Firmware khử trùng lặp theo `command_id`. Chỉ lần giao **đầu** đổi trạng thái + ghi audit. Quá hạn thì không giao nữa.
- **ACK tự kiểm** dưới khoá: đúng thiết bị gọi; trạng thái `delivered`; `now ≤ expires_at`; ánh xạ `(node, relay) → feeder` vẫn như
  ảnh chụp. Hợp lệ → `applied` / `failed`. **ACK lặp cùng nội dung** → 200, không đổi gì. **Khác nội dung** → 409. ACK cho lệnh đã
  `expired` / `superseded` → 409 `COMMAND_CLOSED` — **nhưng** `reported_mode` trong ACK vẫn là sự thật của thiết bị: ghi vào
  `feeder_control` nếu ánh xạ còn đúng (I-6), không đổi trạng thái lệnh.
- **ACK body:** `{result: applied | failed, reported_mode, error?}`. `applied` ⇒ `reported_mode` bắt buộc và **bằng**
  `requested_mode`, nếu không → 400. `failed` ⇒ `error` bắt buộc; `reported_mode` tuỳ chọn.
- **Hết hạn khi ĐỌC không ghi:** `GET /lighting/commands` hiển thị `expired` cho dòng `pending` / `delivered` đã quá hạn mà không
  sửa DB; trạng thái được **lưu** ở lượt ghi kế tiếp có khoá (poll / ACK / lệnh mới / nối rơ-le).
- Hạn mặc định **60 s** (`Lighting:CommandTtl`).
- 🔴 **Thứ tự lệnh (D-10, chốt 08/10).** Thứ tự tới server ≠ thứ tự thực thi (gửi lại, mất response, thiết bị khởi động lại). Mỗi
  lệnh mang **`seq`** — số nguyên tăng dần do server cấp (`lighting_command_seq`). Thiết bị lưu **`seq` lớn nhất đã thực thi cho mỗi
  rơ-le vào flash (NVS)** và **bỏ** mọi lệnh có `seq` ≤ giá trị đó — kể cả sau khởi động lại. ACK mang `seq`. Server chỉ ghi
  `reported_mode` vào `feeder_control` khi `seq` **lớn hơn** `feeder_control.mode_seq` (cột mới) — ACK đến trễ của lệnh cũ không đè
  chế độ mới. ACK cho lệnh đã đóng (`expired` / `superseded`) → 409, **lưu vào lịch sử**, chỉ đổi chế độ nếu `seq` vẫn mới hơn.

### 3.5 Audit — sửa sau review (P1)

`AuditEvent.cs:25-27` hiện có actor `User | Cv | Iot`, entity `WorkOrder | Fault | SurveySweep`. Thêm: actor **`System`**
(hết hạn), entity **`LightingRequest`**, **`LightingCommand`**, action `Requested`, `Delivered`, `Applied`, `Failed`, `Expired`,
`Superseded` (+ CHECK sinh lại). Guard đòi **đúng một event mỗi `SaveChanges`** (`AuditWriteGuard.cs:16`), nên:

| Thao tác | Một `SaveChanges` | Event |
|---|---|---|
| Quản lý gửi | request + N lệnh + lệnh cũ bị `superseded` | **1** — entity `LightingRequest`, actor Quản lý, snapshot liệt kê lệnh tạo / thay |
| Poll giao K lệnh | **K lần** `SaveChanges` trong một transaction | 1 mỗi lệnh — `Delivered`, actor `Iot` (user / role null, snapshot có `node_id`) |
| ACK | 1 lệnh | 1 — `Applied` / `Failed`, actor `Iot` |
| Hết hạn lưu lúc ghi | từng lệnh | 1 mỗi lệnh — `Expired`, actor `System` |
| Tháo / nối rơ-le | ánh xạ + lệnh bị thay | 1 — `Superseded`, actor Quản lý |

### 3.6 API người dùng

| Endpoint | Capability | Ghi chú |
|---|---|---|
| `GET /api/v1/lighting/preview?feeder_id=` \| `?segment_id=` | `ControlLighting` | Không ghi. `targets[]` (node, relay, feeder), `excluded[]` (lý do: thiết bị không `supports_remote_control`, chưa cấp bí mật…), `affected_segment_ids`, `uncontrollable_pole_count` |
| `POST /api/v1/lighting/commands` | `ControlLighting` | `{feeder_id \| segment_id, mode, client_op_id}` → **202** `{request_id, commands[] (command_id, node, relay, feeder, status), excluded[], affected_segment_ids, uncontrollable_pole_count}`. Gửi phần điều khiển được, **liệt kê phần bị loại** (D-8); chỉ 409 `NO_CONTROLLABLE_RELAY` khi không còn rơ-le nào. Cùng `client_op_id` cùng nội dung → 200 cùng request; khác nội dung → 409 `DUPLICATE_OP` |
| `GET /api/v1/lighting/commands?request_id=&feeder_id=&status=` | `ReadNetwork` (D-6) | Phân trang, mới nhất trước; trạng thái hết hạn tính lúc đọc |

### 3.7 API thiết bị (policy `DeviceOnly`)

| Endpoint | Ghi chú |
|---|---|
| `GET /api/v1/device/commands` | Lệnh `pending` + `delivered` còn hạn của **chính thiết bị**, mới nhất mỗi rơ-le: `[{command_id, seq, relay_no, mode, expires_at}]`. Cập nhật `last_report_at` (D-5, chỉ tiến) |
| `POST /api/v1/device/commands/{command_id}/ack` | 3.4 |

### 3.8 Đăng ký thiết bị + rơ-le (phần BE-34, `ManageAssets` — D-7)

| Endpoint | Ghi chú |
|---|---|
| `GET/POST /api/v1/assets/iot-nodes`, `GET/PUT/DELETE …/{nodeId}` | `cabinet_id` bắt buộc (CAB-3), `data_source ∈ calibration_rig \| simulated` (CHECK sẵn), `supports_remote_control`. DELETE: FK quyết (rơ-le, lệnh `Restrict`) → 409 |
| `PUT /api/v1/assets/iot-nodes/{nodeId}/relays/{relayNo}` | `{feeder_id \| null}` — nối / tháo. Feeder phải cùng trụ (CAB-4) và chưa bị rơ-le khác giữ. Dưới khoá thiết bị; lệnh `pending` / `delivered` của rơ-le đó → `superseded` |
| `POST /api/v1/assets/iot-nodes/{nodeId}/credential` | Cấp / xoay bí mật — trả **một lần**; bí mật cũ hết hiệu lực ngay |

## 4. KHÔNG làm

- Không điều khiển gì ngoài thiết bị `supports_remote_control = true` (D-R7); không viết như thể chạm lưới chiếu sáng của xã.
- Không ingest telemetry (dòng điện, công suất) — **IOT-09 của Đạt**. Ticket này chỉ có lệnh + ACK.
- Không hẹn giờ, không tự động theo sự cố. Không đặt `control_mode` từ lệnh đã gửi (I-6). Không retry lệnh hết hạn.

## 5. Kéo theo (Phase 2)

Migration (2 bảng, 2 cột credential, prefix `CMD`, enum audit + CHECK); `PrefixedIds` + test; scheme + policy `DeviceOnly`;
`CapabilityPolicyCoverageTests` — ngoại lệ **có tên** cho đúng hai route thiết bị, khẳng định chỉ `Device`, có xác thực, không anonymous
(`CapabilityPolicyCoverageTests.cs:38,56`, `AnonymousEndpointTests.cs:69,84`); test bảo mật: thiếu / sai / đã xoay bí mật, Bearer
gọi endpoint thiết bị, thiết bị khác cùng xã / khác xã, header Device gọi endpoint người dùng; test đồng thời: hai poll, ACK vs hết hạn
vs thay, hai Quản lý cùng gửi, nối rơ-le khi lệnh đã giao; `RoleCapabilityMatrixTests` (chỉ Quản lý gửi); `copy_dev_to_supabase.py`
`PLAN`; teardown fixture; OpenAPI + generator; ERD (`erd.md:224`); `CLAUDE.md`; drift; báo WP5 (thay nút mock) và Đạt (kênh + ACK).

## 6. D-item — ✅ CHỐT 08/10/2026 (Claude + Codex, theo uỷ quyền của Mỹ · SELF-SIGNED)

Mỹ uỷ quyền: *"tham khảo cùng với codex và đưa ra quyết định"*. Codex (gpt-6.1-sol, 51k token) đồng ý D-2, D-3, D-5…D-9 (kèm điều
kiện), đề nghị đổi D-1, D-4, D-10 và tách D-7 — Claude **nhận cả bốn**. Cột "Đề xuất" dưới đây là bản **trước** khi chốt; cột
"Quyết định" là bản chốt.

| Mã | Câu hỏi | Đề xuất | Quyết định |
|---|---|---|---|
| **D-1** | Kênh truyền lệnh | **Poll HTTPS**, chu kỳ 2–5 s trên testbed. Hỏi Đạt: telemetry đã chọn MQTT thì dùng chung | **Dịch vụ lệnh độc lập kênh + adapter poll HTTPS trước** (3.1). Kênh cuối cùng chờ Đạt |
| **D-2** | Xác thực thiết bị | Bí mật riêng mỗi thiết bị, định dạng + so sánh như 3.2, policy `DeviceOnly`, không `[AllowAnonymous]` | Như đề xuất + TLS kiểm chứng chứng chỉ, **không ghi bí mật vào log** |
| **D-3** | ID lệnh | Prefix `CMD`, độ rộng 6. Nhóm lệnh dùng UUID (`request_id`), không ID hiển thị | Như đề xuất — `CMD`, **tối thiểu** 6 chữ số (luật 0.3) |
| **D-4** | Vòng đời + đồng thời | 3.4: khoá theo thiết bị, poll giao lại lệnh còn hạn, ACK tự kiểm hạn / ánh xạ, hạn 60 s, không retry | Như đề xuất + ACK cho lệnh đã đóng chỉ vào lịch sử; chế độ chỉ đổi theo `seq` (D-10) |
| **D-5** | Poll có tính là "thiết bị còn sống" | **Có** — cập nhật `last_report_at` (chỉ tiến), không đồng nghĩa có telemetry | Như đề xuất; `last_report_at` = kênh điều khiển còn sống, **không** phải telemetry còn mới (IOT-09 có độ mới riêng) |
| **D-6** | Ai xem trạng thái lệnh | `ReadNetwork` (cả bốn vai trò xem, chỉ Quản lý gửi) | Như đề xuất, vẫn scope xã, không bao giờ trả bí mật |
| **D-7** | Ai đăng ký thiết bị / nối rơ-le / cấp bí mật | **Quản lý** (`ManageAssets`); Quản trị hệ thống không ghi tài sản (D-R12) | Như đề xuất; **Phase 2 tách hai PR**: 2a đăng ký thiết bị + rơ-le + bí mật + scheme `Device`; 2b lệnh |
| **D-8** | Gửi theo tuyến khi một phần không điều khiển được | Gửi phần được, **`excluded[]`** kèm lý do; 409 chỉ khi không còn rơ-le nào | Như đề xuất; FE xác nhận qua `preview`, server **kiểm lại** quyền + ánh xạ lúc gửi |
| **D-9** | Chống replay ở tầng xác thực | **Không** cho pilot: HTTPS + ACK idempotent. Chữ ký / timestamp / nonce để sau nếu ra thực địa | Như đề xuất cho pilot: TLS + bí mật riêng + TTL + dedup + thứ tự D-10. Ghi rõ giới hạn khi bảo vệ |
| **D-10** | Số thứ tự báo cáo từ thiết bị | **Không** cho pilot: thứ tự = thứ tự tới server dưới khoá thiết bị | **Đổi:** `seq` do server cấp; thiết bị lưu `seq` đã thực thi mỗi rơ-le vào flash, bỏ lệnh cũ; server chỉ đổi chế độ khi `seq` mới hơn `mode_seq` (3.4) |

**Phải báo Đạt (firmware) — hợp đồng không phụ thuộc kênh:** lệnh `{command_id, seq, relay_no, mode, expires_at}`; **khử trùng lặp
theo `command_id`**; **bỏ lệnh quá `expires_at`**; **bỏ lệnh có `seq` ≤ `seq` đã thực thi** của rơ-le đó, giá trị này lưu flash qua
khởi động lại; ACK **sau khi** thực thi `{seq, result, reported_mode, error?}`; quy tắc AUTO (về lịch / cảm biến của thiết bị); trạng
thái lúc khởi động. Hỏi: telemetry đang gửi bằng HTTP hay MQTT.

