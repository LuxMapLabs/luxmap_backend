---
title: Khảo sát phase 2 bỏ relay và hợp nhất Telemetry vào IoT/Bridge
status: complete
---

## Yêu cầu Mỹ trong chat 15/11/2026

Chỉ sau khi phase 1 nhiều node/tủ tích hợp dev, kiểm chứng và commit: khảo sát phase 2.
Mô hình mong muốn: một cabinet nhiều IoT node, một node điều khiển một feeder;
bỏ quản lý relay trên bề mặt nghiệp vụ, cân nhắc hợp nhất module Telemetry vào IOT/Bridge.

## Tiêu chí khảo sát

- Đối chiếu code thực: model/FK, module dependency, hai đường điều khiển, auth, audit, firmware, job offline,
  telemetry/retention, API/FE và triển khai/backup.
- Phân biệt bỏ abstraction relay, bỏ module .NET và bỏ dữ liệu đo; không coi là một thao tác xóa.
- Đề xuất thứ tự chuyển đổi có kiểm dữ liệu, tương thích và rollback; nghi vấn thành D-item để Mỹ chốt.
- Cập nhật tracking và commit báo cáo khảo sát riêng với phase 1.

## KHÔNG ĐƯỢC làm

Không hiện thực phase 2; không xóa module/bảng/cột/dữ liệu, đổi API/firmware/auth/config/dependency hoặc tạo migration.
Không áp DB dev/VPS, deploy/push, liên hệ WP5/WP6/Đạt hoặc tự chốt D-item.
Không sửa workspace gốc; khảo sát dựa trên commit phase 1 `f46364d`.

Khảo sát hoàn tất: `.ai/results/IOT-CONSOLIDATION-p1.md`; D-1…D-8 chờ Mỹ chốt, chưa triển khai phase 2.
