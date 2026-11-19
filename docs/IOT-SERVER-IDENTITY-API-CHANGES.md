# Gộp server_id và Gateway ID — thay đổi API IoT

Cập nhật 19/11/2026. Thay đổi BE, Bridge và firmware espserver; không thay đổi mã FE.
Đây là bề mặt API đề xuất SELF-SIGNED, theo IOT-SERVER-IDENTITY-1 trong contract-drift.md.
Ví dụ URL dưới đây dành cho bản mới sau khi deploy, không xác nhận VPS đã chạy bản này.

## 1. Một bộ ESP32 gateway dùng một mã

- server_id = iot-server-01: mã quản lý trong LuxMap, đồng thời là MQTT Client ID và username.
- device_id = LUXMAP-P001: mã phần cứng của ESP32 node, giữ nguyên trong firmware node.
- node_id = NODE-004: mã tài sản do BE sinh, dùng điều khiển và tra cứu.
- feeder_id = FDR-009: mạch điện được node giám sát.

Nhiều server_id có thể dùng chung một Bridge trên VPS. Mỗi server chỉ thấy trạng thái heartbeat,
key và tuyến của chính nó. Một gateway hỗ trợ tối đa 32 device_id. Không gửi gateway_id khi tạo node.
Địa chỉ Bridge nội bộ do BE cấu hình, không gửi base_url từ FE.

## 2. Những API được thêm hoặc sửa

| API | Thay đổi | Quyền |
|---|---|---|
| POST /api/v1/iot/servers | JSON giữ nguyên; server_id chuẩn hóa chữ thường, dùng luôn cho MQTT | ManageSettings (Admin) |
| GET /api/v1/iot/servers | Heartbeat theo đúng server_id, thêm credential_issued, không dựa vào gateway khác cùng Bridge | ReadNetwork, lọc xã |
| PUT /api/v1/iot/servers/{serverId}/location | Không đổi JSON; cập nhật xã và GPS của cùng mã | ManageSettings |
| POST /api/v1/iot/servers/{serverId}/credential | Mới, cấp key cho Client ID = username = server_id | ManageSettings |
| GET /api/v1/iot/servers/{serverId}/credential | Mới, xem lại key của đúng server | ManageSettings |
| DELETE /api/v1/iot/servers/{serverId}/credential | Mới, thu hồi key, xóa heartbeat và ngắt MQTT | ManageSettings |
| GET /api/v1/iot/servers/{serverId}/nodes | Mới, danh sách node đúng server, page/page_size | ReadNetwork, lọc xã |
| POST /api/v1/iot/servers/{serverId}/gateway-migration | Mới, chuyển có kiểm tra từ mã gateway cũ sang server_id | ManageSettings |
| POST /api/v1/iot/nodes | Bỏ gateway_id trong request/response; tự gán ACL theo server_id | ManageSettings |
| PUT /api/v1/iot/nodes/{nodeId} | Chuyển nguồn giữa server cùng Bridge và cùng xã: kiểm tra chủ hiện tại rồi chuyển ACL | ManageAssets (Manager), lọc xã |
| DELETE /api/v1/iot/nodes/{nodeId}/feeder | Mới: gỡ khỏi feeder, giữ node trong tủ và giữ lịch sử | ManageAssets, lọc xã |
| DELETE /api/v1/iot/nodes/{nodeId} | Gỡ quyền gửi MQTT của thiết bị khỏi gateway; giữ node và lịch sử | ManageAssets, lọc xã |

Các API command, command/history, electrical, lịch sử điện theo feeder và diagnostics không đổi URL/JSON.
Kênh điều khiển vẫn là /api/v1/iot/nodes/{nodeId}/command.

### Các URL cũ còn tương thích, đã đánh dấu deprecated trong OpenAPI

- GET /api/v1/iot/servers/{serverId}/gateways/routes: chỉ trả tuyến canonical của server này, không trả toàn Bridge.
- PUT /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/route.
- POST/GET/DELETE /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential.

Ở các URL có gatewayId, giá trị phải bằng server_id hoặc alias đã chuyển thành công bằng gateway-migration.
Một gatewayId tùy ý, kể cả mã của server khác cùng Bridge, trả 409 IOT_REGISTRATION_CONFLICT.
Alias cũ chỉ trỏ API đến mã mới, không cho phép MQTT đăng nhập bằng key/mã cũ.
Luồng thêm mới không cần gọi API gán tuyến riêng.

## 3. Thêm một bộ mới trên Postman

Mọi request BE dùng Authorization: Bearer <access_token>. POST/PUT dùng Content-Type: application/json.
Admin thực hiện ba bước đầu; Manager cùng xã đọc và điều khiển node.

### Bước 1 — đăng ký ESP32 gateway

POST https://luxmap.com.vn/api/v1/iot/servers

~~~json
{
  "server_id": "iot-server-01",
  "commune_id": "COM-070",
  "location": { "lat": 10.965681, "lng": 106.504581 }
}
~~~

201: {"server_id":"iot-server-01"}. Đăng ký server không tự tạo key và chưa có heartbeat.
Server phải có xã để gán node; tủ, feeder và server phải cùng xã.

### Bước 2 — cấp key và cấu hình gateway

POST https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/credential

Body none. POST và GET cùng URL trả 200, Cache-Control: no-store:

~~~json
{
  "server_id": "iot-server-01",
  "client_id": "iot-server-01",
  "username": "iot-server-01",
  "password": "<secret do Bridge cấp>",
  "node_ids": []
}
~~~

POST lại khi key còn tồn tại trả 409 IOT_REGISTRATION_CONFLICT.
GET khi chưa cấp/đã thu hồi trả 404 ASSET_NOT_FOUND.
Key cũ chỉ có hash không xem lại được: 409 IOT_CREDENTIAL_NOT_RETRIEVABLE.
DELETE cùng URL trả 204, gọi lại vẫn 204. Sau đó POST để cấp key mới và nhập lại gateway.
Giữ volume data và keyring của Bridge để GET xem lại key qua lần deploy.

Trang cấu hình ESP32 gateway chỉ cần Wi-Fi, broker mqtts://luxmap.com.vn:8883,
Server ID iot-server-01 và MQTT secret vừa nhận. Username tự lấy từ Server ID.
Không nhập Node ID; gateway tự lấy danh sách node qua MQTT sau đăng nhập và định kỳ mỗi 10 giây.
Key chưa cấp hoặc sai password thì MQTT không kết nối được, kể cả khi Wi-Fi đã có IP.

### Bước 3 — tạo node và gán feeder trong một request

POST https://luxmap.com.vn/api/v1/iot/nodes

~~~json
{
  "server_id": "iot-server-01",
  "cabinet_id": "CAB-011",
  "feeder_id": "FDR-009",
  "device_id": "LUXMAP-P001",
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500
}
~~~

201 khi tạo, 200 khi gửi lại cùng cấu hình. Ví dụ response (node_id thực tế do BE sinh):

~~~json
{"node_id":"NODE-004","server_id":"iot-server-01","device_id":"LUXMAP-P001","feeder_id":"FDR-009"}
~~~

BE thêm device_id vào quyền MQTT của đúng server_id, gateway tự nhận danh sách mới.
Request chỉ có 7 trường ở trên. Không gửi gateway_id, lamp_count_calibration, installed_lamp_count, data_source hoặc supports_remote_control. BE tự đặt data_source=calibration_rig và supports_remote_control=true cho bộ IoT testbed. Không thay đổi node đã có hoặc API /assets/iot-nodes.
Một device_id không được đồng thời thuộc hai nguồn trên cùng Bridge.

### Bước 4 — đọc trạng thái và danh sách node

GET https://luxmap.com.vn/api/v1/iot/servers

~~~json
[{"server_id":"iot-server-01","status":"online","commune_id":"COM-070","location":{"lat":10.965681,"lng":106.504581},"credential_issued":true}]
~~~

status: online (heartbeat trong 30 giây), offline (hết hạn/chưa heartbeat), unconfigured (chưa có tuyến),
broker_down (Bridge chạy nhưng broker dừng), unreachable (BE không đọc được Bridge).
credential_issued phản ánh lần đọc Bridge thành công; khi unreachable cần kiểm tra kết nối rồi đọc lại.

GET https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/nodes?page=1&page_size=20

~~~json
{
  "page":1,"page_size":20,"total":1,
  "items":[{
    "node_id":"NODE-004","server_id":"iot-server-01","device_id":"LUXMAP-P001","feeder_id":"FDR-009",
    "status":"online","last_seen_at":"2026-11-19T08:00:00Z","electrical_status":"fresh"
  }]
}
~~~

Mặc định page=1, page_size=50, tối đa 200. Trang vượt tổng trả items rỗng.
status là liên lạc node; electrical_status là tuổi dữ liệu điện, hai trường độc lập.
Node vừa ACK lệnh có thể online nhưng electrical_status=stale. Truy vấn Bridge tối đa 12 luồng, ngân sách 5 giây; hàng chưa đọc kịp trả source_unavailable.
Server/node ngoài xã của người đọc trả 404; không lộ node khác cùng Bridge.

### Bước 5 — Manager điều khiển như trước

POST https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/command

~~~json
{"mode":"MANUAL","command":"OFF"}
~~~

ON đổi command thành ON. AUTO:

~~~json
{"mode":"AUTO","command":null,"auto_schedule":{"day_start":"06:00","night_start":"18:00"}}
~~~

Theo dõi bằng GET cùng URL. command_acknowledged=true xác nhận node đã thực hiện lệnh.

## 4. Chuyển bộ đang dùng LUXMAP-GW001

Không tự đổi chữ thường của NVS cũ vì key gắn chính xác với Client ID. Firmware vẫn đọc cấu hình cũ
cho đến khi người quản trị chủ động đổi. Sau khi BE mới deploy, các API trạng thái/key xác định theo
server_id mới; cần hoàn tất chuyển đổi để bộ cũ hiện đúng trạng thái và cho phép gán nguồn mới.

1. Backup PostgreSQL và toàn bộ volume data/ của Bridge, gồm credentials, keyring, routes và telemetry.
2. Deploy BE và Bridge cùng phiên bản, áp dụng migration AddIotServerGatewayIdentity.
   Migration chỉ thêm legacy_gateway_id nullable và unique index, không xóa nguồn/lịch sử.
3. Admin POST /api/v1/iot/servers/iot-server-01/credential để cấp key cho mã mới (GET nếu đã cấp).
4. Nạp espserver mới, mở cấu hình, đổi Server ID thành iot-server-01 và nhập secret mới.
   Giữ nguyên broker TLS. Chờ mã mới kết nối và gửi heartbeat; danh sách node có thể tạm rỗng.
5. Admin gọi:

POST https://luxmap.com.vn/api/v1/iot/servers/iot-server-01/gateway-migration

~~~json
{"legacy_gateway_id":"LUXMAP-GW001"}
~~~

Thành công 204. Bridge chỉ chuyển các device_id trùng nguồn đang gắn cho server trong BE;
nếu tuyến cũ có thiết bị ngoài danh sách đó thì 409, không tự chiếm tuyến của server khác.
Mã mới phải có key và heartbeat online; nếu chưa đáp ứng trả 409. Server phải tồn tại trong DB.
BE lưu alias và audit; Bridge gỡ tuyến cũ, thu hồi key cũ và ngắt phiên cũ.
Gateway mới nhận danh sách node. Dữ liệu điện/lịch sử lệnh vẫn gắn device/node/feeder cũ, không đổi tên.
Có thể gọi lại cùng request khi lần trước Bridge đã chuyển nhưng BE chưa commit được.

URL cũ dùng LUXMAP-GW001 chỉ tương thích sau bước 5; MQTT Client ID/username vẫn phải là iot-server-01.
Không gọi DELETE key qua alias cũ sau migration để dọn: alias trỏ key mới nên sẽ thu hồi key mới.

## 5. PUT/DELETE nguồn và giới hạn vận hành

PUT https://luxmap.com.vn/api/v1/iot/nodes/NODE-004 dùng đúng 6 trường:

~~~json
{
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009",
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500
}
~~~

Thành công 204 No Content. Không gửi cabinet_id, gateway_id, data_source, supports_remote_control,
relay_no, pole_id, cờ hiệu chuẩn/số bóng hoặc các ngưỡng nội bộ; BE dùng ngưỡng mặc định.
Tủ hiện tại giữ nguyên; feeder phải thuộc tủ đó. Nếu node đã rời tủ, BE lấy tủ từ feeder mới cùng xã.

DELETE https://luxmap.com.vn/api/v1/iot/nodes/NODE-004/feeder dùng Body none.
Manager cùng xã gọi; 204 kể cả gọi lại. Chỉ gỡ nguồn đo, binding relay/MQTT và chặn điều khiển qua nguồn đã gỡ;
node vẫn trong tủ, lịch sử lệnh/điện/lỗi không mất. Dùng PUT trên để gắn lại.
Thao tác gỡ không gửi ON/OFF vật lý; muốn đèn OFF thì gửi lệnh và kiểm ACK trước khi gỡ.
Node chưa gán feeder có trạng thái nguồn unconfigured. DELETE /nodes/NODE-004 vẫn gỡ cả khỏi tủ như trước.

Chỉ Manager trong cùng xã. Thiết bị mới chưa có nguồn phải được Admin cấp tuyến trước;
thiết bị đang gắn có thể chuyển sang server khác cùng Bridge với key đã cấp, cùng xã.
Bridge kiểm tra chủ hiện tại trước khi chuyển. Đổi device_id cần target đã được cấp quyền ở server đích.
DELETE gỡ tủ/feeder và ACL MQTT của device_id này, không thu hồi key của gateway hoặc node khác.
Dùng PUT cùng node_id để gắn lại; lịch sử theo feeder vẫn được giữ.

Giao dịch DB và HTTP Bridge không phải giao dịch phân tán nguyên tử. BE thử hoàn tác ACL khi DB lỗi
sau khi Bridge đã xác nhận. Nếu mất phản hồi, crash hoặc hoàn tác thất bại, API có thể trả 503;
Admin cần đối chiếu nguồn DB với tuyến Bridge và thử lại, không tạo node trùng để né lỗi.
Routes Bridge lưu snapshot đầy đủ, ghi bền rồi thay file nguyên tử để removals không hồi sinh sau restart.
Không thể chuyển một device_id giữa hai Bridge khác nhau bằng PUT này; trả 409.

## 6. Triển khai và kiểm tra

BE cấu hình IotDiagnostics__BaseUrl=http://iot-bridge:5000 và allowlist origin này.
Mqtt__BridgeKey và Mqtt__ProvisioningKey phải khớp BE/Bridge, không gửi ra FE/firmware.
Gateway chỉ giữ secret MQTT của riêng server và broker TLS. Cổng 8883 mở cho thiết bị;
HTTP 5000 nội bộ và MQTT không mã hóa 1883 không public internet.
Không cần đổi chứng chỉ CA TLS hoặc firmware node cho việc gộp ID.
Firmware espserver mới đã build; việc nạp thiết bị/deploy VPS thực hiện riêng sau khi kiểm thử.
