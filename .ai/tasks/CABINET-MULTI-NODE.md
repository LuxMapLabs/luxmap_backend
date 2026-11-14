---
title: Một cabinet chứa nhiều IoT node — phase 1
status: complete
---

## Phạm vi đã được Mỹ duyệt trong chat (14/11/2026)

- Cho phép nhiều IoT node trong cùng cabinet; mỗi node vẫn bắt buộc thuộc một cabinet cùng xã.
- Thêm `iot_node_ids[]` trên danh sách / chi tiết tài sản tủ và properties bản đồ tủ.
- Giữ `iot_node_id` tương thích: phần tử đầu danh sách, hoặc null; danh sách theo `created_at, length(node_id), node_id`.
- Migration chỉ thay unique index theo cabinet bằng index thường, giữ dữ liệu và các FK hiện có.
- Kiểm tra đăng ký đồng thời, đọc đủ node, phạm vi xã, relay / lệnh độc lập giữa hai node cùng tủ và rollback migration.

## KHÔNG ĐƯỢC làm

- Không thực hiện phase 2: không bỏ relay, không ép mỗi node chỉ điều khiển một feeder.
- Không đổi giao thức MQTT, quyền, enum, bí mật thiết bị hoặc lịch sử lệnh.
- Không xóa bảng hoặc dữ liệu; không migrate DB dev / VPS, deploy hoặc push.
- Không đưa thay đổi có sẵn của người dùng vào commit này.

## Hoàn thành

Build và regression test xanh; migration đọc Up/Down và diễn tập trên container riêng; OpenAPI sinh lại / lint;
dashboard và tài liệu cập nhật; review độc lập, diff và commit theo đường dẫn tường minh.
