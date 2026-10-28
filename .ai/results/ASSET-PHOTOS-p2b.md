# ASSET-PHOTOS — Phase 2-B (trạng thái mạch): HOÀN TẤT bởi Claude, 28/10/2026

Codex (lượt chạy lại 2, báo cáo nguyên văn ở dưới) dừng với 5 test đỏ và chưa lint / phá thử vì sandbox chặn ghi cache npm. Claude hoàn tất và kiểm độc lập:

- **Sửa 5 test đỏ (chỉ phía test, không đổi hành vi production):** thứ tự feeder trong `FeederStateTests` lấy theo quy ước DB (`length(id), id`) thay vì thứ tự tạo mảng; bỏ assertion "141 op" khỏi `OpenApiSpecTests`
  (test host thêm endpoint thử nên đếm 154 — con số 141 do generator in ra: `implemented_ops=141`); `CabinetTests` thêm key `feeders`.
- **Một lỗi thiết kế bắt được khi rà code:** `FeederPowerState` khai `On` đầu nên là giá trị mặc định của CLR; `FeederRow` dựng `FeederListItem` mà không gán `PowerState`, nên một đường quên gán sẽ nói "có điện".
  Đã khai `Unknown` đầu (`FeederPowerTests`, trap `iot-lighting.md`); thứ tự enum trong spec thành `unknown, on, off`.
- **Spec:** `dotnet swagger tofile` → `gen_consolidated_spec.py`: `info.version` 1.21, **141 op**, 270 schema; `redocly lint` hợp lệ, **9 warning (baseline giữ nguyên)**.
- **Contract:** sửa thứ tự dòng changelog đầu file (v1.19 · v1.20 · v1.21).
- **Phá thử (năm, đều đỏ đúng test rồi khôi phục bằng `cp`, xanh lại):** `State()` trả `On` → `FeederPowerTests` + `FeederStateTests`; `INNER JOIN` → `FeederStateTests` (mất feeder không rơ-le);
  tra control trong vòng lặp → `FeederStateQueryTests.A_page_of_200_uses_the_same_number_of_queries_as_one_feeder`; bỏ `feeder_ids[]` → `FeederStateTests`; enum mặc định `On` → `FeederPowerTests`.
- **Toàn bộ test (mã thoát 0):** Shared 386, Persistence 45, Storage 54, Api 1229 pass, 2 skip (`MqttEndToEndTests`, cần broker thật), 0 fail. `dotnet build -warnaserror`: 0 warning, 0 error.
- **Không kiểm được:** `restore` cục bộ phải chạy `-p:NuGetAudit=false` (sandbox không ghi được cache lỗ hổng NuGet); CI vẫn chạy audit. SQL thật của map cabinet đã có `EXPLAIN` trong `FeederStateQueryTests` (dùng `ix_electrical_cabinet_geom`).
- **Còn mở:** bốn câu hỏi firmware (D-09) chưa gửi; WP5 / WP6 / BE2 chưa xác nhận; phần A (ảnh tài sản) là PR sau.

---

## Báo cáo gốc của Codex (lịch sử — các lỗi liệt kê trong đó đã được sửa như trên)

# ASSET-PHOTOS — Phase 2-B, lượt chạy lại 2

28/10/2026 · Codex · worktree `luxmap-wt-feeder-state`.

**CHƯA HOÀN TẤT. Dừng theo mục “Cách chạy” của ticket khi sandbox chặn ghi cache npm ngoài worktree.**
Không git add / commit / push theo cập nhật mới nhất của Mỹ. Không trailer Co-Authored-By.
Không sửa tracking.html, tasks-backend.csv hay tasks/current.md; Claude cập nhật sau.

## Đã hiện thực trong working tree

- Chỉ phần B; không ảnh tài sản, migration, cột mới, CHECK, package hay project reference mới.
- Shared có `FeederPowerState` (`on|off|unknown`), một hàm `FeederPower.State()` trả Unknown vô điều kiện; Assets và Map cùng gọi.
- Dời **cùng enum** `FeederControlMode` từ Telemetry.Entities về Shared.Contracts.Enums: port Assets cần kiểu đó, Assets không thể reference Telemetry vì chiều tham chiếu hiện có ngược lại. Giữ tên wire on/off/auto; không đổi enum DB hay schema. Thêm using ở entity và MqttMessages; các nơi còn lại đã có using Shared.
- Assets-owned `IFeederControlLookup` / `FeederControlSnapshot`; Telemetry hiện thực và đăng ký DI. Query control batch join node, giữ cả query filter xã; không IgnoreQueryFilters/raw SQL trong implementation.
- Inventory feeder list giữ count + page, thêm một batch control cho cả page; detail dùng lại item và batch một ID. Control null khi chưa nối relay; mode/time null khi chưa báo; offline giữ mode/time lịch sử. Power luôn unknown. Không thêm vào write/import.
- Map cabinets LEFT JOIN controls cho mọi feeder, gồm feeder không relay. `feeder_ids` và `feeders` cùng tập, cùng thứ tự created_at/length(id)/id. Tủ rỗng trả hai mảng rỗng. Không đổi map poles/segments, sync, registry relay write, ingest, settings, capability.
- Contract v1.21 SELF-SIGNED, mục 5.3.1/5.6/changelog; drift FPS-1 đủ Decision/Decision maker/Date/Scope, nhãn FE unknown, bốn câu hỏi firmware, SPEC-NULLABLE. CLAUDE.md đồng bộ v1.21. Generator cập nhật info.version từ 1.19 (sót #190 dù changelog có 1.20) lên 1.21 và mô tả chỉ B, A chưa hiện thực.
- Trap iot-lighting ghi port/batch, enum Shared, power unknown và hai mảng cùng tập.
- Thêm FeederStateTests, FeederStateQueryTests và assertion OpenApiSpecTests. **Hiện còn lỗi test, xem bên dưới.**

## Bằng chứng chạy thật

### Build

Không restore lại, không sửa NuGetAudit/Directory.Build.props. Lệnh:

```sh
dotnet build LuxMap.slnx --no-restore --disable-build-servers -warnaserror
```

Lượt cuối trả mã **0**:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:04.62
```

`git diff --check` cuối lượt: mã 0, không output.

### Docker / host startup

```sh
docker info --format '{{.ServerVersion}}'
```

Mã 0, output `29.6.1`. Testcontainers tạo PostGIS riêng và TCP kết nối được; không dùng luxmap_dev.
Lượt kiểm tra host đầu tiên treo rồi bị ngắt (mã 1); lượt FeederStateTests không polling bị blame hủy:

```text
The active test run was aborted. Reason: Test host process crashed
Data collector 'Blame' message: The specified inactivity time of 90 seconds has elapsed.
Test Run Aborted.
```

Swagger CLI cũng treo, đã ngắt (mã 130). Quan sát lsof thấy thư viện FileSystem.Watcher; thử cùng Swagger CLI với
`DOTNET_USE_POLLING_FILE_WATCHER=1` thì thành công ngay. Các lượt kiểm chứng tiếp dùng biến này **chỉ trong tiến trình**,
không sửa cấu hình repo. Đây là bằng chứng polling xử lý triệu chứng trong môi trường này, chưa chứng minh nguyên nhân OS.
Không thêm debug instrumentation vào repo.

### Test tích hợp và capability

```sh
DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet test tests/LuxMap.Api.Tests --no-build --disable-build-servers \
  --filter 'FullyQualifiedName~FeederState|FullyQualifiedName~OpenApiSpecTests|FullyQualifiedName~RoleCapabilityMatrixTests|FullyQualifiedName~CapabilityMatrixTests|FullyQualifiedName~CapabilityPolicyCoverageTests|FullyQualifiedName~CabinetTests|FullyQualifiedName~AssetReadShapeTests' \
  --logger 'console;verbosity=normal'
```

Mã thoát **1**:

```text
Test Run Failed.
Total tests: 183
     Passed: 178
     Failed: 5
 Total time: 9,8985 Seconds
```

Các test mới đã qua:

```text
Passed FeederStateTests.Inventory_and_map_preserve_reports_but_never_infer_power(mode: null, connected: False)
Passed FeederStateTests.Inventory_and_map_preserve_reports_but_never_infer_power(mode: "auto", connected: True)
Passed FeederStateTests.Commune_scope_hides_foreign_feeders_controls_and_cabinets
Passed FeederStateTests.Offline_preserves_the_last_mode_and_empty_cabinets_have_both_arrays
Passed FeederStateQueryTests.A_page_of_200_uses_the_same_number_of_queries_as_one_feeder
Passed FeederStateQueryTests.The_actual_cabinet_response_query_can_use_the_4326_gist_index
```

**Năm lỗi cần sửa trước review/commit:**

1. OpenApiSpecTests.Feeder_state_is_additive_read_only_and_uses_string_enums: Expected 141, Actual 154.
   Host test có thêm endpoint test. CLI xuất từ production assembly thực tế là 141. Assertion số op cần đọc artifact production hoặc lọc đúng endpoint test đã xác minh; không đổi kỳ vọng production thành 154.
2. FeederStateTests.Inventory... mode off: expected `[FDR-005,FDR-004]`, actual `[FDR-004,FDR-005]`.
3. Cùng test mode null + connected: expected `[FDR-007,FDR-006]`, actual `[FDR-006,FDR-007]`.
4. Cùng test mode on: expected `[FDR-009,FDR-008]`, actual `[FDR-008,FDR-009]`.
   Fixture tạo array entity trước SaveChanges không bảo đảm thứ tự DB cấp sequence. Phải đọc lại thứ tự created_at/length(id)/id cho kỳ vọng và định danh feeder có relay riêng; không giả định phần tử đầu array là ID nhỏ nhất. Các ID ở đây là output thật, không literal trong source.
5. CabinetTests.A_cabinet_without_a_device_is_still_on_the_map_with_exactly_the_decided_keys: expected key set cũ không có `feeders`; actual thêm `feeders` đúng API mới. Cập nhật expectation literal của test cũ.

Query tests đã assert số SQL không tăng page 1→200 và một SELECT control/page, EXPLAIN SQL response thật chứa ix_electrical_cabinet_geom.
**Chưa có output số SQL/plan trong báo cáo:** logger normal không in ITestOutputHelper của test pass. Cần chạy lại hai test với logger detailed để lấy bằng chứng số SQL và plan, không suy một con số từ code.
Không chạy toàn suite. Không chạy hai dotnet test đồng thời.

### Spec

```sh
DOTNET_USE_POLLING_FILE_WATCHER=1 Swagger__Enabled=true Cors__AllowedOrigins__0=https://localhost:3000 \
  dotnet swagger tofile --output docs/openapi/luxmap-v1.json src/LuxMap.Api/bin/Debug/net10.0/LuxMap.Api.dll v1
python3 docs/openapi/tools/gen_consolidated_spec.py
```

Cả hai mã 0:

```text
Swagger JSON/YAML successfully written to .../docs/openapi/luxmap-v1.json
wrote docs/openapi/luxmap-v1.5.json: paths=110 implemented_ops=141 not_implemented_ops=0 schemas=270
```

Trước: consolidated info.version 1.19, 141 op. Sau: 1.21, 141 op. Không sửa JSON sinh bằng tay.
Rà generator không có override FeederListItem/CabinetProperties/FeederControlMode; schema mới đi từ code.
Swagger thực tế: FeederPowerState enum chuỗi on/off/unknown; FeederControlMode on/off/auto;
mode_reported_at nullable=true, relay_no trên CabinetFeederState nullable=true; nullable control/mode vẫn `$ref` trần.

### Điểm dừng bắt buộc — lint

```sh
npx @redocly/cli lint docs/openapi/luxmap-v1.5.json
```

Mã **1**, trước khi Redocly lint được spec:

```text
npm error code EPERM
npm error syscall open
npm error path /Users/nhm809/.npm/_cacache/tmp/***
npm error errno EPERM
npm error Log files were not written due to an error writing to the directory: /Users/nhm809/.npm/_logs
```

Ticket Phase 2-B, Cách chạy: **“Nếu sandbox chặn Docker / mạng / ghi ngoài worktree thì dừng ở chỗ đó và ghi rõ”**.
Đã dừng khi thấy output này; không sudo/chown, không đổi quyền/cache để vượt điểm dừng.
**Chưa xác nhận baseline 9 warning không tăng.** Đây là lỗi ghi filesystem npm cache, không phải lỗi restore/NuGet và không phải Docker.

## Phá thử — chưa thực hiện

Cả bốn lượt chưa chạy vì bộ test cơ sở còn đỏ và gặp điểm dừng sandbox:

- Mode on → power on phải đỏ.
- INNER JOIN làm mất feeder không relay phải đỏ.
- Control lookup trong foreach phải làm SQL count đỏ.
- Bỏ feeder_ids phải đỏ.

Không có file phá thử để khôi phục, không có mã phá thử còn lại trong working tree.

## Bàn giao

✅ Verified: build không restore, production Swagger/generator 141 op, diff whitespace; 178 test qua trong lượt được nêu.

⚠️ Could not verify: hoàn tất test (5 lỗi nêu trên), lint/9 warning, SQL count/plan dạng output để dán, bốn phá thử, hồi quy sau sửa test, enum round-trip riêng và bộ Shared/Persistence. Chưa đủ điều kiện hoàn tất Phase 2-B.

📌 Remaining assumptions: vẫn SELF-SIGNED tới FW; nghĩa phép đo điện / bốn câu hỏi firmware chưa chốt như D-09. Không phát sinh quyết định kiến trúc mới. Giữ toàn bộ thay đổi chưa stage cho Claude kiểm/sửa/chạy tiếp; đặc biệt rà lại thứ tự ngày v1.19/v1.20/v1.21 đầu Contract trước khi commit.
