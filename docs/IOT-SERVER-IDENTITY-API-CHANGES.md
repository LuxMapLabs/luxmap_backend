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
| POST /api/v1/iot/servers | Thêm radius_km (0–1000, mặc định 0); server_id chuẩn hóa chữ thường, dùng luôn cho MQTT | ManageSettings (Admin) |
| GET /api/v1/iot/servers | Heartbeat theo đúng server_id, thêm credential_issued và radius_km, không dựa vào gateway khác cùng Bridge | ReadNetwork, lọc xã |
| PUT /api/v1/iot/servers/{serverId}/location | Nhận radius_km (0–1000, mặc định 0); cập nhật xã, GPS và bán kính | ManageSettings |
| POST /api/v1/iot/servers/{serverId}/credential | Mới, cấp key cho Client ID = username = server_id | ManageSettings |
| GET /api/v1/iot/servers/{serverId}/credential | Mới, xem lại key của đúng server | ManageSettings |
| DELETE /api/v1/iot/servers/{serverId}/credential | Mới, thu hồi key, xóa heartbeat và ngắt MQTT | ManageSettings |
| GET /api/v1/iot/servers/{serverId}/nodes | Mới, danh sách node đúng server, page/page_size | ReadNetwork, lọc xã |
| POST /api/v1/iot/nodes | Bỏ gateway_id trong request/response; tự gán ACL theo server_id | ManageSettings |
| GET /api/v1/iot/nodes/{nodeId} | Đọc cấu hình nguồn và ba chỉ số danh định; không gọi Bridge | ReadNetwork, lọc xã |
| PUT /api/v1/iot/nodes/{nodeId} | Chuyển nguồn giữa server cùng Bridge và cùng xã: kiểm tra chủ hiện tại rồi chuyển ACL | ManageAssets (Manager), lọc xã |
| DELETE /api/v1/iot/nodes/{nodeId}/feeder | Mới: gỡ khỏi feeder, giữ node trong tủ và giữ lịch sử | ManageAssets, lọc xã |
| DELETE /api/v1/iot/nodes/{nodeId} | Gỡ quyền gửi MQTT của thiết bị khỏi gateway; giữ node và lịch sử | ManageAssets, lọc xã |

Các API command, command/history, electrical, lịch sử điện theo feeder và diagnostics không đổi URL/JSON.
Kênh điều khiển vẫn là /api/v1/iot/nodes/{nodeId}/command.

### Các URL cũ còn tương thích, đã đánh dấu deprecated trong OpenAPI

- GET /api/v1/iot/servers/{serverId}/gateways/routes: chỉ trả tuyến canonical của server này, không trả toàn Bridge.
- PUT /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/route.
- POST/GET/DELETE /api/v1/iot/servers/{serverId}/gateways/{gatewayId}/credential.

Ở các URL có gatewayId, giá trị phải bằng server_id hoặc alias đã lưu từ phiên bản trước. Không tạo alias mới qua API.
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
  "location": { "lat": 10.965681, "lng": 106.504581 },
  "radius_km": 0.75
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
  "feeder_id": "FDR-009",
  "radius_km": 0.2,
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
Request có 7 trường: server_id, feeder_id, device_id, ba ngưỡng danh định và radius_km. BE suy ra cabinet_id từ feeder_id; feeder phải được gán vào một tủ điện trong cùng xã với server. Nếu feeder chưa gán tủ, API trả 400. Bán kính hợp lệ từ 0 đến 1000 km. Không gửi gateway_id, lamp_count_calibration, installed_lamp_count, data_source hoặc supports_remote_control. BE tự đặt data_source=calibration_rig và supports_remote_control=true cho bộ IoT testbed. Không thay đổi node đã có hoặc API /assets/iot-nodes.
Một device_id không được đồng thời thuộc hai nguồn trên cùng Bridge.

### Bước 4 — đọc trạng thái và danh sách node

GET https://luxmap.com.vn/api/v1/iot/servers

~~~json
[{"server_id":"iot-server-01","status":"online","commune_id":"COM-070","location":{"lat":10.965681,"lng":106.504581},"radius_km":0.75,"credential_issued":true}]
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

## 4. Chỉ dùng server_id — không có bước chuyển gateway

Luồng thêm mới chỉ dùng server_id để cấp key, cấu hình gateway và đăng ký node.
API gateway-migration và endpoint migration nội bộ Bridge đã bị gỡ ngày 19/11/2026.
Không gọi API chuyển đổi sau khi cấu hình Wi-Fi/MQTT; gateway gửi heartbeat bằng server_id.
Wi-Fi có IP không chứng minh MQTT đã đăng nhập bằng đúng server_id và key.

Giữ nguyên dữ liệu nguồn, node, feeder, lịch sử điện/lệnh/lỗi và alias đã lưu từ phiên bản trước.
Không tự chuyển tuyến, đổi tên key, thu hồi key cũ hoặc ngắt gateway cũ khi deploy.
Bộ cũ chưa chuyển vẫn giữ cấu hình cũ; nếu còn tuyến đứng tên mã cũ, cần quản trị kiểm tra
và xử lý binding có đối chiếu sở hữu, không xóa node/lịch sử hay cấp lại thiết bị để né xung đột.

## 5. PUT/DELETE nguồn và giới hạn vận hành

PUT https://luxmap.com.vn/api/v1/iot/nodes/NODE-004 dùng 7 trường, gồm radius_km:

~~~json
{
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009",
  "nominal_voltage": 5,
  "nominal_current_ma": 100,
  "nominal_power_mw": 500,
  "radius_km": 0.2
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


## Đồng bộ tuyến khi POST / PUT node (19/11/2026)

POST /api/v1/iot/nodes (Admin) thêm tuyến MQTT theo server_id sau kiểm tra xã/tủ/feeder.
Gửi lại POST cùng cấu hình trả 200 và cùng node_id, đồng thời khôi phục tuyến bị thiếu.
PUT /api/v1/iot/nodes/{nodeId} (Manager cùng xã) với cấu hình hiện có cũng đồng bộ lại tuyến,
không cần API gateway-migration. BE gửi lại danh sách node và đọc xác nhận Bridge trước khi trả thành công.
Server phải có key; MQTT/Wi-Fi và LoRa còn phải hoạt động để nhận mẫu/ACK, không hứa online ngay khi lưu.

Nếu tuyến của một nguồn DB đã tồn tại còn nằm ở gateway legacy chưa đăng ký thành server,
BE kiểm tra quyền sở hữu nguồn trong phạm vi xã rồi chuyển đúng device_id bằng compare-and-swap.
Không chuyển cả gateway, không thu hồi key cũ, giữ các device khác và toàn bộ lịch sử.
Không tự lấy thiết bị từ canonical ID hoặc alias của server khác đã đăng ký: trả 409.
Nguồn mới không được tự chiếm một tuyến đang có chủ; lỗi Bridge/không xác nhận được trả 503.


## GET cấu hình node đã lưu

GET https://luxmap.com.vn/api/v1/iot/nodes/NODE-004

Authorization: Bearer token có ReadNetwork, trong phạm vi xã node. Body none.
Phản hồi 200 (ví dụ):

```json
{
  "node_id": "NODE-004",
  "server_id": "iot-server-01",
  "device_id": "LUXMAP-P001",
  "feeder_id": "FDR-009",
  "nominal_voltage": 5,
  "nominal_current_ma": 93,
  "nominal_power_mw": 500,
  "radius_km": 0.2
}
```

nominal_voltage là V, nominal_current_ma là mA, nominal_power_mw là mW danh định đã cấu hình,
không phải số đo trực tiếp. Không trả password, khóa Bridge hoặc các ngưỡng nội bộ.
Đọc từ cấu hình đã lưu trong DB (hoặc cấu hình legacy đang hoạt động), không gọi Bridge;
vẫn trả được khi gateway/Bridge offline. Cache-Control: no-store.
401 chưa đăng nhập, 403 thiếu ReadNetwork, 404 node không tồn tại/ngoài xã hoặc chưa có nguồn đang hoạt động.
Nguồn đã gỡ trả 404 ở GET cấu hình; node và lịch sử điện/lệnh vẫn giữ nguyên ở API tương ứng.
FE có thể dùng bảy trường ngoài node_id, gồm radius_km, cho PUT cùng URL bằng token Manager cùng xã.
Admin tạo node qua POST; Admin không được cấp ManageAssets chỉ để gọi PUT.
