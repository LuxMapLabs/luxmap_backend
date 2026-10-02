---
ticket: API-CLIENT-LABELS
title: Đánh dấu trong Swagger endpoint nào cho mobile, cho web, hay dùng chung
status: ready
phase: 2
owner: codex
branch: feat/api-client-labels
---

## Bối cảnh

Mỹ cần nhìn Swagger là biết ngay endpoint nào chỉ dành cho **mobile** (Android, WP6), endpoint nào chỉ dành
cho **web** (WP5), endpoint nào **dùng chung**. Hiện chỉ nhóm auth là khác nhau theo client:

- `/api/v1/auth/login|register|refresh|logout` — **mobile**: refresh token trong body (Contract §4.1).
- `/api/v1/auth/web/login|refresh|logout` — **web**: refresh token chỉ trong cookie HttpOnly, kiểm `Origin` (§4.2).
- `GET /api/v1/auth/me` và **mọi endpoint nghiệp vụ khác** — **dùng chung** (Bearer access token).

Không suy đoán thêm client cho endpoint nghiệp vụ: chúng đều là dùng chung về mặt API.

## Yêu cầu

1. Trong code (`src/`), thêm một attribute nhỏ (ví dụ `ClientSurfaceAttribute` với enum `Mobile | Web`)
   ở **`LuxMap.Shared`** hoặc module Identity, gắn lên `AuthController` (các action mobile) và
   `WebAuthController`. `GET /auth/me` **không** gắn → dùng chung. Không gắn → dùng chung.
2. Một `IOperationFilter` mới ở `src/LuxMap.Api/OpenApi/` (đăng ký trong `SwaggerSetup`, cạnh
   `CapabilityOperationFilter`) cho **mọi** operation:
   - thêm extension `x-luxmap-client`: `mobile` | `web` | `shared`;
   - tiền tố `summary`: `[Mobile] `, `[Web] `, `[Dùng chung] ` — **chỉ** cho operation có tiền tố mobile/web
     và cho `GET /auth/me` (vì nó nằm trong tag `Auth` mobile, dễ hiểu nhầm). Endpoint nghiệp vụ không thêm
     tiền tố (tránh nhiễu) — chỉ có extension.
   - Không đổi `operationId`, **không đổi tên tag** (`Auth`, `WebAuth`): FM-04 sinh DTO/class Kotlin theo
     tag/operationId, đổi là vỡ code mobile.
3. Sửa mô tả hai tag trong Swagger (ở code nếu có khai tag, và `TAGS` trong
   `docs/openapi/tools/gen_consolidated_spec.py`):
   - `Auth`: "MOBILE (Android) — refresh token trong body. Riêng GET /auth/me dùng chung cho web và mobile."
   - `WebAuth`: "WEB (trình duyệt) — refresh token chỉ trong cookie HttpOnly, kiểm Origin."
   - Thêm câu: endpoint không ghi [Mobile]/[Web] là dùng chung, xác thực bằng Bearer access token.
4. `gen_consolidated_spec.py` ghi đè `summary` từ dict `SUMMARY` → phải giữ tiền tố: đọc `x-luxmap-client`
   của operation đã xuất và ghép tiền tố tương ứng vào summary viết tay (cùng luật ở mục 2).
5. Test (ở `tests/LuxMap.Api.Tests`, theo khuôn test metadata/OpenAPI sẵn có): mọi operation có
   `x-luxmap-client`; 4 op auth mobile = `mobile`, 3 op web = `web`, `/auth/me` = `shared`, một endpoint
   nghiệp vụ bất kỳ = `shared`. Kỳ vọng viết LITERAL.
6. Ghi `.ai/results/API-CLIENT-LABELS.md` (tiếng Việt): làm gì, file nào, cách kiểm.

## KHÔNG ĐƯỢC làm

- Không đổi route, operationId, tag name, hình dạng request/response. Không sửa Contract, mock.
- Không đọc `.env`. Không kết nối DB/Docker. Không chạy test cần DB (Claude chạy). Không xuất lại
  `docs/openapi/*.json` (Claude chạy).
- Không thêm NuGet package. Không commit, không push, không `git add -A`.

## Tiêu chí xong

- [ ] `dotnet build -warnaserror` 0 warning.
- [ ] Test không cần DB (Shared, Persistence, Storage) xanh.
- [ ] Results đã ghi. Dừng hẳn.
