---
title: Đưa phase 1 nhiều node/tủ lên dev sau PR 256
status: complete
---

## Mỹ chốt trong chat 15/11/2026

- Port phase 1 lên dev hiện tại, giải conflict, giữ ảnh tài sản / feeder power_state và các chức năng đã merge.
- Giữ `iot_node_ids[]` có thứ tự và scalar tương thích; giữ relay và Telemetry trong phase 1.
- Khi Bridge và relay gán hai node khác nhau cho cùng feeder: **chặn thao tác mâu thuẫn, trả lỗi rõ ràng**.
- Chặn nguồn cấu hình tĩnh + nguồn DB trùng feeder gây 500; kiểm cả hai thứ tự ghi và concurrency.
- Phase 1 xong mới khảo sát phase 2 bỏ relay / hợp nhất Telemetry vào IoT/Bridge, chưa hiện thực phase 2.

## KHÔNG ĐƯỢC làm

- Không xóa module Telemetry, bảng, dữ liệu hoặc lịch sử; không đổi giao thức firmware / enum / quyền.
- Không kéo lịch sử SRS/branding không liên quan từ nhánh cũ; không hồi sinh DBML 51 bảng chưa từng nằm trên dev.
- Không đụng thay đổi chưa commit tại workspace gốc, không apply DB dev/VPS, không deploy hoặc push chưa xác nhận.
- Không tự quyết các D-item phase 2; chỉ khảo sát, nêu lựa chọn và phương án chuyển đổi.

## Done

Resolve đúng hai phía; migration metadata theo model dev, Up/Down đúng phạm vi; test hồi quy và HTTP/DB tạm;
OpenAPI sinh lại/lint; review độc lập; tracking cập nhật; commit local; sau đó khảo sát phase 2.
