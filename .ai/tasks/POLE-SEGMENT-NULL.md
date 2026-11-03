---
ticket: POLE-SEGMENT-NULL
title: Cột (pole) được phép chưa thuộc tuyến — `pole.segment_id` nullable
status: ready
phase: 1
owner: codex (P1, P2) · claude (review)
branch: docs/POLE-SEGMENT-NULL-p1
contract_refs:
  - "docs/api-contract-v1.1.md — POST /assets/poles (segment_id), PoleListItem, import poles, GET /work-orders/{id}/poles, GET /sync/bundle"
  - "docs/openapi/luxmap-v1.5.json (file SINH)"
depends_on: []
---

## Bối cảnh

Hôm nay `Pole.SegmentId` là `required` (`src/LuxMap.Modules.Assets/Entities/Pole.cs`) và `poles.csv` bắt buộc
`segment_external_ref`, nên khu vực mới phải có tuyến TRƯỚC khi nhập được cột. Ở xã mà OSM thiếu đường thì
việc nhập cột bị chặn. **Mỹ chốt 02/11/2026: cho `pole.segment_id` nullable** (phương án "nullable" trong cuộc thảo luận
cùng ngày; không chọn tuyến giả "Chưa phân tuyến" vì `road_segment.geom` bắt buộc → bịa hình học; không chọn
"giữ NOT NULL + sinh tuyến từ cột").

Tuyến vẫn là khoá nghiệp vụ ở nhiều chỗ: phiếu khảo sát giao theo `segment_ids`, hành lang khảo sát tính từ hình học
tuyến, `GET /work-orders/{id}/poles` sắp cột dọc đường, sự cố cấp đoạn (`fault_cluster`), thống kê, bản đồ. Khoảng 40 file
`src` đọc `SegmentId`. Cột null tuyến sẽ **lặng lẽ rơi khỏi** những chỗ đó nếu ta không xử lý tường minh — đó là rủi ro chính
của ticket, không phải việc đổi cột.

Consumer bị ảnh hưởng: WP5 (web), WP6 (Android, `GET /sync/bundle`), WP4 (engine CV) — qua hình dạng response.

## Quyết định đã chốt (Mỹ, 02/11/2026) — KHÔNG mở lại

1. `pole.segment_id` nullable. **Chỉ `pole`** — chưa quyết cho `fault` / `fault_cluster`; nếu khảo sát thấy buộc phải đổi
   thì ghi D-item, không tự đổi.
2. `null` = trạng thái tường minh **"chưa phân tuyến"**. Cột đó **không được giao vào phiếu khảo sát** cho tới khi có tuyến.
3. Quản lý có bộ lọc "cột chưa gán tuyến". Thống kê đếm nhóm này **riêng**, không gộp vào tuyến nào (cùng tinh thần `unknown`
   không gộp vào `out`).
4. Chỗ nào không chấp nhận cột null tuyến thì **từ chối rõ ràng bằng lỗi có lý do**, không bỏ qua im lặng.

## Phải đọc trước

- `CLAUDE.md` (= `AGENTS.md`), đặc biệt: quy ước API, "Bẫy chung", "Test".
- `docs/agents/traps/assets.md` (mục `segment_id` KHÔNG có khoá ghép — cố ý; import; topology), `spatial-map.md`,
  `faults-workorders.md`, `survey.md`, `migrations.md`, `commune-scope.md`, `testing.md`.
- `docs/api-contract-v1.1.md` mục 5.3 (tài sản), POST /assets/poles (dòng ~733), `GET /work-orders/{id}/poles`, sync.
- `docs/contract-drift.md` — các mục TOPO-INFER (mẫu: nhãn nguồn đi cặp với quan hệ nullable), CAB-13.
- `docs/database/erd.md`.
- `src/LuxMap.Modules.Assets/Entities/Pole.cs`, `Configurations/AssetConfigurations.cs`, `Crud/AssetCrudService.cs`,
  `Import/AssetImportService.cs`, `docs/templates/README.md` + `poles*.csv`.

## Yêu cầu — PHASE 1 (khảo sát, KHÔNG sửa code)

Ghi kết quả vào `.ai/results/POLE-SEGMENT-NULL-p1.md`. Mọi khẳng định kèm **lệnh sinh ra nó** và output thật (`grep -n`,
đọc file) — không viết từ trí nhớ.

1. **Bản kiểm kê consumer.** Mọi chỗ đọc / ghi `pole.segment_id` / `Pole.SegmentId` / `PoleListItem.segment_id`: `src/**`,
   SQL thô (`FromSql`, `ExecuteSql`, `SqlQuery`), `scripts/**`, `mocks/**`, `tests/**`, OpenAPI. Dạng bảng:

   | File:dòng | Làm gì với segment | Hành vi HIỆN TẠI nếu segment null | Đề xuất (refuse / exclude / show_unassigned / n-a) | Cần test |

   Chú ý riêng: **INNER JOIN `road_segment`** hoặc `pole.Segment.…` (navigation) sẽ làm cột null biến mất không báo lỗi.
   Liệt kê từng chỗ và nói rõ đã đọc `Include`/join kiểu nào.
2. **Schema thật.** Từ migrations + model snapshot (KHÔNG chạy gì trên `luxmap_dev`): cột, FK (`Restrict`), index có
   `segment_id`, CHECK liên quan, `HasCommuneReference`. Mô tả `Up()`/`Down()` cần có (viết ra văn bản, **không tạo file
   migration**). `Down()` sẽ làm gì khi đã có cột null (không được âm thầm xoá/đổi dữ liệu).
3. **Hình dạng API.** Với từng endpoint / DTO chạm tới: `POST/PUT /assets/poles`, `PoleListItem`, import `poles.csv`
   (`segment_external_ref`), `GET /map/poles` + `/map/poles/{id}` (`properties`), `GET /work-orders/{id}/poles`,
   `GET /sync/bundle` + `/sync/push`, `GET /faults`, thống kê: field nào thành optional / nullable, mã lỗi mới nếu cần,
   thay đổi này **additive hay breaking** cho WP5/WP6. Nêu rõ tác động lên `luxmap-v1.5.json` và chỗ nullable `$ref` trần
   (drift SPEC-NULLABLE).
4. **Quy tắc theo chức năng** cho từng consumer ở (1): phiếu khảo sát / hành lang / `GET /work-orders/{id}/poles` / sự cố
   (`POST /faults` có cột; `fault.segment_id` lấy từ đâu khi cột null) / `fault_cluster` / thống kê / bản đồ / sync / citizen
   (`/public/…` QR → cột) / IoT-lighting (`affected_segment_ids`, `uncontrollable_pole_count`).
5. **Gán tuyến về sau.** Đường nào gán / đổi tuyến cho cột null (PUT, import lại, endpoint riêng?), bộ lọc "chưa gán tuyến"
   (tham số, quyền, phân trang), có cần nhãn nguồn kiểu `feeder_source` cho `segment_id` không (→ D-item, đừng tự thêm).
6. **Kế hoạch test.** Test mới theo từng quy tắc ở (4) (tên, trường hợp), test hiện có phải đổi kỳ vọng, và chốt chặn
   cần **phá thử** (`docs/agents/traps/testing.md`). Test import: `segment_external_ref` bỏ trống, `segment_external_ref`
   không khớp (vẫn phải lỗi dòng, KHÔNG rơi về null).
7. **Tài liệu & thông báo.** Mục `docs/contract-drift.md` cần ghi (SELF-SIGNED hay cần duyệt), trap nào cập nhật,
   `docs/templates/README.md`, `tracking.html`, ai phải được báo (WP5 / WP6 / WP4) và báo gì.
8. **D-item.** Mọi chỗ mơ hồ → `D-x` ở cuối file, kèm phương án + khuyến nghị. Không tự chọn.

Tiêu chí xong Phase 1:

- [ ] `.ai/results/POLE-SEGMENT-NULL-p1.md` có đủ 8 mục, bảng consumer phủ MỌI kết quả của lệnh
      `grep -rIn "SegmentId\|segment_id" src scripts mocks --include='*.cs' --include='*.sql' --include='*.py' --include='*.json'`
      (loại `Migrations/`, `bin/`, `obj/`) — chỗ bỏ qua phải nêu lý do.
- [ ] Không có thay đổi nào dưới `src/`, `docs/` (trừ file kết quả), `tests/`.

## KHÔNG ĐƯỢC làm

- Không sửa code, không tạo migration, không `dotnet ef`, không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json`
  / `docs/contract-drift.md` / traps / `tracking.html` ở Phase 1.
- Không chạy `dotnet test` và không chạm `luxmap_dev` (đọc schema từ migrations + snapshot).
- Không mở lại 4 quyết định đã chốt ở trên; không đề xuất tuyến giả "Chưa phân tuyến" hay sinh tuyến từ cột.
- Không làm phần tự gán tuyến theo khoảng cách / tuyến từ OSM — ngoài phạm vi ticket này.
- Không thêm / đổi giá trị enum đã publish; không `git push`; không `git add -A` / `git add .`.
- Không tự chọn phương án kiến trúc — ghi thành D-item.
- Không viết định danh cột / constraint từ trí nhớ; chép từ migration / snapshot.

## Phase 1 dừng ở đâu

Khảo sát xong thì **dừng hẳn**, commit đúng file kết quả (stage theo đường dẫn), chờ Mỹ chốt D-item. Không viết code ở
phase 1, kể cả khi đã rõ phải sửa gì. Claude review `.ai/results/POLE-SEGMENT-NULL-p1.md` trước khi trình Mỹ.
