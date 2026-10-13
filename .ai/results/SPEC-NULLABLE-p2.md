# SPEC-NULLABLE — Phase 2 (Claude, Codex review hai lượt, 13/10/2026)

Nhánh `feat/SPEC-NULLABLE` (từ `dev` @ `df598c1`). Quyết định: D-1…D-6 (Mỹ, 13/10), **D-2 sửa thành `anyOf`** sau review Codex P1.
Drift **SN-1…SN-4**. 🔴 **Merge chỉ sau khi WP5 sửa `scripts/generate-types.js` (D-4)** — với script hiện tại, dạng `anyOf` ra `any`.

## Đã làm

| Phần | File |
|---|---|
| `NullableReferenceSchemaFilter`: đọc nullable annotation của C#, bọc thuộc tính `$ref` nullable thành `anyOf: [{$ref}, {type, nullable: true, enum: [null]}]`; loại thuộc tính có kiểu là tham số generic (`Feature<T>.Properties`). `NullabilityInfoContext` tạo mới mỗi lần (không an toàn đa luồng) | `src/LuxMap.Api/OpenApi/NullableReferenceFilters.cs` |
| `NullableReferenceDocumentFilter`: gỡ lại ở mọi schema chạm tới từ request body / tham số | cùng file, `SwaggerSetup.cs` |
| Generator: `S["PoleProperties"]` viết tay — `power_source`, `fixture_type` dạng `anyOf`; `lamp_watt`, `install_date` `nullable` | `gen_consolidated_spec.py` |
| Spec: code **25** thuộc tính, hợp nhất **27**; **0 operation đổi**; không đổi gì trên dây | `docs/openapi/` |
| `OpenApiNullableTests` (43): literal 25 response / 9 request / 6 feature; dạng `anyOf` toàn spec; đối chiếu **độc lập** mọi kiểu response bằng reflection (schema không ánh xạ được → đỏ, trừ generic đóng); bản hợp nhất giữ mọi nullable của code (và không chấp nhận `$ref` kèm `nullable`) | tests |

## Bằng chứng

```
dotnet build LuxMap.slnx -warnaserror      Build succeeded
dotnet test: mã thoát 0 — Api 1009 ✓ · 2 skip (MQTT E2E) · Storage 46 ✓ · Persistence 45 ✓ · Shared 383 ✓
Redocly: valid, 8 warnings (như trước)
openapi-schema-validator 0.6.3 (OAS30, venv tạm):
  fixture anyOf      null→accept | object→accept | "oops"→reject
  fixture allOf (cũ) null→reject | object→accept | "oops"→reject
  source_channel anyOf      null→accept | "cv"→accept | "bogus"→reject
  source_channel allOf (cũ) null→reject | "cv"→accept | "bogus"→reject
```

Phá thử (khôi phục bằng `cp` + `touch`) — 9 chốt, đều đỏ: bỏ document filter (9) · bỏ luật generic (6) · bỏ schema filter (26) · enum ghi
`object` (15) · `IsNullBranch` luôn false (9) · bỏ schema filter → test độc lập · generator bỏ `nullable` của `lamp_watt` · generator ghi
`$ref` kèm `nullable` · generator xoá `lamp_watt`.

## Review Codex

- Lượt 1 (`.ai/reviews/SPEC-NULLABLE-p2-by-codex.md`): **1 P1 / 3 P2 / 2 P3**. P1 — `allOf` + `nullable` vẫn bị validator chặt từ chối `null`
  ⇒ Mỹ đổi D-2 sang `anyOf` (đã chứng minh bằng validator). P2: thêm đối chiếu độc lập + kiểm bản hợp nhất; đính chính kết luận về script
  WP5; WP6 DTO là nợ có sẵn (bản tin). P3: lý do giữ request (5 trường `null` có nghĩa — nợ spec, follow-up); bỏ con số 121 sai.
- Lượt 2 (`.ai/reviews/SPEC-NULLABLE-p2b-by-codex.md`): **0 P1, không P2 mới, 3 P3** — đã sửa cả ba (báo schema không ánh xạ được; kiểm
  nullable theo ngữ nghĩa OAS 3.0 + trường bị xoá; đồng bộ lý do request ở drift / AGENTS / test).

## Giới hạn

- Chưa phủ: phần tử nullable **bên trong** list / map, và thuộc tính có kiểu là tham số generic (`Wrapper<T>.Value`) — chưa có trong response.
- 5 trường request mà `null` có nghĩa vẫn khai `$ref` trần — nợ có từ trước, follow-up riêng.
- **D-4:** chờ WP5 sửa script (nhánh `anyOf` ref + null → `<Tên> | null`); WP6 sửa DTO theo bản tin.
