# SPEC-NULLABLE — Codex review lượt 2 (13/10/2026)

Review `git diff 26f2eb4..531d7a3` theo hai trục của skill `code-review`. **Không còn P1; không thấy P2 mới. D-4 vẫn chặn merge; còn 3 P3.** Chỉ ghi file này.

## Standards — 3 P3

1. **P3 — Mapping reflection bỏ sót âm thầm.** `tests/LuxMap.Api.Tests/OpenApiNullableTests.cs:162,166,181`: nhóm tên CLR trùng bị loại, schema ID không khớp `Type.Name` bị bỏ qua; generic cũng không được ánh xạ đầy đủ. `>500` hợp lý như chốt chống mapping hỏng diện rộng, nhưng mất một DTO vẫn xanh. **Đề xuất:** assert/report các schema response có properties chưa ánh xạ, ngoại lệ generic ghi rõ; không dùng tổng số làm bằng chứng “mọi response”. Chưa thấy lỗi trong 25 trường hiện tại; literal vẫn bảo vệ chúng.

2. **P3 — Helper nullable kiểm cờ, chưa kiểm ngữ nghĩa.** `tests/LuxMap.Api.Tests/OpenApiNullableTests.cs:203,207,215`: **có bắt** generator đè `anyOf` bằng `$ref` trần, nhưng `$ref + nullable:true` vẫn qua; xoá property/schema cũng bị bỏ qua. Đã đối chiếu predicate bằng JSON sửa trong bộ nhớ: bare ref bị báo mất, sibling nullable và property bị xoá không bị báo. **Đề xuất:** không nhận nullable sibling của `$ref` trong OAS30 (target tự nullable thì resolve); kiểm `type` cùng nullable hoặc nhánh null hợp lệ; assert trường nullable gốc còn tồn tại. Đây là giới hạn chốt test, spec hiện tại không mất nullable.

3. **P3 — Rationale request vẫn sai trong tài liệu mới.** `CLAUDE.md:1967`, `docs/contract-drift.md:1338` (SN-2), `tests/LuxMap.Api.Tests/OpenApiNullableTests.cs:48`: vẫn nói cả chín trường null → 400/client không được gửi null. Filter đã sửa đúng tại `NullableReferenceFilters.cs:75`: bốn trường từ chối, **năm trường null có nghĩa**. **Đề xuất:** đồng bộ câu “giữ request theo D-1; năm trường là nợ spec cần follow-up”, không mở rộng implementation. P3 #5 **chưa đóng hết**; nhất là drift hiện được dùng để bàn giao consumer.

## Spec — 1 P2 tồn đọng (D-4), không có P1/P2 mới

**P2 #3 còn điều kiện merge:** `../luxmap-web/scripts/generate-types.js:55,94` tại `origin/dev` **b05d74e** chưa hiểu `anyOf`; dạng mới không có `type` ngoài nên **cả 25 trường ra `any`**. Handoff `.ai/results/handoff-2026-10-13-wp5-wp6.md:72–74` đề xuất đúng: nhận đúng hai nhánh ref/null trước `type`, trả tên ref + ` | null`; giữ `?` theo `required` (:107–111). Không chuyển nhánh `[null]` qua nhánh enum cũ (:62), vì `join` biến null thành chuỗi rỗng. Duyệt dependency (:265–273) và import (:346–373) dùng `JSON.stringify` toàn schema, nên đã thấy ref trong `anyOf`; không cần sửa riêng. **Đề xuất:** WP5 sinh lại types và kiểm object/enum nullable cùng import trước khi báo D-4 xong. Lượt này chỉ `git show`/đọc ref, không thực thi generator.

## Các điểm đã đóng / giới hạn

- **P1 encoding đóng:** `NullableReferenceFilters.cs:51,58`, `gen_consolidated_spec.py:357–358` đúng OAS 3.0.4: type cùng nullable nhận null; enum `[null]` loại mọi giá trị khác ở nhánh đó. `anyOf` giữ nguyên constraint của target. Căn cứ [OAS §4.7.24](https://spec.openapis.org/oas/v3.0.4.html#schema-object) và [validator 0.6.3](https://github.com/python-openapi/openapi-schema-validator/blob/0.6.3/openapi_schema_validator/_keywords.py); kết quả instance do Claude cung cấp phù hợp source.
- **Công cụ:** [Redocly enum rule](https://github.com/Redocly/redocly-cli/blob/main/packages/core/src/rules/common/no-enum-type-mismatch.ts) truyền nullable vào [type checker](https://github.com/Redocly/redocly-cli/blob/main/packages/core/src/rules/utils.ts), nên null với type object/string không bị coi mismatch. Suy từ [openapi-typescript 7.13.0](https://github.com/openapi-ts/openapi-typescript/blob/openapi-typescript%407.13.0/packages/openapi-typescript/src/transform/schema-object.ts): string branch sinh null; object enum được xử lý qua composition, `[null]` sinh `never`, rồi nullable thành `never | null` (tương đương null); wrapper vẫn là `T | null`. Không thấy sai kiểu với mẫu này; đây là kiểm source, chưa chạy công cụ.
- **Discriminator/readOnly:** spec hiện không có discriminator; 25 wrapper không mang readOnly/writeOnly. Không thấy mất annotation hiện tại. Không suy rộng cho target có discriminator: validator 0.6.3 có đường gọi `instance.get` trên instance null khi xử lý discriminator. ReadOnly của property phải nằm ở schema property ngoài, không đặt riêng trong nhánh null.
- **IsNullBranch:** `NullableReferenceFilters.cs:61–62,105` đủ cho mọi wrapper tự sinh hiện nay: hai nhánh đúng thứ tự, enum duy nhất C# null, type chứa Null. Predicate lỏng hơn “đúng shape” (không kiểm type nền/constraint thêm), không nhận thứ tự đảo. Với giả định chỉ filter sinh wrapper này đã ghi ở `CLAUDE.md:1973`, chưa có unwrap sai; nếu bỏ giả định, dùng marker hoặc kiểm shape chính xác và thêm probe. Không cần đổi encoding hiện tại.
- **P2 #2 đóng cho phạm vi hiện tại:** có reflection độc lập và so spec hợp nhất; `$ref` trần ghi đè bị bắt. Generic/container vẫn chưa phủ, đã ghi rõ `NullableReferenceFilters.cs:28–29`; không coi là tự động phủ mọi DTO tương lai. Hai giới hạn test ở P3 trên.
- **P3 #6 đóng:** `NullableReferenceFilters.cs:26` đã bỏ số 121 operation; P1 có đính chính. Không yêu cầu viết lại review/task lịch sử.

✅ So JSON độc lập: từ mốc review này, **25 property đổi encoding ở mỗi spec, 0 operation đổi**; 25 anyOf/spec, không mất nullable từ code sang hợp nhất; `git diff --check` sạch. Con số **25/27** của toàn Phase 2 thuộc mốc trước, không phải diff lượt này.

⚠️ Không chạy dotnet/docker/npm/pip, validator hay TypeScript. **1009/46/45/383 xanh, exit 0, build -warnaserror và các sabotage đỏ** là bằng chứng người giao việc cung cấp, không chạy lại. Tổng **15 lệnh shell** kể cả hai reviewer và kiểm file đầu ra; không fetch/checkout, commit hay cập nhật dashboard.

📌 Standards: **3 P3**, nặng nhất là chốt test bỏ sót. Spec: **1 P2 tồn đọng D-4**, không có P1/P2 mới; merge sau bằng chứng WP5 đã sửa script.

## Đã xử lý (Claude, 13/10/2026)

| P3 | Xử lý | Phá thử |
|---|---|---|
| #1 mapping bỏ sót âm thầm | Schema response có thuộc tính mà không ánh xạ được kiểu C# → đỏ, nêu tên; chỉ miễn generic đóng (`*PagedResult`, `*Feature`, `*FeatureCollection`) | — (không có DTO lệch tên để phá) |
| #2 kiểm cờ chứ không kiểm ngữ nghĩa | `Nullable` chỉ nhận `nullable` bên cạnh `type` (không bên cạnh `$ref`) hoặc đúng dạng `anyOf` ref + null; trường nullable bị xoá khỏi bản hợp nhất cũng tính là mất | generator ghi `$ref` + `nullable` → đỏ; generator xoá `lamp_watt` → đỏ |
| #3 lý do request | Đồng bộ ở drift SN-2, AGENTS.md, comment test: 4 trường `null` → 400, 5 trường `null` có nghĩa (nợ, follow-up) | — |
