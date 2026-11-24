# LUX-PHOTO — kết quả (Claude, 24/11/2026)

Codex không chạy được (`gpt-6.1-sol` bị từ chối với tài khoản ChatGPT), nên Claude hiện thực trực tiếp theo D-1…D-9 của ticket.
Contract lên **v1.23** (dev đã ở v1.22).

## Đã đổi

- Survey: `LuxReading.PhotoId`, cấu hình FK ghép `fk_lux_reading_pole_photo` + unique partial `ux_lux_reading_photo_id`,
  kiểm ảnh trong `LuxReadingService`, `photo_id` trên request / hai response, `PhotoLuxLookup`, đăng ký trong `SurveyModule`.
- WorkOrders: port `IPhotoLuxLookup`; `PolePhotoItem` + 3 trường lux; mọi đường trả item qua `Items(...)`; `NotWithdrawn` thành public.
- Shared: `ErrorCodes.PolePhotoNotFound`, `PhotoPoleMismatch`, `PhotoLuxAlreadyLinked`.
- Migration `AddLuxReadingPhotoLink` (cột, FK, index; `Down()` đối xứng). Snapshot: bỏ nhiễu `\r\n` → `\n` của hai CHECK không liên quan.
- Docs: Contract §5.7 / bảng mã lỗi / changelog; drift LUX-PHOTO-1…5; trap survey.md quy tắc 5; OpenAPI xuất lại (lint: 10 warning, bằng trước).
- `scripts/copy_dev_to_supabase.py`: `lux_reading` chép sau `work_order_pole_photo`, chỉ giữ liên kết tới ảnh được chép.

## Bằng chứng

```
dotnet build --disable-build-servers → 0 Warning(s), 0 Error(s)
dotnet test --no-build
  LuxMap.IotBridge.Tests 18 · Shared.Tests 447 · Persistence.Tests 47 · Infrastructure.Storage.Tests 90
  LuxMap.Api.Tests: Passed 1959, Skipped 7, Failed 0 — exit 0
python3 -m unittest discover -s scripts/tests -p test_pole_photo_copy_plan.py → OK
```

Phá thử (khôi phục bằng `cp`): bỏ kiểm cùng cột → `A_photo_of_another_pole_is_400` đỏ; `IgnoreQueryFilters()` trên ảnh →
`A_photo_outside_the_callers_communes_is_404_not_a_pole_mismatch` đỏ.
