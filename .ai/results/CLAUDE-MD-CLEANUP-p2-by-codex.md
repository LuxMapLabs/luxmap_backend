# CLAUDE-MD-CLEANUP — Phase 2, Codex (20/10/2026)

Phạm vi: chỉ writer Codex, nhánh `docs/CLAUDE-MD-CLEANUP`. Áp D-1…D-6 đã ký trong task; đối chiếu audit Phase 1 với core mới và bản cũ 2.293 dòng. Không sửa core `CLAUDE.md`, symlink `AGENTS.md`, Contract, drift, tracking; không commit/push. Không sửa ticket/review/result lịch sử. Hai file old/missing untracked có sẵn được giữ nguyên.

## S/C/D/W — từng finding

Mỗi dòng: ID → file → thay đổi. “Core/đã đúng” là finding đã được Claude xử lý trước bàn giao hoặc chỉ có ở core; không tự viết lại. Luật trùng chỉ rút thành con trỏ khi không có điều kiện/lý do/test riêng; các ví dụ bảo vệ hành vi được giữ.

- S-1 → `docs/agents/traps/survey.md` → Cập nhật D-R22/D-R23 đã chốt; D-R21/D-R24 vẫn nền tạm, dẫn core.
- S-2 → `docs/agents/traps/media-storage.md`; `docs/agents/traps/survey.md` → Bỏ video PLANNED/presigned/BE-15 chưa đặc tả; nêu proxy streaming, MP4 H.264/H.265, cap 300 MiB.
- S-3 → `CLAUDE.md` (core/đã đúng) → consumer máy; không sửa ở lượt này.
- S-4 → `CLAUDE.md` (core/đã đúng) → Contract v1.14; không sửa ở lượt này.
- S-5 → `CLAUDE.md` (core/đã đúng) → tóm tắt FW-00 và archive; không sửa ở lượt này.
- S-6 → `docs/agents/traps/survey.md` → Bỏ nền điện thoại làm đầu vào sweep; giữ LuxReading thủ công, phân biệt BH1750 theo phiên.
- S-7 → `CLAUDE.md` (core/đã đúng) → external_ref đã chốt; không sửa ở lượt này.
- S-8 → `CLAUDE.md` (core/đã đúng) → data_source đã có ở Contract; không sửa ở lượt này.
- S-9 → `docs/agents/traps/faults-workorders.md` → PATCH có severity; dẫn PatchFaultRequest hiện hành, không giữ schema ba trường cũ.
- S-10 → `docs/agents/traps/spatial-map.md` → Dẫn MapProperties/spec và nêu warranty_status; không giữ danh sách properties thiếu trường.
- S-11 → `CLAUDE.md` (core/đã đúng) → bảng task chưa đặc tả; không sửa ở lượt này.
- S-12 → `docs/agents/traps/commune-scope.md` → Bỏ số 14 entity còn lại và 15/16 entity neo phạm vi.
- S-13 → `docs/agents/traps/commune-scope.md` → last_sweep_id đã có FK Restrict tới SurveySweep.
- S-14 → `docs/agents/traps/media-storage.md` → Bốn bucket: survey/evidence/video/citizen; phân biệt stream và ảnh sanitize.
- S-15 → `docs/agents/traps/media-storage.md` → Capture config/track kiểm cấp phiên; bỏ đòi metadata trên từng JPEG.
- S-16 → `docs/agents/traps/media-storage.md` → Bỏ Hangfire/W12; thumbnail đồng bộ, scheduler DB lease.
- S-17 → `docs/agents/traps/media-storage.md` → Storage không provision lúc startup; không suy rộng sang worker DB.
- S-18 → `docs/agents/traps/survey.md` → Thay lý do CV đo sáng ảnh bằng phân biệt lux thủ công và history PeakLux/CvState/BaselineRatio.
- S-19 → `docs/agents/traps/survey.md` → nearest_luminance còn null vì nợ nối nguồn A/D-R23; bảng history đã có.
- S-20 → `docs/agents/traps/assets.md` → GET bản đồ là /api/v1/map/poles; /assets dành kiểm kê.
- S-21 → `docs/agents/traps/assets.md` → Thêm cabinets/cabinet_external_ref, bốn bảng external_ref, inventory emit nhưng map properties không emit; thứ tự năm file.
- S-22 → `docs/agents/traps/assets.md` → ImportResult có unchanged, total_warnings, warnings.
- S-23 → `docs/agents/traps/assets.md` → Bỏ capability chưa có endpoint và 4×6; giữ test literal, coverage máy có tên.
- S-24 → `docs/agents/traps/migrations.md` → Bỏ bảng ticket sắp tạo cột và cảnh báo confidence đang hở; nêu CHECK hữu hạn 0..1 hiện có.
- S-25 → `docs/agents/traps/migrations.md` → Bỏ đếm call site bulk; phân loại AuthService và test theo lý do/pragma.
- S-26 → `docs/agents/traps/testing.md`; `docs/agents/traps/assets.md` → DB riêng mỗi run, sequence dùng chung trong run; bỏ 16 class/DB dev chung.
- S-27 → `docs/agents/traps/faults-workorders.md` → WorkOrder/WorkOrderFault/WorkOrderSegment/Fault đã IAudited; hạ tầng audit hiện hành ở BE-23a.
- S-28 → `docs/agents/traps/faults-workorders.md` → Bỏ deviation commune_id chưa vào Contract; O-10 đã hợp nhất.
- S-29 → `docs/agents/traps/faults-workorders.md` → Bỏ hẹn BE-26 tính priority; theo P-1/BE-33/CV-16. Đồng bộ chú thích Fault.cs.
- S-30 → `docs/agents/traps/faults-workorders.md` → Seed đã ghi cluster; bỏ BE-13 chưa làm, dẫn tracking cho tiến độ pipeline.
- S-31 → `docs/agents/traps/faults-workorders.md` → Contract đã định nghĩa fault mở; bỏ pending drift 27 và dẫn core/FaultStatusSets.
- S-32 → `docs/agents/traps/spatial-map.md` → Fixture đã xoá fault/lux trước pole; giữ invariant teardown, không kể như lỗi hiện tại.
- S-33 → `docs/agents/traps/auth-accounts.md` → Bỏ register khỏi danh sách action; giữ nguyên tên test metadata còn tồn tại.
- S-34 → `docs/agents/traps/assets.md` → Mock có 3 node; NODE-047/detail-runtime là deviation cũ, không phải ngoại lệ mô hình.
- S-35 → `docs/agents/traps/assets.md` → Dùng seed_mock_set.py giữ ID; bỏ lời hẹn BE-39 còn phải seed.
- S-36 → `docs/agents/traps/assets.md` → --existing dùng lại external_ref khớp và không import cột cũ; ca mơ hồ vào review.
- S-37 → `CLAUDE.md` (core/đã đúng) → snapshot lịch/blocker; không sửa ở lượt này.
- S-38 → `CLAUDE.md` (core/đã đúng) → registry prefix; trap đã giữ đúng sáu cột của migration; không sửa ở lượt này.
- S-39 → `docs/agents/traps/faults-workorders.md` → Tách ActiveWorkOrdersAsync và ActiveRepairsAsync JOIN WO bỏ filter; scope neo bảng nối.
- S-40 → `docs/agents/traps/citizen.md` → Resolver/catch thuộc YoloSingleLampPhotoHinter; Citizen chỉ adapter đổi nhãn.
- S-41 → `docs/agents/traps/iot-lighting.md` → MQTT handler đang gọi LightingCommandService; bỏ thì tương lai.
- S-42 → `docs/agents/traps/iot-lighting.md` → DeviceEndpoints → MachineEndpoints.
- S-43 → `docs/agents/traps/survey.md` → Ba caller của YoloModel: survey, detect endpoint, hinter ảnh đơn.
- S-44 → `docs/agents/traps/migrations.md` → Bỏ mệnh đề chỉ mắt người bắt được; nêu test overflow/collision/catalog và giới hạn assertion.
- C-1 → `docs/agents/traps/migrations.md` → LPAD cắt đầu 1234 theo core; ID sai khác collision bị PK/23505 chặn.
- C-2 → `docs/agents/traps/media-storage.md` → Đổi xã/vai trò áp lúc cấp lại token, tối đa 60 phút; giữ lý do cấm presigned.
- C-3 → `CLAUDE.md` (core/đã đúng) → nguồn lamp fault và drift CLAUDE-MD-1 đã xử lý; không sửa Contract.
- C-4 → `docs/agents/traps/faults-workorders.md` → Sort mặc định -severity theo P-3; priority là sort chọn riêng. Sửa cả chú thích Fault.cs/FaultConfigurations.cs.
- C-5 → `docs/agents/traps/survey.md` → Ratio chỉ cho dim; CV OFF quyết out, ngưỡng null ở core; bỏ lý do history CV cũ.
- C-6 → `docs/agents/traps/spatial-map.md` → Current status từ quan sát công bố mới nhất: sweep hoặc ảnh cột, chưa có mới unknown.
- C-7 → `docs/agents/traps/commune-scope.md`; `docs/agents/traps/survey.md` → Cascade dữ liệu theo xã khác ngoại lệ token/sync; bỏ cụm bảng cascade thứ ba.
- C-8 → `docs/agents/traps/media-storage.md` → Original nguyên byte giới hạn theo luồng; Citizen sanitize là ngoại lệ.
- C-9 → `docs/agents/traps/assets.md`; `docs/agents/traps/iot-lighting.md` → Coverage capability có ngoại lệ máy MachineEndpoints; không khẳng định cấm mọi policy ngoài Matrix.
- C-10 → `docs/agents/traps/migrations.md` → Bỏ confidence đang hở, giữ CHECK hiện hành như assets.
- C-11 → `docs/agents/traps/faults-workorders.md` → Ví dụ chuyển hợp lệ detected→confirmed→in_progress→resolved.
- C-12 → `docs/agents/traps/survey.md` → Lux thủ công so giữa cột/đêm; dim theo baseline riêng cột, giữ quy trình/lý do sai số chung.
- C-13 → `.ai/README.md`; `.ai/context/sources.md`; `docs/agents/domain.md` → Con trỏ thứ tự ưu tiên về core đã ký D-4; bỏ bản chép thứ tự/version cũ.
- C-14 → `docs/agents/traps/deploy-ops.md` → Evidence đọc body không chứng minh mọi multipart an toàn; pole-photo early-413 cần guard 17 MiB riêng.
- D-1 → `.ai/README.md` → Giữ bảng đúng tầng và dẫn core; không giữ bản thứ tự ưu tiên riêng.
- D-2 → `docs/agents/traps/survey.md` → Nền v1.4 dẫn core, chỉ giữ hệ quả LuxReading/history.
- D-3 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-4 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-5 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-6 → `docs/agents/traps/migrations.md` → Giữ chi tiết thứ tự CREATE FUNCTION riêng, DEFAULT dẫn core; bản trap đã đúng khi nhận.
- D-7 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-8 → `docs/agents/traps/spatial-map.md` → Giữ điều kiện ST_Transform chỉ trong SQL và phép chiếu; không chép anti-pattern thứ ba.
- D-9 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-10 → `docs/agents/traps/assets.md`; `docs/agents/traps/spatial-map.md` → Assets dẫn giao diện map ở core; giữ riêng plan/index và test SQL EF.
- D-11 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-12 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-13 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-14 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-15 → `docs/agents/traps/commune-scope.md`; `docs/agents/traps/assets.md` → Ngoại lệ Feeder key dẫn O-7; giữ lý do không bắt InvalidOperationException rộng và tên test.
- D-16 → `docs/agents/traps/commune-scope.md` → Một kết luận benchmark, phần đầu dẫn cuối; bỏ bảng đo.
- D-17 → `docs/agents/traps/media-storage.md` → Thumbnail metadata chỉ giải thích/test đầy đủ ở mục ImageSharp, phần original dẫn lại.
- D-18 → `docs/agents/traps/survey.md`; `docs/agents/traps/media-storage.md` → AI dẫn decode chung; Citizen giữ riêng pipeline sanitize và giới hạn pixel.
- D-19 → `docs/agents/traps/assets.md` → Hai đoạn nguồn commune/BE-REVIEW-02 dẫn O-7; giữ nguồn body và fixture từ pole.
- D-20 → `docs/agents/traps/faults-workorders.md` → Audit chung tại BE-23a, fault chỉ giữ yêu cầu riêng; tập mở dẫn core, giữ test định nghĩa/dữ liệu và lý do.
- D-21 → `docs/agents/traps/spatial-map.md`; `docs/agents/traps/testing.md` → Sequence chọn dải live/try-finally ở testing, map dẫn lại; bỏ nhật ký chạy lặp.
- D-22 → `CLAUDE.md` (core/đã đúng) → các range của finding chỉ nằm ở core; không thêm bản sao trong trap.
- D-23 → `docs/agents/traps/assets.md`; `docs/agents/traps/iot-lighting.md` → Mục CSV dẫn rule schema viết tay tại BE-14b; giữ lý do CI tự khớp vẫn xanh.
- D-24 → `docs/agents/traps/citizen.md`; `docs/agents/traps/survey.md` → Citizen dẫn AI-1 về giới hạn test accuracy/WP4.
- D-25 → `docs/agents/traps/iot-lighting.md`; `docs/agents/traps/faults-workorders.md` → MQTT dẫn core validation service, giữ ví dụ AckAsync; Sync giữ chi tiết adapter/RejectServerOwnedFields.
- D-26 → `docs/agents/traps/iot-lighting.md` → Lệnh thiết bị dẫn principal ở 2a; không lặp lý do cùng xã chưa đủ.
- D-27 → `docs/agents/traps/iot-lighting.md` → Coverage máy gom LC-12/core; 2b giữ yêu cầu OpenAPI Device.
- D-28 → `docs/agents/traps/auth-accounts.md`; `docs/agents/traps/iot-lighting.md` → Nguyên nhân Response.Clear dẫn core; giữ Retry-After và challenge Device trần, không giữ lỗi JWT chi tiết.
- D-29 → `docs/agents/traps/assets.md`; `docs/agents/traps/survey.md` → MATCH SIMPLE chung tại BE-15 P2c; O-7 giữ hậu quả feeder nullable/MATCH FULL.
- D-30 → `docs/agents/traps/iot-lighting.md` → Giữ khuôn test/poll, dẫn luật FOR NO KEY UPDATE ở core; Citizen giữ tên regression theo luồng.
- D-31 → `docs/agents/traps/citizen.md` → Luật code/qr_id tại P2a; public endpoint dẫn lại, body vẫn bắt buộc.
- D-32 → `docs/agents/traps/migrations.md`; `docs/agents/traps/citizen.md`; `docs/agents/traps/faults-workorders.md`; `docs/agents/traps/iot-lighting.md`; `docs/agents/traps/assets.md` → Một nguyên lý rollback CHECK + bảng bảy migration; từng luồng dẫn bảng, giữ điều kiện giá trị cụ thể.
- W-1 → `docs/agents/traps/commune-scope.md`; `docs/agents/traps/assets.md`; `docs/agents/traps/migrations.md`; `docs/agents/traps/media-storage.md`; `docs/agents/traps/faults-workorders.md` → Bỏ deadline/caller đầu tiên/ticket sắp làm/capability chưa dùng; trạng thái dẫn tracking, không sửa tracking.
- W-2 → `docs/agents/traps/assets.md`; `docs/agents/traps/media-storage.md`; `docs/agents/traps/survey.md`; `docs/agents/traps/faults-workorders.md` → Bỏ bản sao drift đã đóng hoặc lý do cũ; giữ con trỏ decision/deviation hiện hành.
- W-3 → `CLAUDE.md` (core/đã đúng) → đã bỏ lịch sử bản AGENTS cũ, symlink giữ nguyên.
- W-4 → `docs/agents/traps/commune-scope.md` → Bỏ warm-up/cặp/IQR/µs và bảng lượt; giữ không dùng số cũ nới guard, cách đo/thành phần và tên benchmark.
- W-5 → `docs/agents/traps/spatial-map.md` → Bỏ bảng EXPLAIN/rows/buffers/offset/ppm cụ thể; giữ bbox→distance, selectivity join, SRID, LengthM và test.
- W-6 → `docs/agents/traps/assets.md`; `docs/agents/traps/commune-scope.md`; `docs/agents/traps/spatial-map.md`; `docs/agents/traps/testing.md`; `docs/agents/traps/migrations.md`; `docs/agents/traps/faults-workorders.md` → Bỏ số timestamp/test/rác/sequence dev/lượt chạy; giữ invariant, giới hạn dữ liệu và test canh.
- W-7 → `docs/agents/traps/media-storage.md` → Bỏ registry 404/401 theo ngày/cache máy; giữ pin manifest-list digest đa kiến trúc.
- W-8 → `docs/agents/traps/media-storage.md` → Bỏ KiB/SHA vòng tròn kiểm tay; FakeS3 không canh MinIO thật, tiến độ dẫn tracking.
- W-9 → `docs/agents/traps/survey.md` → Giữ RecordLuxReading/ReadLuxReadings hiện tại, lịch sử quyền chỉ dẫn D-R5.
- W-10 → `docs/agents/traps/faults-workorders.md` → Bỏ inventory stub/schema đã xoá; giữ object? nullable thiếu type và lint fix.
- W-11 → `docs/agents/traps/survey.md`; `docs/agents/traps/auth-accounts.md`; `docs/agents/traps/citizen.md`; `docs/agents/traps/assets.md`; `docs/agents/traps/deploy-ops.md` → Bỏ HEVC148s/Mac-Linux cùng phút/3-3/11 test/120MB/10-10; giữ failure mode, cap cấu hình và tên regression.

## Completeness — 285 dòng không còn nguyên văn

Đã đọc từng dòng/run trong missing.txt và đối chiếu bản old. Bảng dưới chỉ đếm dòng có trong missing.txt nằm trong mỗi khoảng; các khoảng rời nhau, tổng phải bằng 285. Payload/schema có nguồn Contract/DTO/spec thì dẫn nguồn, không đóng băng lại danh sách cũ (S-9/S-10).

| Dòng cũ | Số dòng | Đích / kết luận | Kiểm quy tắc |
|---|---:|---|---|
| 3–23 | 19 | Core mở đầu | LOST-9 phục hồi BH1750FVI/đầu xe máy theo quy trình; nền/đề tài/consumer; trạng thái D-R đã sửa S-1…S-6. |
| 27–35 | 6 | Core Nguồn sự thật + Enum | Thứ tự D-4; manual→field_report là lịch sử. |
| 41–66 | 18 | Core Ghi phát hiện vào ĐÚNG tầng | Giữ bốn đích ghi, approve/escalate, SELF-SIGNED. |
| 72–91 | 16 | Core Giao thức .ai | Giữ đọc/ghi ticket và symlink; bỏ log bản sao cũ W-3. |
| 97–120 | 18 | Core Phạm vi + survey BE-42 | Nền điện thoại/controlled-reference/pending cũ là S-1/S-6/S-7; BH1750 là đầu vào, D-R23 đã chốt. |
| 122–136 | 7 | Core data_source + assets Testbed | Luật tách nguồn/không emit map giữ; LOST-1 phục hồi testbed là RoadSegment. |
| 142–166 | 25 | Core Quy ước API + migrations | Wire/date/ID/order/regex/LPAD; LOST-4 phục hồi lọc khoảng text; số đo timestamp bỏ W-6. |
| 170–179 | 7 | Core Quy ước API + spatial BE-10 | GeoJSON phẳng, lng/lat, properties ID, 4326/3405 giữ. |
| 192–214 | 14 | Core Enum + migrations rollback | Alias/các số lượng backfill là lịch sử; giữ chuyển dữ liệu trước CHECK và Down không hoàn nguyên solar. |
| 216–222 | 5 | Core Enum + iot BE-14b | Nguồn fault/state machine/unknown giữ; LOST-8 phục hồi danh sách quan hệ tính lúc đọc. |
| 228–245 | 16 | Core API + spatial/faults/survey | S-9 cập nhật qua DTO/spec; C-6 trạng thái cả hai nguồn; LOST-6 giữ đường ảnh/đúng một cha. |
| 250–269 | 9 | Core API + spatial BE-20 | Properties dẫn MapProperties/spec có warranty_status; LOST-5 giữ null/empty/cap/chiều/order; ratio/backend và out null theo D-R22. |
| 275–299 | 20 | Core API + assets BE-31 | Luật đặc tả trước/retention/nullable warranty còn; bảng task/deadline cũ bỏ S-11/W-1. |
| 305–320 | 13 | Core Mô hình dữ liệu + commune BE-09 | Inventory/count là S-12; giữ ERD/Q-x, Pole/Fixture, ảnh riêng và quyền ghi current-status. |
| 1351–1365 | 11 | Core Vai trò + auth-accounts | Vai trò/capability/seed/invite giữ; LOST-2 phục hồi nhiều xã gán, không cấp huyện. |
| 1367–1376 | 9 | Core Vai trò + citizen/iot | QR hàng chờ, không citizen role, testbed control/audit giữ; alias role cũ là lịch sử. |
| 1384–1386 | 3 | Core Quy tắc dễ sai + survey BE-42 | Kiểm cấu hình cấp phiên; D-6 tách lux thủ công và dim baseline riêng. |
| 1388–1395 | 7 | Core Quy tắc dễ sai + iot/faults | Model/artifact/audit/P-R riêng/topology/cluster/idempotent/offline giữ; LOST-3 phục hồi firmware. |
| 1401–1415 | 12 | Core Stack và database | Hangfire stale đã sửa; UTC/index/500ms/enum/transaction giữ. |
| 1421–1432 | 11 | Core trỏ tracking | Bảng lịch/ai chờ là tiến độ W-1/S-37, không phải luật cần phục hồi. |
| 1458–1473 | 16 | Core Quy ước/API + faults BE-18 | Các anti-pattern đã có nguồn một chỗ; LOST-7 phục hồi không hard-code trọng số ưu tiên. |
| 1479–1485 | 7 | Core Quy ước làm việc | Stage tường minh/task prefix/correlation/PostGIS giữ; log nuốt untracked là lịch sử. |
| 1489–1491 | 2 | Core trỏ tracking | Snapshot sprint cũ W-1/S-37 bỏ. |
| 1493–1497 | 3 | Core Quy ước API + migrations | DEFAULT/EF ValueGenerated/sequence gap/client_op_id giữ; commit LPAD là lịch sử. |
| 1499–1501 | 2 | migrations luxmap_format_id | Giữ function đứng trước DEFAULT/sáu cột lịch sử/chữ ký Down; inventory 16/10 là S-38. |
| 1503 | 1 | Core trỏ tracking | BE-40/41/43 đang chặn là snapshot S-37. |
| 1619–1623 | 5 | Core Enum + faults BE-18 | Giữ CASE/Expression và test Default_order_is_severity_by_rank_not_alphabet_then_oldest_first. |
| 2042 | 1 | Core Agent skills | Ticket file/issue-tracker giữ. |
| 2046 | 1 | Core Agent skills | status/owner/triage-labels giữ. |
| 2050 | 1 | Core Agent skills | CONTEXT/domain, không docs/adr giữ. |
| **Tổng** | **285** | **Không còn dòng chưa phân loại** | |

### LOST đã phục hồi đúng tầng

| Mã | Dòng cũ | Quy tắc | Phục hồi |
|---|---|---|---|
| LOST-1 | 136 | Testbed đăng ký RoadSegment thật, dùng cùng pipeline | `docs/agents/traps/assets.md` |
| LOST-2 | 1360–1361 | Cấp giám sát nhiều xã theo danh sách, không cấp huyện | `docs/agents/traps/auth-accounts.md` |
| LOST-3 | 1388 | Phát hiện IoT truy vết phiên bản firmware | `docs/agents/traps/iot-lighting.md` |
| LOST-4 | 149 | Không lọc khoảng bằng ID text trần qua ngưỡng độ rộng | `docs/agents/traps/migrations.md` |
| LOST-5 | 234 | Detail: fixture null, iot null, runtime rỗng, baseline theo chiều, history ≤30 cũ→mới | `docs/agents/traps/spatial-map.md` |
| LOST-6 | 238 | Không photo_frame_id ở POST fault; ảnh endpoint riêng và evidence đúng một cha | `docs/agents/traps/faults-workorders.md` |
| LOST-7 | 1468 | Không hard-code trọng số ưu tiên; xét tính lại cột lưu khi đổi trọng số | `docs/agents/traps/faults-workorders.md` |
| LOST-8 | 219, 259–260 | Danh sách controller/segment/feeder tính lúc đọc, không lưu bản sao | `docs/agents/traps/iot-lighting.md` |
| LOST-9 | 11 | BH1750FVI gắn đầu xe máy, giữ bố trí theo quy trình khảo sát | `docs/agents/traps/survey.md` |

Không còn LOST chưa phục hồi. Không cần sửa core: các luật thiếu thuộc một vùng; các luật áp mọi ticket đã có trong core.

## Con trỏ cập nhật (file:line sau sửa)

Đã grep src/, tests/, docs/, scripts/, README.md, .ai/README.md, template và .ai/context/. Các tham chiếu còn ở core dùng tên mục hiện hành (Quy ước API…). Các tài liệu archive/review/task/result lịch sử không sửa. Chuỗi output runtime của benchmark không sửa vì giới hạn .cs chỉ comment.

- `.ai/README.md:8` · `.ai/README.md:9` · `.ai/README.md:20`
- `.ai/context/commands.md:98`
- `.ai/context/sources.md:8` · `.ai/context/sources.md:9` · `.ai/context/sources.md:11` · `.ai/context/sources.md:29`
- `.ai/tasks/_TEMPLATE.md:20`
- `README.md:10`
- `docs/agents/domain.md:7` · `docs/agents/domain.md:8` · `docs/agents/domain.md:19`
- `docs/agents/issue-tracker.md:29`
- `docs/agents/traps/assets.md:9` · `docs/agents/traps/assets.md:50` · `docs/agents/traps/assets.md:99` · `docs/agents/traps/assets.md:198` · `docs/agents/traps/assets.md:213` · `docs/agents/traps/assets.md:253` · `docs/agents/traps/assets.md:258` · `docs/agents/traps/assets.md:338` · `docs/agents/traps/assets.md:391` · `docs/agents/traps/assets.md:415`
- `docs/agents/traps/auth-accounts.md:69`
- `docs/agents/traps/citizen.md:21` · `docs/agents/traps/citizen.md:56` · `docs/agents/traps/citizen.md:78` · `docs/agents/traps/citizen.md:80`
- `docs/agents/traps/commune-scope.md:37`
- `docs/agents/traps/faults-workorders.md:34` · `docs/agents/traps/faults-workorders.md:56` · `docs/agents/traps/faults-workorders.md:61` · `docs/agents/traps/faults-workorders.md:181` · `docs/agents/traps/faults-workorders.md:277`
- `docs/agents/traps/iot-lighting.md:40` · `docs/agents/traps/iot-lighting.md:50` · `docs/agents/traps/iot-lighting.md:70` · `docs/agents/traps/iot-lighting.md:76` · `docs/agents/traps/iot-lighting.md:80` · `docs/agents/traps/iot-lighting.md:89`
- `docs/agents/traps/media-storage.md:43` · `docs/agents/traps/media-storage.md:56` · `docs/agents/traps/media-storage.md:67` · `docs/agents/traps/media-storage.md:74`
- `docs/agents/traps/migrations.md:125`
- `docs/agents/traps/spatial-map.md:74` · `docs/agents/traps/spatial-map.md:78`
- `docs/agents/traps/survey.md:9` · `docs/agents/traps/survey.md:22` · `docs/agents/traps/survey.md:48` · `docs/agents/traps/survey.md:106`
- `docs/development.md:457`
- `docs/openapi/tools/gen_consolidated_spec.py:406` · `docs/openapi/tools/gen_consolidated_spec.py:410`
- `scripts/seed_mock_set.py:27`
- `src/LuxMap.Api/Http/IsoDateQueryBinding.cs:20`
- `src/LuxMap.Infrastructure.Storage/ImagePipeline.cs:52`
- `src/LuxMap.Modules.Admin/Communes/CommuneAdminService.cs:32`
- `src/LuxMap.Modules.Assets/Crud/AssetCrudService.cs:1048`
- `src/LuxMap.Modules.Assets/Import/AssetImportService.cs:386`
- `src/LuxMap.Modules.Citizen/Configurations/QrCodeConfiguration.cs:38`
- `src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:18` · `src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:134` · `src/LuxMap.Modules.Faults/Configurations/FaultConfigurations.cs:172`
- `src/LuxMap.Modules.Faults/Entities/Fault.cs:120`
- `src/LuxMap.Modules.Faults/FaultQueryService.cs:169`
- `src/LuxMap.Modules.Identity/Accounts/PasswordController.cs:13`
- `src/LuxMap.Modules.Identity/Accounts/UserAdminService.cs:94`
- `src/LuxMap.Modules.Identity/Auth/AuthService.cs:141`
- `src/LuxMap.Modules.Map/Features/MapQueryService.cs:263` · `src/LuxMap.Modules.Map/Features/MapQueryService.cs:536`
- `src/LuxMap.Modules.Notifications/Configurations/NotificationConfiguration.cs:35`
- `src/LuxMap.Modules.Survey/Configurations/LuxReadingConfiguration.cs:76`
- `src/LuxMap.Modules.WorkOrders/WorkOrderAgenda.cs:145`
- `src/LuxMap.Persistence/Migrations/20261007115020_AddElectricalCabinet.cs:219`
- `src/LuxMap.Shared/Storage/ObjectStorage.cs:42`
- `tests/LuxMap.Api.Tests/CabinetTests.cs:128` · `tests/LuxMap.Api.Tests/CabinetTests.cs:594`
- `tests/LuxMap.Api.Tests/DeviceProbeController.cs:17`
- `tests/LuxMap.Api.Tests/FaultListTests.cs:97` · `tests/LuxMap.Api.Tests/FaultListTests.cs:146`
- `tests/LuxMap.Api.Tests/FaultSchemaTests.cs:258`
- `tests/LuxMap.Api.Tests/FeederCommuneConstraintTests.cs:274`
- `tests/LuxMap.Api.Tests/LuxReadingTests.cs:507`
- `tests/LuxMap.Api.Tests/MapEndpointTests.cs:185` · `tests/LuxMap.Api.Tests/MapEndpointTests.cs:315` · `tests/LuxMap.Api.Tests/MapEndpointTests.cs:457`
- `tests/LuxMap.Api.Tests/PrefixedIdOrderingTests.cs:27`
- `tests/LuxMap.Api.Tests/PrefixedIdOverflowTests.cs:271`
- `tests/LuxMap.Api.Tests/StatisticsTests.cs:144`
- `tests/LuxMap.Api.Tests/SyncTests.cs:371`

## Bằng chứng và kiểm tra

Bằng chứng kỹ thuật theo finding trong audit Phase 1, đã đọc lại nguồn hiện hành: StorageKeys/ObjectStorage, ImportKindExtensions/ImportResult, FaultSort/PatchFaultRequest, ActiveWorkOrderLookup, YoloSingleLampPhotoHinter, SurveyRawParser, PoleDetailService/MapProperties, cấu hình confidence/ingest và các Down dựng CHECK cũ. Đây là sửa mô tả theo code/quyết định, không đổi hành vi/API/schema.

Kiểm tự động bằng Python trên diff: bỏ dòng comment // của mỗi file C# rồi so trước/sau; kiểm symlink, file cấm, đường dẫn cụ thể. Output thật:

```text
PASS: 29 C# files changed comments only; executable lines unchanged.
PASS: core, symlink, tracking, Contract/drift and historical files preserved.
Concrete path references checked: 36
Missing: []
```

`git diff --check`: exit 0, không output. Các đường dẫn file:line trong bảng con trỏ được kiểm tồn tại và nằm trong số dòng file. Không chạy test vì chỉ tài liệu/comment; đã build toàn solution, gồm các project test.

### Build

Lượt restore mạng bị sandbox DNS chặn (`NU1301`, api.nuget.org). Dùng cache package hiện có, CLI home tạm và một MSBuild node; không sửa csproj/NuGet config. Lệnh build cuối:

```bash
DOTNET_CLI_HOME=/private/tmp/luxmap-cleanup-dotnet \
NUGET_PACKAGES=/Users/nhm809/.nuget/packages \
MSBUILDDISABLENODEREUSE=1 \
dotnet build --disable-build-servers -m:1 --source /Users/nhm809/.nuget/packages
```

Exit code: **0**. Output thật (lượt cuối):

```text
  Determining projects to restore...
CSSM_ModuleLoad(): One or more parameters passed to a function were not valid.
  All projects are up-to-date for restore.
  LuxMap.Shared -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Shared/bin/Debug/net10.0/LuxMap.Shared.dll
  LuxMap.Persistence -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Persistence/bin/Debug/net10.0/LuxMap.Persistence.dll
  LuxMap.Modules.Identity -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Identity/bin/Debug/net10.0/LuxMap.Modules.Identity.dll
  LuxMap.Modules.Assets -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Assets/bin/Debug/net10.0/LuxMap.Modules.Assets.dll
  LuxMap.Modules.Notifications -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Notifications/bin/Debug/net10.0/LuxMap.Modules.Notifications.dll
  LuxMap.Modules.Faults -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Faults/bin/Debug/net10.0/LuxMap.Modules.Faults.dll
  LuxMap.Modules.WorkOrders -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.WorkOrders/bin/Debug/net10.0/LuxMap.Modules.WorkOrders.dll
  LuxMap.Modules.Survey -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Survey/bin/Debug/net10.0/LuxMap.Modules.Survey.dll
  LuxMap.Modules.Telemetry -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Telemetry/bin/Debug/net10.0/LuxMap.Modules.Telemetry.dll
  LuxMap.Modules.Map -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Map/bin/Debug/net10.0/LuxMap.Modules.Map.dll
  LuxMap.Modules.Sync -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Sync/bin/Debug/net10.0/LuxMap.Modules.Sync.dll
  LuxMap.Modules.Statistics -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Statistics/bin/Debug/net10.0/LuxMap.Modules.Statistics.dll
  LuxMap.Modules.Admin -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Admin/bin/Debug/net10.0/LuxMap.Modules.Admin.dll
  LuxMap.Infrastructure.Storage -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Infrastructure.Storage/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.dll
  LuxMap.Modules.AI -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.AI/bin/Debug/net10.0/LuxMap.Modules.AI.dll
  LuxMap.Modules.Citizen -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Modules.Citizen/bin/Debug/net10.0/LuxMap.Modules.Citizen.dll
  LuxMap.Api -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll
  LuxMap.Api.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/tests/LuxMap.Api.Tests/bin/Debug/net10.0/LuxMap.Api.Tests.dll
  LuxMap.Infrastructure.Storage.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/tests/LuxMap.Infrastructure.Storage.Tests/bin/Debug/net10.0/LuxMap.Infrastructure.Storage.Tests.dll
  LuxMap.Persistence.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/tests/LuxMap.Persistence.Tests/bin/Debug/net10.0/LuxMap.Persistence.Tests.dll
  LuxMap.Shared.Tests -> /Users/nhm809/Documents/LuxMap/luxmap_backend-claudemd-review/tests/LuxMap.Shared.Tests/bin/Debug/net10.0/LuxMap.Shared.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.84
```

✅ Verified: completeness 285/285, 9 LOST phục hồi, con trỏ/đường dẫn, comment-only C#, diff và build. ⚠️ Không tái chạy benchmark/diễn tập/S3/model accuracy; đó là log cũ đã bỏ, không dùng làm bằng chứng mới. 📌 Nền SELF-SIGNED, D-R25/D-R27 và nợ nghiệp vụ tiếp tục theo core/drift/tracking, không tự chốt ở lượt này.
Bản CLAUDE.md cũ (2.293 dòng) không lưu ở đây: `git show d380c1c:CLAUDE.md`.
