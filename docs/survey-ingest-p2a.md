# BE-15 P2a — API nhận phiên khảo sát

**SELF-SIGNED, nền tạm tới FW**, theo chốt D-01…D-15 trong `.ai/results/BE-15-p1.md` §11
và phạm vi `.ai/tasks/BE-15-p2a.md`. Không thay thế Contract hợp nhất.

- `ReadSurveys`: cả bốn vai trò; kỹ sư chỉ thấy phiếu được giao, vẫn kiểm xã.
- `SubmitSurveys`: field_engineer được giao phiếu. Phiếu phải `in_progress` khi nhận dữ liệu mới.
- `ReviewSurveys`: manager; chưa có endpoint duyệt trong P2a.
- Mọi lookup qua sweep → phiếu đã scope và kiểm quyền các tuyến; ngoài quyền trả 404.
- Nộp phiên không hoàn thành phiếu, không chạy detector, không ghi trạng thái cột/sự cố.

## Giao phiếu

`POST /api/v1/work-orders`: `task_kind: "survey"`, `title`, **`commune_id` làm xã neo**,
`segment_ids: [...]` có thứ tự, `assigned_to?`, các trường lịch/ghi chú hiện có.
Không gửi `segment_id`; `fault_ids` rỗng hoặc bỏ qua. Giới hạn 1–200 tuyến, không trùng.
Detail trả `segment_ids` theo đúng thứ tự; `segment_id` null. Tạo và giao lại đều kiểm người được
giao phủ các xã của tuyến và các cột trong phạm vi người giao. Không ép chiều/thứ tự di chuyển.
`commune_id` vẫn bị từ chối cho inspection/repair. Kỹ sư gọi `/work-orders/{id}/start` trước khi thu.

## Tạo phiên

`POST /api/v1/sweeps`, ví dụ:

```json
{
  "work_order_id": "WO-0001",
  "client_op_id": "01f52939-e90d-4f8f-bd44-1c91a1ecaf5c",
  "boot_session_id": "95b59f63-5083-4e5f-911d-c070e75351d5",
  "elapsed_anchor_ns": "9007199254740000",
  "started_elapsed_ns": "9007199254740000",
  "utc_anchor": "2026-10-02T12:00:00Z",
  "utc_uncertainty_ms": 2,
  "data_source": "simulated"
}
```

201 khi tạo; 200 retry cùng người/key/body đã chuẩn hóa; 409 `IDEMPOTENCY_CONFLICT` khi khác body.
Timestamp UTC lưu micro giây; đồng hồ ns giữ bigint nguyên vẹn. ID do sequence DB sinh.
Chưa tạo registry/profile trong P2a; profile/firmware do file cấu hình thô khai báo, chưa xác minh registry.

## Upload

`PUT /sweeps/{id}/clips/{clip_no}`: body MP4 nhị phân, bắt buộc `Content-Length` dương và
`X-Content-SHA256` là 64 ký tự hex thường. Không multipart/form-data.
Giới hạn 314572800 byte (300 MiB). Kiểm `ftyp`, SHA-256 và byte count thật; codec/khả năng decode
để P2b kiểm bằng ffmpeg. Multipart S3 giữ tối đa một phần 5 MiB trong buffer ứng dụng, abort khi lỗi/hủy.
200 lưu thành công hoặc retry cùng hash/size; 409 khi slot đã có hash khác; 400 hash/length sai;
413 quá cỡ; 415 thiếu MP4 magic. Ghi object xong mới ghi row.

`PUT /sweeps/{id}/raw/{kind}`: `gps_track`, `lux_log`, `capture_config`. Cap 10 MiB/file, 200 khi
lưu/retry nguyên byte; khác byte tại slot đã ghi trả 409. Giữ nguyên BOM/newline/whitespace trong
object store. Parse hết trước khi ghi bất cứ object/sample nào; lỗi 400 có `details.line/field`.
Sau submit không nhận slot mới. Retry slot cũ cùng hash vẫn trả 200.

GPS/lux là UTF-8 JSONL có header schema_version=1 + boot_session_id khớp phiên. Các schema mẫu ở
`.ai/results/BE-15-p1.md` §4.1. `phone_elapsed_ns`, `module_ms` là chuỗi chữ số ASCII không âm,
không quá bigint; `sample_no`, `module_epoch`, `seq` là số nguyên JSON. `sample_no` bắt đầu từ 0
và không được xung đột; mẫu giống hệt lặp lại được bỏ trùng. Không sort lại chuỗi mẫu.
GPS: lat/lng đúng miền, accuracy/speed không âm, heading [0,360). Lux không âm. Tất cả số phải hữu hạn.

`capture_config` là object JSON theo §4.2: schema/boot, profile/firmware ID, phone_model, camera_id,
app_version, sensor_timestamp_source, clock anchor khớp sweep; object `camera` gồm iso,
exposure_time_ns (chuỗi), aperture, fps, focus_mode/distance, white_balance_mode/value,
resolution.width/height, bốn cờ ae/eis/hdr/night_mode; object `mount` gồm camera_side,
mount_height_m, angle_deg, sensor_position. Metadata mở rộng và mapping frame được giữ nguyên,
không dựng frame/run hay xác nhận chất lượng đồng bộ tại P2a. Không tự điền thông số camera.

## Nộp phiên

`POST /sweeps/{id}/submit`:

```json
{
  "client_op_id": "b636c3ac-a84c-47f7-b2f5-955ddfd3f97d",
  "ended_elapsed_ns": "9007199255740000",
  "manifest": {
    "clips": [{"clip_no": 0, "sha256": "<64 hex thường>"}],
    "gps_hash": "<64 hex thường>",
    "lux_hash": "<64 hex thường>",
    "config_hash": "<64 hex thường>"
  }
}
```

Ít nhất một clip + đủ ba raw, manifest phải khớp toàn bộ file đã ghi. Thiếu → 409
`UPLOAD_INCOMPLETE`, `details.missing`; hash/danh sách lệch → 409 `MANIFEST_MISMATCH`.
202 chuyển cả status/processing_status sang queued; retry cùng payload → 200 trạng thái hiện tại,
khác → 409 `IDEMPOTENCY_CONFLICT`. Một audit tạo và một audit nộp, cùng transaction dữ liệu.

## Đọc và giới hạn hiện tại

`GET /sweeps`: page/page_size (max 200), work_order_id, segment_id, processing_status, data_source.
Enum bộ lọc là snake_case. Sắp tăng `created_at, length(sweep_id), sweep_id`.
`GET /sweeps/{id}` trả cùng thông tin phiên và manifest upload, không trả object key.
`started_at/ended_at` suy từ anchor + đồng hồ thu, không lấy giờ upload; ended_at null khi chưa nộp.
`frame_count` là số frame đã lưu của phiên sau P2b-2; `coverage_pct` giữ mức đi ngang cột (P2b-1). Ngoài miền DateTime trả null thay vì tràn số.

Migration `AddSurveyIngest` chỉ thêm các bảng ingest và mở rộng CHECK, thêm FK
`pole_current_status.last_sweep_id`. Apply phải kiểm dữ liệu mồ côi trước; migration không tự sửa/xóa
lịch sử. `Down()` mất bảng dữ liệu khảo sát và không chạy được nếu còn phiếu/audit survey khi khôi
phục enum cũ. Ưu tiên rollback ứng dụng tương thích, giữ dữ liệu; không chạy rollback để dọn dữ liệu.


## Ánh xạ video — P2b-2 (SELF-SIGNED, chờ D-06/WP6)

P2a vẫn giữ cấu hình gốc; worker P2b-2 yêu cầu `sensor_timestamp_source = "REALTIME"`,
`mount.camera_side = "front" | "left" | "right"` và đủ một entry cho từng clip:

```json
"clips": [{
  "clip_no": 0,
  "first_pts_ns": "0",
  "last_pts_ns": "280000000",
  "first_sensor_timestamp_ns": "1000000000",
  "last_sensor_timestamp_ns": "1280000000",
  "time_base_num": 1,
  "time_base_den": 1000
}]
```

Đây là đoạn nằm trong object `capture_config` đã có metadata camera/mount đầy đủ ở trên.
Nanosecond nhận chuỗi thập phân hoặc số nguyên JSON chính xác; ưu tiên chuỗi để không mất bit ở JavaScript.
PTS có thể âm. Ánh xạ mô phỏng hiện tại có slope 1: `phone = first_sensor + pts - first_pts`;
hai cặp đầu/cuối phải nhất quán. Clip phải có thời lượng dương, không lặp số clip/không chồng khoảng thời gian.
Worker đối chiếu time base và PTS đầu/cuối **thực từ ffprobe**; thiếu hoặc mâu thuẫn trả mã run
`CLOCK_VIDEO_MAPPING`. Không dùng FPS trung bình hay thời điểm bấm quay làm đồng hồ.
Clip không phủ tâm cửa sổ của cột → `video_gap`, không lấy ảnh từ clip kế bên.

Quy ước bên ảnh tạm: camera `front`, cột bên trái chiều đi dùng nửa trái ảnh; bên phải dùng nửa phải.
Camera `left/right` chỉ nhận cột cùng bên chiều đi. Bên cột lấy dấu tích có hướng với tiếp tuyến tuyến
trong SQL EPSG:3405, đảo dấu ở lượt ngược. Cột nằm trên tuyến/mơ hồ bị để `unknown`.
Đây chưa là phép hiệu chuẩn camera; cần kiểm bằng mẫu gắn đầu xe của WP6/FO.

Cờ `ambiguous_association` của ghép lux không chặn ON/OFF: bỏ peak khi phân loại, ON → normal,
ratio null, không đủ điều kiện xét dim; OFF → out. Cờ `route_ambiguous` chặn CV vì không có thời điểm
ngang cột đáng tin; `video_gap` chặn CV vì clip không phủ thời điểm đó. `gps_degraded` và
`gps_offset_unresolved` vẫn giữ thời điểm ngang cột để xét CV. Ghép trong cửa sổ mặc định trước 3 s,
sau 0,5 s, lấy 5 frame/giây: một detection đạt chất lượng mỗi frame ở phía cột, cùng nhãn là một track
(dù bbox không giao nhau). Nhiều detection cùng frame, xung đột ON/OFF hoặc evidence dùng chung
vẫn để unknown.

D-06 chưa có mẫu Camera2/sidecar thực để chứng minh affine, D-07 chưa có model thật:
worker P2b-2 chỉ xử lý nguồn `simulated`; nguồn khác thất bại `VIDEO_DEVICE_MAPPING_PENDING`.
Fake không tự thay thế detector thật, và không được chọn ở Production/Staging.
`frame_count` trả số row frame của sweep; coverage phát hiện và coverage đủ xét dim lưu riêng trong run,
cùng mẫu số pole dự kiến của snapshot. Chưa thêm trường API cho hai mức này, chưa công bố kết quả.
