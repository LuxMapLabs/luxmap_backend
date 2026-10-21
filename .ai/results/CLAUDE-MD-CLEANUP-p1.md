# CLAUDE-MD-CLEANUP — Phase 1, phần cấu trúc (Claude)

Nền: `origin/dev` @ `a9a85df`. `CLAUDE.md` = 2.293 dòng / 204 KB, nạp vào MỌI phiên của Claude và Codex (`AGENTS.md` là symlink).
Mỹ chốt 20/10/2026: **tách lõi + tra cứu**, không xoá luật nào; sửa file **sau khi** PR #161 và `feat/PHOTO-REVIEW` merge.

## Chẩn đoán

1. **Sprawl.** ~75% dung lượng là bẫy theo ticket (BE-09 … POLE-PHOTO-AI) — mỗi phiên chỉ cần 1–2 vùng trong đó.
2. **Lồng sai.** ~40 mục ticket (dòng 1505–2031) nằm DƯỚI `## Thứ tự hiện tại`; CABINET → POLE-PHOTO-AI nằm DƯỚI `## Agent skills`;
   `### Vai trò` nằm dưới `## Domain model`. Mục lục không còn phản ánh nội dung.
3. **Bẫy chung chôn trong mục ticket.** Luật áp cho MỌI ticket nhưng chỉ nằm trong mục của ticket đã sinh ra nó, nên agent
   làm ticket khác không gặp: `UtcMicrosecondClock` (BE-23), mỗi `SaveChanges` đúng một audit (BE-23a/BE-19), `HasDatabaseName`
   cho partial index (BE-23), tên Postgres 63 ký tự (P2b), một schemaId Swagger một kiểu (BE-20), hai `ProducesResponseType`
   cùng mã (BE-31), body JSON là `sealed class` không positional record (POLE-PHOTO-AI), `Response.Clear()` xoá header
   (BE-33a **và** LIGHT-CTRL 2a — trùng), khoá hàng bằng `SELECT 1 … FOR UPDATE` không `SELECT *` (BE-15 P2a), test khoá dùng
   `FOR NO KEY UPDATE` (LIGHT-CTRL 2b), đọc exit code `dotnet test` (BE-17), `cp` không `mv` khi khôi phục phá thử (BE-36),
   `--no-build` sau đổi branch (BE-36), port giữa module thay vì project reference (BE-40, CABINET, POLE-LOCATION, WO-SURVEY-CHAIN).
4. **Lịch sử / tiến độ lẫn vào luật** (sai tầng theo chính bảng "Ghi đúng tầng"): `## Thứ tự hiện tại`, `## Ai đang chờ endpoint`,
   bảng `## Contract phủ tới đâu` (gạch ngang, "đã hiện thực"), 4 lượt đo A/B guard, đoạn solar 45 hàng, `AGENTS.md` cũ 17/09.
5. **Đặc tả chép lại** (cache của Contract/OpenAPI): bảng endpoint, danh sách `properties` — đáng giữ phần *luật* (bbox bắt buộc,
   `/map/` prefix, không `feature.id`), không đáng giữ phần *hình dạng* (spec là nguồn).

## Cấu trúc đích

### `CLAUDE.md` — lõi (mục tiêu 600–800 dòng)

```
# LuxMap — Backend (WP2)                 3–5 dòng: đề tài, consumer, Phiếu v1.4 (1 đoạn ngắn + trỏ registration/drift)
## Nguồn sự thật & ghi đúng tầng         gộp 2 mục hiện có + giao thức .ai/ (rút gọn)
## Phạm vi và data_source                luật tách data_source, testbed ≠ field, source_channel ≠ data_source (bỏ bảng lịch sử v1.2)
## Quy ước API (Contract mục 0)          giữ nguyên ý: snake_case, enum chữ, UTC/µs, ngày, ID tối thiểu N + khuôn sắp, lỗi, phân trang,
                                         GeoJSON, toạ độ 4326/3405
## Enum                                  khối enum + 4 ràng buộc nghiệp vụ (unknown ≠ out, nguồn fault_type, luồng fault_status, IoT ở tủ)
## Vai trò và phân quyền                 4 vai trò, capability matrix (1 nguồn), fallback, capability rỗng = mở, test literal,
                                         [AllowAnonymous] cấp class, 403 hai mã, Citizen không tài khoản, ON/OFF chỉ testbed
## Phạm vi xã (commune scope)            ICommuneScoped khi nào, HasQueryFilter chỉ đọc + CommuneWriteGuard, Narrow, backdoor,
                                         vùng mù cascade, FK Restrict mặc định, AdministrativeUnit không lọc
## Bẫy chung (mọi ticket)                một dòng mỗi bẫy + trỏ file chi tiết: migration đọc trước, partial index, ExecuteUpdate cấm,
                                         double NaN, enum text ORDER BY, khoảng cách DistanceMeters + bbox trước, ảnh proxy + object
                                         trước row, µs clock, một audit/SaveChanges, khoá hàng, port giữa module, Swagger schemaId…
## Test                                  BE-36 test host, AssetDatabaseCollection, không literal ID, culture, tài khoản riêng, teardown
                                         theo Restrict, exit code, phá thử, --no-build
## Quy tắc dễ sai âm thầm                giữ nguyên (đây là lõi nghiên cứu)
## Stack                                 sửa lỗi thời (Hangfire → BE-26 lease DB…)
## Quy ước làm việc                      branch, stage tường minh, mã task, client_op_id
## Tra cứu theo vùng                     bảng con trỏ (dưới)
```

### `docs/agents/traps/*.md` — tra cứu (chuyển NGUYÊN VĂN, chỉ sửa lỗi thời Codex tìm ra)

| File | Nhận các mục hiện có | Câu trỏ trong lõi ("đọc trước khi …") |
|---|---|---|
| `assets.md` | BE-09 (Pole/Fixture, pole_current_status), BE-12a, O-7, BE-REVIEW-02 #1/#3/#4, POLE-NOTE, N-4 `updated_by`, TOPO-INFER, CABINET, POLE-LOCATION, COMMUNE-BBOX, BE-31, bộ mock FO-26 + `external_ref` | sửa tài sản, import, topology, tủ điện, xã |
| `spatial-map.md` | BE-10, BE-14, BE-20, BE-17 độ phủ | truy vấn không gian, bản đồ, chi tiết cột |
| `media-storage.md` | BE-11 (6 quy tắc + ImageSharp + MinIO image) | lưu / phục vụ ảnh, video, thumbnail |
| `survey.md` | BE-42, BE-15 P2a/ffprobe/P2c, AI-1, POLE-PHOTO-AI (+ PHOTO-REVIEW) | khảo sát, lux, model AI, ảnh cột |
| `faults-workorders.md` | BE-18, BE-19, BE-23, BE-23a, BE-40, FR-2 (+ WO-SURVEY-CHAIN), BE-27, BE-26, BE-43 | sự cố, phiếu, audit, thông báo, job nền, sync |
| `auth-accounts.md` | BE-33a, LOGIN-RATE, forwarded headers | tài khoản, đăng nhập, mail, giới hạn tần suất |
| `citizen.md` | CITIZEN-QR P2a/P2b/P2c | QR, form công khai, hàng chờ duyệt |
| `iot-lighting.md` | BE-14b, LIGHT-CTRL 2a/2b, LC-12 | thiết bị, điều khiển đèn, MQTT |
| `testing.md` | AssetDatabaseCollection + literal ID, REG-v1.2, BE-14 #3/#4, BE-36, FakeTimeProvider, chi phí guard (lịch sử đo) | viết / sửa test, fixture, teardown |
| `migrations.md` | đọc migration, partial index, CREATE FUNCTION trước DEFAULT, xmin, HasDatabaseName, 63 ký tự, DB-only constraint, rollback có audit | tạo / sửa migration |
| `deploy-ops.md` | OPS-DEPLOY-VPS, Supabase `PLAN`, CI host config | Docker, deploy, script chép dữ liệu, CI |

`docs/agents/` đã tồn tại (issue-tracker, triage-labels, domain) — cùng chỗ, cùng khuôn con trỏ.

## Bỏ khỏi lõi (không chuyển)

- `## Thứ tự hiện tại`, `## Ai đang chờ endpoint` → tiến độ, thuộc `tracking.html` (giữ lại đúng 2 luật nằm lẫn trong đó:
  ID sinh ở DB / client không sinh ID hiển thị; `CREATE FUNCTION` trước migration dùng nó → `migrations.md`).
- Bảng `## Contract phủ tới đâu` → giữ 1 câu luật ("task chưa đặc tả: đặc tả trước khi hiện thực") + D-R16 lưu trữ.
- Đoạn `AGENTS.md` cũ 17/09, chi tiết solar 45 hàng, 4 lượt đo A/B → lịch sử; số đo giữ trong `testing.md` mục guard (1 đoạn).
- Bảng endpoint + `properties` → chỉ giữ luật; hình dạng tra `docs/openapi/luxmap-v1.5.json` / Contract.

## Rủi ro và cách giữ

- **Con trỏ yếu = luật không bao giờ được đọc.** Mỗi con trỏ mở đầu bằng *vùng code* (tên module/thư mục), vì agent biết mình đang
  sửa file nào; Codex cũng thấy chúng qua `AGENTS.md`.
- **Mất chữ khi chuyển.** Chuyển bằng script (cắt theo dòng), so tổng: mọi dòng cũ phải có ở lõi hoặc đúng một file tra cứu, trừ
  danh sách xoá có chủ đích ở trên → diff kiểm được.
- **Test nêu tên mục CLAUDE.md** (vd. XML doc / test message trỏ "CLAUDE.md mục X") → grep và sửa con trỏ cùng PR.
- **Đo được:** 54 lần nhắc `CLAUDE.md` trong comment `src/` + `tests/` (16 file test, vd. `PrefixedIdOverflowTests`, `CommuneWriteGuardCostTests`,
  `CabinetTests`), cộng `docs/development.md`, `docs/agents/*.md`, `docs/database/erd.md`, `README.md`, `gen_consolidated_spec.py`,
  `scripts/seed_mock_set.py`, `.ai/README.md`, `.ai/tasks/_TEMPLATE.md`. Phase 2 cập nhật những chỗ trỏ tới mục **đã chuyển**
  (`.ai/tasks/*` cũ và `docs/archive`, `docs/review` là lịch sử — không sửa).

---

## Gộp với kiểm chứng của Codex (`CLAUDE-MD-CLEANUP-p1-by-codex.md`)

Codex đối chiếu từng khẳng định với code / docs / git: **44 lỗi thời (S), 14 mâu thuẫn (C), 32 trùng lặp (D), 11 sai tầng (W)**, mỗi mục có
bằng chứng `file:line`. Claude kiểm lại ngẫu nhiên 5 mục nặng (sort mặc định `-severity`, `LPAD` giữ 4 chữ số ĐẦU, `DeviceEndpoints` →
`MachineEndpoints`, mock 3 node, 24 capability) — **cả 5 đúng**.

Hai phần khớp nhau: W-1…W-11 của Codex trùng mục "Bỏ khỏi lõi" ở trên; D-1…D-32 là danh sách gộp khi viết lõi.

**Nghiêm trọng nhất — luật đang dẫn agent làm SAI, không chỉ cũ:**

| Mã | CLAUDE.md nói | Thật |
|---|---|---|
| C-4 | `GET /faults` mặc định `-priority_score` | `-severity` (drift P-3, `FaultSort.Default`) |
| C-5 | ngưỡng `out` 0.15, mọi phân loại theo ratio | `out` do CV OFF; `out_threshold_ratio` trả `null` (D-R22) |
| C-3 | `lamp_out`/`lamp_dim` **chỉ** từ CV | kỹ sư báo tại chỗ (`field_report`) và nay ảnh cột người sửa |
| C-1 | `LPAD` 12345 → `2345`, không ràng buộc nào bắt | → `1234`; trùng ID bị PK chặn (23505) |
| C-2 | thu hồi xã có hiệu lực ngay request kế | hiệu lực khi access token hết hạn / refresh (≤ 60 phút) |
| S-15 | từ chối frame thiếu ISO/shutter/GPS từng ảnh | kiểm ở cấp phiên (D-R24), heading tuỳ chọn |
| S-2 | video là PLANNED, đề xuất presigned | D-R28 = proxy streaming, đã hiện thực, trần 300 MiB |
| S-16 | stack có Hangfire | không Hangfire; lease DB (BE-26) |
| S-1 | D-R20…D-R28 chưa chốt | D-R20/21/22/23/24/28 đã chốt |

## D-item cho Mỹ

- **D-1 Cấu trúc**: lõi + 11 file `docs/agents/traps/*.md` như bảng trên. *Khuyến nghị: nhận.*
- **D-2 Sửa nội dung lúc chuyển**: áp mọi mục S/C/D/W có bằng chứng khi chuyển (không chuyển nguyên văn chỗ đã sai). Mỗi sửa ghi một dòng
  trong `p2` để review được. *Khuyến nghị: nhận — chuyển nguyên văn chỗ sai là chép lỗi sang file mới.*
- **D-3 Lịch sử / số đo (W)**: xoá khỏi CLAUDE.md, giữ **kết luận** và tên test canh; bảng đo (guard A/B, EXPLAIN, lượt đỏ/xanh) để git
  history giữ. *Khuyến nghị: xoá, không tạo file lịch sử.*
- **D-4 Thứ tự ưu tiên nguồn (C-13)**: viết rõ "quyết định trong `contract-drift.md` đã chốt nhưng chưa hợp nhất vào Contract thắng chữ
  Contract" — đây là cách repo đang làm (IoT I-18, sort P-3, D-R22). *Cần Mỹ ký vì nó đổi câu "Contract thắng mọi thứ".*
- **D-5 Contract tự mâu thuẫn** (C-3: `api-contract-v1.1.md:285` ↔ `:703`): ghi một mục drift, không sửa Contract trong PR này.
- **D-6 C-12 (so độ sáng giữa hai cột)**: lux thủ công tương đối dùng để so giữa cột/đêm, còn phân loại `dim` chỉ theo baseline của chính
  cột — viết thành hai câu có phạm vi. *Khuyến nghị: nhận.*

## Lịch

Sửa file **sau khi** PR #161 (WO-SURVEY-CHAIN) và `feat/PHOTO-REVIEW` merge (Mỹ chốt). Nhánh này rebase lên `dev` lúc đó; hai mục mới
(FR-2 WO-SURVEY-CHAIN, POLE-PHOTO-AI PHOTO-REVIEW) đi vào `faults-workorders.md` / `survey.md`.
