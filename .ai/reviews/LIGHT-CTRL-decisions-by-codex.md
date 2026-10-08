---
ticket: LIGHT-CTRL
reviewer: codex (gpt-6.1-sol, reasoning high)
date: 2026-10-08
scope: tham vấn quyết định D-1…D-10 (Mỹ uỷ quyền)
tokens: 51063
---

> Nguyên văn. Claude nhận cả bốn đề nghị đổi (D-1 dịch vụ độc lập kênh, D-4, D-10 seq, tách D-7) — xem LIGHT-CTRL-p1.md mục 6.

| Mục | Khuyến nghị độc lập | Lý do | Rủi ro nếu sai |
|---|---|---|---|
| **D-1** | **Đổi:** dịch vụ lệnh độc lập transport; adapter HTTPS polling 2–5 s trước. Chưa chốt MQTT khi telemetry còn UNKNOWN. | Đổi kênh truyền không phải đổi vòng đời, quyền hay audit. | Gắn nghiệp vụ vào HTTP khiến tích hợp MQTT phải viết lại hoặc sinh hai luồng lệch nhau. |
| **D-2** | **Đồng ý**, với TLS kiểm chứng chứng chỉ, xoay bí mật và không ghi secret vào log. | Bí mật ngẫu nhiên riêng từng node + `DeviceOnly` + scope node/xã phù hợp pilot. | Lộ secret cho phép giả thiết bị, ACK giả; chỉ scope xã sẽ lọt truy cập chéo node. |
| **D-3** | **Đồng ý:** `CMD`, **tối thiểu** 6 chữ số; UUID cho nhóm nội bộ. | Dễ tra audit, theo quy ước ID hiện có. | Hiểu “đúng 6 chữ số” gây cắt/trùng ID khi vượt ngưỡng. |
| **D-4** | **Đổi một phần:** giữ khoá, TTL, giao lại cùng ID; ACK đóng chỉ lưu lịch sử, chỉ cập nhật mode khi báo cáo còn mới theo D-10. | Khoá DB không loại được ACK đến trễ; hết hạn ACK không chứng minh chưa thực thi. | Mode cũ ghi đè mode mới; UI báo hết hạn trong khi rơ-le đã đổi trạng thái. |
| **D-5** | **Đồng ý:** poll xác thực tính là sống; tách rõ độ mới telemetry. | Kết nối điều khiển hoạt động không chứng minh cảm biến đang đo. | Node “online” che telemetry ngừng hoặc dữ liệu điện đã cũ. |
| **D-6** | **Đồng ý:** `ReadNetwork`, vẫn scope xã; không trả credential. | Xem trạng thái phục vụ giám sát, không cấp quyền điều khiển. | Rò lịch sử ngoài địa bàn hoặc nhầm quyền xem thành quyền gửi. |
| **D-7** | **Đồng ý:** Quản lý qua `ManageAssets`; giữ phân tách với Quản trị hệ thống. | Khớp D-R12 và quyền quản lý tài sản đã chốt. | Nối nhầm rơ-le hoặc xoay secret làm gián đoạn/điều khiển nhầm mạch. |
| **D-8** | **Đồng ý có điều kiện:** gửi phần hợp lệ; UI xác nhận phần loại và tuyến bị kéo theo; server kiểm lại quyền/ánh xạ lúc gửi. | Thành công một phần hữu ích nhưng phải thể hiện đúng phạm vi tác động. | Người dùng tưởng toàn tuyến đã đổi; mạch chung tác động tuyến ngoài dự kiến/phạm vi. |
| **D-9** | **Đồng ý có điều kiện:** hoãn HMAC/nonce cho testbed; giữ TLS, secret riêng, TTL, dedup và bảo vệ thứ tự D-10. | NFR không chỉ định cơ chế chống replay; bearer tĩnh là đánh đổi có thể giải trình. | Secret bị lấy có thể giả ACK; idempotency không chặn nội dung giả mới. |
| **D-10** | **Đổi:** có thứ tự tối thiểu cho lệnh và báo cáo, xử lý được reboot; không dùng thứ tự tới server. | Mạng chập chờn/giao lại khiến thứ tự nhận khác thứ tự thực thi. | Lệnh cũ được thực thi lại, ACK cũ làm sai trạng thái hiện tại và audit. |

- **Tách D-1:** chốt dịch vụ lệnh ngay; polling là adapter đầu tiên; lựa chọn transport cuối cùng chờ Đạt xác nhận. Telemetry dùng MQTT không tự động buộc control dùng MQTT.
- **Báo firmware:** thống nhất payload lệnh/ACK độc lập transport, dedup `command_id`, kiểm hạn, loại lệnh cũ, ACK sau thực thi, thứ tự qua reboot, quy tắc AUTO và trạng thái khởi động; MQTT QoS không thay ACK nghiệp vụ.
- **Tách D-9/D-10:** chống replay xác thực có thể hoãn; chống thực thi trùng và báo cáo đảo thứ tự phải có trước nghiệm thu. Tách phần đăng ký/rơ-le/credential của **D-7** thành phần BE-34 có phạm vi riêng.
- **Hội đồng:** D-9 có thể bảo vệ với giới hạn pilot và bằng chứng reconnect/reboot/ACK trễ; D-10 nguyên bản chưa thuyết phục. Phiếu yêu cầu buffering và auditability, không mặc nhiên miễn tính đúng vì là testbed.
- ✅ Đã đối chiếu [đặc tả](.ai/results/LIGHT-CTRL-p1.md), [AGENTS.md](AGENTS.md), [Phiếu v1.4](docs/registration/FA26SE222_v1.4.md). ⚠️ Chưa kiểm chứng hành vi; không build/DB/sửa file. 📌 Transport telemetry và khả năng firmware còn chưa xác nhận.
