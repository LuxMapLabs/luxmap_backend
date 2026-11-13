# LuxMap IoT bridge

Chạy `dotnet run --project src/LuxMap.IotBridge` từ gốc repository. Dịch vụ cung cấp HTTP cổng 5000 và MQTT cổng 1883. Chạy trên mạng testbed tin cậy và giới hạn quyền truy cập hai cổng trong mạng nội bộ. MQTT nhận diện gateway theo client ID trong `Mqtt:Gateways`. Backend chính đăng ký HTTP origin của bridge trong allowlist và cấu hình nguồn cho node.

Giữ thư mục `data/` của bridge trên ổ đĩa bền vững. `telemetry.sqlite` lưu các mẫu MQTT theo từng thiết bị. `devices.json` giữ lệnh, mẫu gần nhất và dữ liệu tương thích. Khi chạy lần đầu với `devices.json` cũ, bridge nhập lịch sử còn giữ trong file vào SQLite.

Backend chính đọc lịch sử theo quyền và phạm vi xã qua `GET /api/v1/iot/nodes/{nodeId}/electrical/history`. Dùng `next_cursor` làm `after` cho lần gọi tiếp theo. Bridge chỉ gửi ACK sau khi lưu dữ liệu thành công.

Cấp secret bằng POST /api/luxmap/gateways/{gatewayId}/credential trên bridge. Đặt Mqtt__ProvisioningKey (ít nhất 32 ký tự ngẫu nhiên) và gửi nó qua header X-Iot-Provisioning-Key từ localhost hoặc HTTPS nội bộ. Secret chỉ hiện một lần, hash được lưu trong data/gateway-credentials.json. MQTT TCP chỉ dành cho mạng nội bộ tin cậy.
Đặt cùng Mqtt__BridgeKey (ít nhất 32 ký tự) trên BE và bridge. API đọc và điều khiển dùng X-Iot-Bridge-Key. API provisioning còn yêu cầu Mqtt__ProvisioningKey. Swagger bridge chỉ bật trong Development.
