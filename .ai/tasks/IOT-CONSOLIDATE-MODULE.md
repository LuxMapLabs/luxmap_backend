---
title: Phase 2-A — hợp nhất module Telemetry vào IOT, giữ hành vi
status: complete
---

## Phạm vi đã được tiếp tục trong chat 15/11/2026

Mỹ xác nhận PR #257 đã merge và yêu cầu làm tiếp sau lộ trình A→B→C.
Thực hiện bước A của `.ai/results/IOT-CONSOLIDATION-p1.md` trên `origin/dev = 66e1c83`:
chuyển entity/config/registry/lighting/MQTT/job và port của Telemetry sang Modules.IOT;
gỡ project Telemetry sau khi chuyển đủ trách nhiệm và mọi reference hoạt động.

## Acceptance

- IotModule đăng ký đủ service, job offline, version registry columns, HTTP/MQTT và Bridge; không đăng ký trùng.
- Giữ các port Assets, scope/guard, auth/capability, tên bảng/FK/index/sequence/job, API/body/schema và firmware protocol.
- EF model không có DDL thay đổi so với dev; không tạo migration. Historical migration metadata được giữ nguyên.
- Build toàn solution, regression + real MQTT, OpenAPI so khớp dev, kiểm DB tạm/migration và review độc lập.
- Tài liệu/traps/dashboard cập nhật; commit local. Push/PR cho bước A cần xác nhận riêng.

## KHÔNG ĐƯỢC làm

Không hiện thực bước B/C: chưa siết một feeder/node, chọn mapping canonical, đổi TTL/ACK/AUTO,
xóa relay endpoint/field, chuyển kênh firmware, xóa lịch sử, sửa khoảng trống Bridge last-report/firmware/backup.
Không thay package version, tạo schema/migration/config/API, apply DB dev/VPS, deploy hoặc gửi tin ngoài repo.
Không sửa workspace gốc. D-1…D-8 của bước B/C vẫn chờ chốt, không suy ra đã duyệt tất cả từ “làm tiếp”.


## Mỹ duyệt publish/merge sau review (15/11/2026)

Mỹ yêu cầu ghép dev sau PR #258, gửi Claude review khả năng push; nếu đạt thì push, mở PR,
đợi CI xanh và merge bằng admin. Đây là phê duyệt tường minh cho chuỗi publish/merge của bước A,
thay trạng thái “cần xác nhận riêng” ở trên; không phê duyệt deploy/DB dev hoặc hiện thực B/C.
