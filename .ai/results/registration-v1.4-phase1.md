# Registration v1.4 — Phase 1: khảo sát tác động (27/09/2026)

> Phase 1: **chỉ đọc + tài liệu**. Không đổi Contract, spec, migration hay `src/`. Quyết định mở là
> D-R20…D-R28 ở `docs/contract-drift.md` (mục *Registration v1.4*); Phase 2 chỉ bắt đầu sau khi chúng được chốt.
> Nguồn phiếu: `docs/registration/FA26SE222_v1.4.md`.

## 1. Tóm tắt thay đổi phiếu

- **CV chỉ quyết ON/OFF** cho từng bóng; camera điện thoại quay chéo lên, bắt một bóng khi xe ngang cột, khoá phơi sáng.
- **Độ sáng đo bằng BH1750FVI** gắn nóc xe, lấy mẫu liên tục; **đỉnh lux lúc ngang cột** là số đo của cột cho phiên đó.
- **Dim** = CV nói ON **và** đỉnh lux dưới một tỉ lệ cấu hình được của **baseline chính cột đó**. Out = CV nói OFF.
- Trạng thái **đoạn** suy từ các cột (Out/Dim liền nhau).
- Controlled Reference Capture Set **không còn** trong deliverable. Actor / vai trò **không đổi** so với v1.2.
- v1.3 (đánh giá mặt đường theo đoạn) đã nộp nhưng repo **không** áp dụng — bỏ qua.

## 2. Chỗ trong repo đang viết theo nền cũ

| Chỗ | Nội dung hiện tại | Vì sao lệch với v1.4 | Xử lý ở Phase 1 |
|---|---|---|---|
| `CLAUDE.md` đầu file | Nguồn chuẩn = `FA26SE222_v1.2.md` | Phiếu mới | Trỏ sang v1.4 |
| `CLAUDE.md` › Phạm vi | Thực địa: "đo sáng tương đối bằng **điện thoại**"; ground truth nhiều nguồn gồm lux điện thoại + controlled reference | BH1750 trên xe; lux là đầu vào, không là ground truth | Thêm cảnh báo nền đã đổi, không sửa bảng |
| `CLAUDE.md` › Bốn quy tắc BE-42, quy tắc 1 | "`LuxReading` KHÔNG phải `luminance_history` … lux chấm CV" | Ở v1.4 chuỗi luminance **chính là** lux (D-R23) | Thêm cảnh báo đầu mục |
| `CLAUDE.md` › Enum | "`lamp_dim` và `lamp_out` **chỉ** đến từ CV" | `lamp_dim` nay do CV **và** cảm biến | Ghi chú trỏ D-R20 |
| `CLAUDE.md` › Quy tắc dễ sai âm thầm | P/R riêng out/dim "(NFR phiếu v1.2)" | NFR v1.4: P/R/F1 cho ON/OFF + đối chiếu Normal/Dim/Out với field verification | Cập nhật trích dẫn |
| `CLAUDE.md` › Ai đang chờ | BE-42 cho FO-14 "đo lux W5" | FO-14 đổi phương pháp (D-R27) | Ghi chú |
| `api-contract-v1.1.md` §5.7 | `lux_value` = cảm biến **điện thoại**, `meter_model` = model điện thoại (D-R15) | BH1750 | **Không sửa** — chờ D-R21 |
| `api-contract-v1.1.md` §5.2 (detail) | `out_threshold_ratio` 0.15 | Out do CV quyết | **Không sửa** — chờ D-R22 |
| `src/LuxMap.Modules.Survey/Entities/LuxReading.cs` XML doc | "a human with a meter", "an ABSOLUTE `lux_value`", CV-12 chấm bằng lux | Lệch cả D-R15 (tương đối) lẫn v1.4 | **Không sửa code** ở Phase 1 — đưa vào Phase 2 |
| `docs/tasks-backend.csv` BE-42 | "API nhập số đo lux từ hiện trường … phục vụ CV-12" | D-R21, D-R23 | Giữ nguyên tới khi chốt |
| `tracking.html` | Follow-up v1.2 còn mở: ground truth nhiều nguồn, `calibration_rig` cho telemetry | Hai mục này đổi nền lần nữa | Thêm bảng follow-up v1.4 |

## 3. Những gì KHÔNG đổi (và vì sao đây là tin tốt)

- Enum Contract mục 1 — `fixture_status`, `fault_type` (`lamp_out`, `lamp_dim`), `data_source` — giữ nguyên.
- Hình dạng `luminance_baseline` / `luminance_history` (`baseline_ratio`, `classified_as` tính ở backend) giữ nguyên; chỉ nguồn đổi.
- Ngưỡng là **tỉ lệ so với baseline của chính cột**, không so hai cột — nay còn đúng hơn, vì lux tại xe phụ thuộc chiều cao cột, khoảng cách ngang, làn.
- BE-16 (từ chối frame thiếu metadata phơi sáng) giữ nguyên.
- Vai trò, ma trận capability, Citizen/QR, điều khiển testbed, audit trail.

## 4. Câu hỏi cần chữ ký trước Phase 2

Xem bảng D-R20…D-R28. **D-R21** và **D-R24** đã chốt hướng ngày 27/09 (SELF-SIGNED): một phiên = video +
log lux BLE + GPS track + cấu hình quay, chung đồng hồ điện thoại, server xử lý toàn bộ. Còn chặn việc:
**D-R28** (upload video vài GB — chặn BE-15) và **D-R23** (ground truth của `dim` — chặn tiêu chí CV-12).
