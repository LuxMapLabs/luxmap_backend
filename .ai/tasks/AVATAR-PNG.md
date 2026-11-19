---
ticket: AVATAR-PNG
title: Ảnh đại diện nhận cả PNG (lưu lại thành JPEG)
status: done
phase: 2
owner: codex
branch: feat/AVATAR-png
contract_refs:
  - "contract-drift.md — ACCT-5 avatar tài khoản"
depends_on: []
---

## Bối cảnh

`POST /api/v1/auth/me/avatar` hiện chỉ nhận JPEG (magic bytes `FF D8 FF`, decode qua `ThumbnailFactory.JpegOnly`).
Mỹ quyết định 19/11/2026: avatar nhận thêm **PNG**. Web (WP5) đang chặn PNG ở client và sẽ mở theo sau khi API nhận.
Năm advisory ImageSharp 3.1.12 đang bị suppress (`Directory.Build.props`) đều nằm ngoài bộ giải mã PNG (HistogramEqualization,
bộ ghi TIFF T4/T6, bộ đọc BigTIFF, đọc `IccProfile.Entries`), nên bật bộ giải mã PNG **chỉ cho avatar** không chạm tiền đề đó —
miễn là tiền đề được viết lại cho đúng và chốt chặn test vẫn canh.

## Phải đọc trước

- `CLAUDE.md` mục *Bẫy chung → Ảnh* + `docs/agents/traps/media-storage.md` (luật 5 "chỉ nhận JPEG", mục ACCT-AVATAR)
- `Directory.Build.props` khối `NuGetAuditSuppress`
- `src/LuxMap.Shared/Storage/ImageSanitizing.cs`, `src/LuxMap.Infrastructure.Storage/{JpegSanitizer,JpegMagicBytes,ThumbnailFactory}.cs`
- `src/LuxMap.Modules.Identity/Profiles/{AvatarService,AvatarController}.cs`, `src/LuxMap.Modules.Citizen/PublicReportService.cs` (caller thứ hai)
- `tests/LuxMap.Persistence.Tests/JpegOnlyDecodeTests.cs`, `tests/LuxMap.Api.Tests/AvatarTests.cs`, `tests/LuxMap.Api.Tests/CitizenReportTests.cs` (decorator của `IImageSanitizer`)

## Yêu cầu

1. **Chỉ avatar nhận PNG.** Ảnh công dân, survey, evidence, ảnh cột, AI giữ JPEG-only như cũ. Caller phải nói rõ định dạng nhận ở call
   site (ví dụ một tham số/enum định dạng cho phép trên `IImageSanitizer`, mặc định JPEG-only) — không đổi hành vi mặc định của caller khác.
2. **Nhận dạng bằng magic bytes**: JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`. Không tin `Content-Type` hay đuôi file. Không khớp định
   dạng cho phép → 415 `UNSUPPORTED_IMAGE_FORMAT` như hiện tại; thông điệp nêu đúng định dạng được nhận.
3. **Cấu hình giải mã riêng** chỉ gồm `JpegConfigurationModule` + `PngConfigurationModule` (vd `ThumbnailFactory.JpegOrPng`), khai thích
   như `JpegOnly`. Decode PNG chỉ qua cấu hình này; `Configuration.Default` vẫn cấm. **Không** bật `ColorProfileHandling.Convert`
   (giữ mặc định — đọc profile ICC là đường vào GHSA-gwg2).
4. Thứ tự sanitize giữ nguyên cho PNG: cổng đồng thời → đọc có giới hạn byte → magic bytes → `Identify` + trần pixel → decode → orientation
   → thu cạnh dài → **ghép nền trắng nếu có kênh alpha** (JPEG không có alpha; không để vùng trong suốt thành đen) → bỏ mọi metadata →
   **mã hoá lại JPEG**. Đầu ra luôn là JPEG: key `.jpg`, thumbnail, `GET …/avatar` (`image/jpeg`) không đổi.
5. Giới hạn giữ nguyên (`AvatarLimits`: 1 MiB, 16 triệu pixel, cạnh dài 512).
6. **Chốt chặn**: `JpegOnlyDecodeTests` chấp nhận thêm đúng cấu hình JPEG+PNG (không gì khác), cập nhật mô tả. Viết lại comment trong
   `Directory.Build.props`: decode chỉ qua JpegOnly **hoặc** JpegOrPng (chỉ avatar); không advisory nào trong năm cái nằm trong bộ giải mã PNG.
7. Tài liệu: `docs/agents/traps/media-storage.md` (luật 5 + mục ACCT-AVATAR: ngoại lệ PNG cho avatar), `docs/contract-drift.md` mục ACCT-5
   (thêm dòng: avatar nhận JPEG hoặc PNG theo magic bytes, lưu thành JPEG, PNG trong suốt ghép nền trắng — ngày 19/11/2026, Mỹ quyết).

Tiêu chí xong:

- [ ] Test API mới trong `AvatarTests`: PNG hợp lệ → 200 và object lưu là JPEG (magic bytes `FF D8 FF`); PNG có alpha trong suốt → pixel
      tương ứng ở ảnh lưu là trắng (không đen); PNG khai > 16 triệu pixel → 400 trước khi decode; byte bắt đầu bằng chữ ký PNG nhưng hỏng → 415;
      GIF / WebP → 415.
- [ ] Test: PNG gửi vào luồng ảnh công dân vẫn → 415 (canh yêu cầu 1).
- [ ] Unit test (Infrastructure.Storage.Tests) cho nhận dạng chữ ký PNG / JPEG.
- [ ] `JpegOnlyDecodeTests` xanh; phá thử (một `Image.Load` dùng `Configuration.Default`) vẫn đỏ.
- [ ] `dotnet build` không cảnh báo mới.

## KHÔNG ĐƯỢC làm

- Không migration, không đổi cột / key object.
- Không sửa `docs/api-contract-v1.1.md` / `docs/openapi/*.json` (thay đổi đi vào drift trước).
- Không đổi phiên bản ImageSharp, không thêm package.
- Không mở PNG cho bất kỳ luồng ảnh nào khác avatar.
- Không commit (sandbox không ghi được gitdir); không chạy `dotnet test` cần Docker (sandbox không có) — ghi rõ "chưa chạy" trong báo cáo.
