# IoT và điều khiển đèn

Tra cứu tách từ `CLAUDE.md` (20/10/2026). Đọc trước khi thiết bị IoT, scheme `Device`, lệnh ON/OFF/AUTO, MQTT. Luật chung cho mọi ticket nằm ở `CLAUDE.md`.

**Phát hiện tự động từ IoT phải truy vết phiên bản firmware**, cùng yêu cầu model/artifact ở core (BE-34, ERD Q8).
Không thay yêu cầu này bằng một version ứng dụng chung.

## BE-14b — thiết bị IoT ở tủ điện (28/09/2026)

**Schema và quyền ghi tách nhau như `pole_current_status`.** `feeder_control` là bảng riêng khoá theo
`feeder_id`, KHÔNG phải cột trên `feeder`: chế độ ON/OFF/AUTO do thiết bị báo, còn `PUT
/assets/feeders/{id}` là thay thế toàn phần và sẽ xoá mất nó. Cả hai FK của `feeder_control` mang
`commune_id` (khuôn O-7), nên `ak_iot_node_node_id_commune_id` là đích FK — trông thừa, đừng xoá.

**`iot_node.data_source` siết hơn enum.** `ck_iot_node_data_source_not_field` chỉ nhận
`calibration_rig | simulated`: nhóm không lắp thiết bị ngoài thực địa (D-R10). Mở rộng CHECK này là
quyết định, không phải sửa lỗi.

**Danh sách quan hệ tính lúc đọc**, không lưu bản sao: `controller_node_ids[]`, `segment_ids[]`, `feeder_ids[]` (I-7b).

**`node_status` không lưu.** `NodeSilence.StatusAt` tính từ `last_report_at` và cấu hình
`iot_offline_after_minutes` (mặc định 60, Quản trị sửa qua `/admin/settings` — BE-33); đúng bằng ngưỡng vẫn `online`.
Ngưỡng đọc bằng `SystemSettingValues.ReadAsync`, **không cache**, một lần mỗi request / mỗi lượt job — đọc hai lần trong
một lượt là hai ngưỡng khác nhau khi Quản trị vừa sửa. Không đặt lại ngưỡng trong appsettings (BE-33 SET-2).

**Ngưỡng đổi được ⇒ một lần im lặng nhận diện bằng `last_report_at`, không bằng `detected_at`** (BE-33 SET-7).
`detected_at = last_report_at + ngưỡng` dời theo ngưỡng; kiểm trùng trên `detected_at` chính xác sẽ sinh `node_offline`
thứ hai cho cùng lần im lặng (`ScheduledJobTests.Changing_the_threshold_in_the_middle_of_a_silence_does_not_raise_it_twice`).

**Teardown của fixture phải xoá `feeder_control` rồi `iot_node` TRƯỚC `feeder` và xã** — cả hai
`Restrict` (cùng họ bẫy BE-14 số 3). `AssetSchemaFixture` và `AssetImportFixture` đã làm.

🔴 **`gen_consolidated_spec.py` có schema VIẾT TAY ĐÈ lên schema xuất từ code** (`S["PoleProperties"]`,
`S["SegmentProperties"]`…). Đổi một record `properties` trong code mà không sửa bản viết tay thì spec
hợp nhất vẫn ra hình dạng cũ, và CI không đỏ vì spec sinh lại vẫn khớp chính nó. Khi hiện thực một
stub `ni(...)`, xoá luôn stub **và** schema viết tay đi kèm (`S["IotNode*"]` đã xoá ở BE-14b).

**Mock `indent=1`.** Sửa mock bằng script phải round-trip từng byte trước khi đổi (đọc → ghi lại → so
bằng); đổi định dạng làm diff của WP5/WP6 thành vô nghĩa.

## LIGHT-CTRL 2a — scheme `Device` + đăng ký thiết bị (08/10/2026, drift LC-1…LC-10)

- 🔴 **Scheme `Device` KHÔNG phải mặc định; endpoint thiết bị gắn `[Authorize(Policy = DeviceAuth.Policy)]`, KHÔNG BAO GIỜ
  `[AllowAnonymous]`** (nó bỏ toàn bộ authorization). Policy `DeviceOnly` ghim `AddAuthenticationSchemes(Device)` — gỡ dòng đó thì
  Bearer của người dùng chạm được endpoint thiết bị. Coverage ngoại lệ máy: xem mục LC-12 và `CLAUDE.md`, Vai trò và phân quyền.
- **Principal thiết bị:** `commune_ids` = đúng một xã của thiết bị (không bao giờ `*`), `device_node_id`, **không** `role` ⇒
  `CommuneScopeAccessor` có sẵn cho ra phạm vi một xã, query filter + guard áp như mọi request; không capability nào nhận nó. Mọi truy
  vấn lệnh (2b) còn phải lọc `node_id = device_node_id` — cùng xã không có nghĩa là được đụng thiết bị khác.
- **Bí mật:** `DeviceSecret` (`LuxMap.Shared`): 32 byte → base64url; chỉ lưu SHA-256 hex (`iot_node.credential_hash`, CHECK cặp với
  `credential_set_at`); so `FixedTimeEquals`; mọi kiểu sai đều cùng một 401. **Không bao giờ log bí mật hay header `Authorization`**;
  bí mật chỉ xuất hiện đúng một lần trong response cấp / xoay.
- **Nối rơ-le khoá hàng `iot_node`** (`SELECT 1 … FOR UPDATE` rồi mới đọc qua filter); lệnh điều khiển (2b) phải lấy **cùng** khoá.
  Nối lại một rơ-le = xoá dòng `feeder_control` cũ + chèn dòng mới trong một `SaveChanges` (EF xoá trước, chèn sau — canh bằng test).
- **Header theo RFC 9110:** chữ `Device` không phân biệt hoa thường, sau nó 1+ dấu cách; credential không chứa khoảng trắng.
- Header lỗi theo `CLAUDE.md`, mục Bẫy chung. Trang 401 chỉ giữ lại challenge
  **trần** của scheme `Device` (`ApiConventionsSetup.DeviceChallenge`). **Đừng "sửa cho đều" thành giữ mọi challenge:** challenge
  của JwtBearer mang `error_description` (token hết hạn, sai khoá ký…) — đúng nguyên nhân mà `UNAUTHENTICATED` cố ý không tách.
- **Khoá hàng sau khi đã scope:** đọc thiết bị qua filter xã TRƯỚC, rồi mới `FOR UPDATE` theo `node_id` **và** `commune_id` — khoá
  trước thì Quản lý xã khác chờ / đo được khoá của thiết bị ngoài phạm vi. Hàng `feeder` không nằm dưới khoá đó, nên đua chuyển trụ
  được FK DB chặn và service **dịch lại thành 409** (`fk_feeder_control_feeder_same_cabinet`, `pk_feeder_control`), không để 500.
- **Controller test cần xác thực không được nằm trong `TestEndpointsController`** (`[AllowAnonymous]` cấp class thắng). Khuôn:
  `DeviceProbeController` + `DeviceProbeFactory` (host riêng, cùng DB) — không thêm ApplicationPart vào `AssetImportFixture` dùng chung.

## LIGHT-CTRL 2b — lệnh ON / OFF / AUTO (08/10/2026, drift LC-11)

- **Một dịch vụ, mọi kênh:** `LightingCommandService` giữ trọn vòng đời (bấm, giao, ACK, hết hạn, thay). `MqttLightingHandler` hiện
  **gọi đúng các hàm đó** — không viết luồng lệnh thứ hai, không để QoS của broker thay ACK nghiệp vụ.
- 🔴 **Mọi lượt ghi chạm lệnh khoá hàng `iot_node` trước** (đọc qua filter → `FOR UPDATE` theo `node_id` + `commune_id` → đọc
  lại). Lần bấm nhiều thiết bị khoá theo thứ tự `node_id` ordinal; nối rơ-le lấy cùng khoá. Sau khoá phải **lập kế hoạch lại** và so —
  lệch thì 409 `LIGHTING_TARGET_CHANGED`, không âm thầm gửi theo kế hoạch cũ. Phá thử: bỏ khoá → `Two_presses_at_once…` đỏ.
- **Audit đúng một event mỗi `SaveChanges`** ⇒ bước chạm nhiều lệnh (hết hạn, giao, thay do nối rơ-le) **lưu từng lệnh một** trong
  cùng transaction. Nhánh trả lời sớm của ACK vẫn phải **commit** — không thì các lệnh hết hạn vừa lưu bị rollback.
- **`seq` là cột identity**, không vẽ trước: DB cấp lúc INSERT, tăng dần qua mọi phiên; hai lần bấm trên một thiết bị đã tuần tự
  nhờ khoá. `command_id` thì **vẽ trước** (khuôn `NextFaultId`) vì audit của lần bấm phải liệt kê nó. Sắp lệnh theo `seq`,
  **không** theo `command_id` (luật độ rộng tối thiểu ở `CLAUDE.md`, mục Quy ước API).
- **`control_mode` chỉ đổi theo báo cáo của thiết bị, chỉ khi `seq` mới hơn `mode_seq` VÀ chưa có lệnh mới hơn nào được giao cho
  rơ-le đó** (D-10, I-6) — kể cả ACK trên lệnh đã đóng (`COMMAND_CLOSED`, vẫn lưu audit `reported`). `mode_seq` một mình **không đủ**:
  nó nằm trên dòng `feeder_control`, mà tháo rơ-le là xoá dòng ⇒ tháo + nối lại sẽ reset nó (Codex review 2b). Lịch sử lệnh thì còn.
- **Tắt `supports_remote_control` thay mọi lệnh đang mở** (registry, actor Quản lý) và **poll không bao giờ giao** cho thiết bị đã tắt
  cờ (actor `system` nếu cờ đổi đường khác) — D-R7.
- Test giữ khoá theo `CLAUDE.md`, mục Test; khuôn `LightingCommandTests.HoldingTheDeviceLockAsync`, poll đẩy `last_report_at` ra tương lai trước.
- Lọc lệnh theo principal thiết bị: xem LIGHT-CTRL 2a, mục Principal thiết bị.
- Endpoint thiết bị khai scheme `Device` trong `OpenApiSpecTests`; coverage máy theo mục LC-12 (`MachineEndpoints`). Thêm route phải sửa cả hai.
- Teardown: `lighting_command` → `lighting_request` **trước** `feeder_control`, `iot_node`, `feeder` và tài khoản (đều `Restrict`).
  Rollback `AddLightingCommands`: xem `docs/agents/traps/migrations.md`, mục Rollback dựng lại CHECK cũ.

## LC-12 — kênh MQTT cho thiết bị (08/10/2026)

- **Adapter MQTT chỉ chuyển byte.** Mọi quyết định nằm ở `MqttLightingHandler` (test không cần broker) và `LightingCommandService`.
  Thêm loại bản tin = thêm nhánh ở handler gọi hàm của service; đừng viết logic lệnh trong `MqttLightingChannel`.
- **Worker không có principal ⇒ `LightingScopeFactory` tạo context phạm vi = xã của thiết bị** (khuôn `JobScope`). Tra thiết bị
  **không lọc** chỉ để lập danh tính / phạm vi (`CommuneOfAsync`, `NodesWithOpenCommandsAsync`); mọi xử lý nghiệp vụ đi qua context
  có phạm vi.
- Validation cho kênh ngoài HTTP theo `CLAUDE.md`, mục Bẫy chung; ví dụ `AckAsync`: `seq`, `result` bắt buộc, `error` ≤ 500 (MQTT không qua controller).
- 🔴 **Endpoint của MÁY (thiết bị, broker) là ngoại lệ có tên** ở `CapabilityPolicyCoverageTests.MachineEndpoints` (route → policy).
  Callback broker dùng scheme `Broker` (khoá `Mqtt:CallbackKey`, so băm thời gian cố định), **không** `[AllowAnonymous]`, ẩn khỏi
  OpenAPI (`[ApiExplorerSettings(IgnoreApi = true)]`).
- **ACL nằm trong câu trả lời của callback** (`BrokerAuthController.DeviceAcl`/`BackendAcl`), EMQX `sources = []`, `no_match = deny`,
  `deny_action = disconnect`: subscribe / publish trái phép làm **ngắt kết nối**, không chỉ trả mã. Test ACL hai lớp: literal
  (`BrokerAuthTests`) + broker thật (`MqttEndToEndTests`); phá thử nới ACL → cả hai đỏ.
- **MQTTnet 5 KHÔNG ném lỗi khi broker từ chối CONNECT** — đọc `MqttClientConnectResult.ResultCode`. Test "sai bí mật bị từ chối"
  viết bằng `Assert.Throws` sẽ xanh giả.
- **EMQX kiểm callback lúc khởi động; không thấy API thì thử lại sau 15 s**, trong lúc đó từ chối mọi login. Test end-to-end chờ bằng
  kết nối thử với bí mật đúng (`ReadyDeviceAsync`). Host test phải `ListenAnyIP` — container gọi về qua `host-gateway` trên Linux.
- **`docker-compose.yml`: biến của service trong profile vẫn bị nội suy khi chạy profile khác** ⇒ dùng `${VAR:-default}`, không
  `:?` — nếu không, `docker compose up` của người có `.env` cũ sẽ gãy. Thiếu `Mqtt__CallbackKey` thì broker từ chối mọi client.
