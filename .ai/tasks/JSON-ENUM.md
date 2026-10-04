---
ticket: JSON-ENUM
title: Enum trong body JSON chỉ nhận CHUỖI — chặn số nguyên trên toàn API
status: ready
phase: 2
owner: codex
branch: fix/json-enum-strings-only
---

## Bối cảnh

`LuxMapJsonOptions.Configure` đăng ký `new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)`, mà mặc định
`allowIntegerValues = true`: body `{"severity": 999}` hay `{"fault_type": 2}` được đọc thành một giá trị enum — kể cả giá trị
**không định nghĩa** — rồi rơi xuống CHECK của DB (500) hoặc lặng lẽ thành một giá trị hợp lệ khác nghĩa. Contract §0/§1: enum
trên dây là **chuỗi thường**. BE-41 (PR sắp merge) đã chặn cục bộ `severity` của `POST /faults`; task này bịt **gốc**, cho mọi
endpoint. Review Codex BE-41 phát hiện (04/10/2026), Mỹ duyệt sửa toàn cục.

## Phải đọc trước
- `AGENTS.md`: mục Quy ước toàn cục (enum), "Cả JSON cũng hở" (`FiniteDoubleConverter`, `JsonNumberHandlingTests` — cùng họ lỗi,
  khuôn để làm theo), bẫy `Enum.TryParse` trên query string (BE-14 số 1: query string KHÔNG đi qua converter JSON — không phạm vi task).
- `src/LuxMap.Shared/Serialization/LuxMapJsonOptions.cs`, `src/LuxMap.Api/Http/ApiConventionsSetup.cs` (lỗi đọc JSON → 400 nào),
  `tests/LuxMap.Shared.Tests/JsonConventionTests.cs`, `JsonNumberHandlingTests.cs`, `JsonPipelineConventionTests.cs`.

## Yêu cầu
1. Converter enum **không nhận số nguyên** (cả số JSON lẫn chuỗi chữ số như `"2"`). Ghi rõ lý do bằng comment như `FiniteDoubleConverter`.
   Ghi (serialize) không đổi: vẫn chuỗi snake_case.
2. Trên đường HTTP, body có enum là số trả **400 `VALIDATION_FAILED`** theo đúng hình dạng lỗi của repo (không 500, không lộ stack).
   Kiểm cả một endpoint thật dùng enum trong body (ví dụ `PATCH /faults/{id}` hay `POST /faults` hay `PATCH /work-orders`) bằng test
   API **chỉ biên dịch** (Claude chạy).
3. Rà toàn bộ `src/` và `tests/`: có chỗ nào **gửi** hoặc **dựa vào** enum dạng số trong JSON không (seed, fixture, test, Hangfire,
   `JsonDocument`/`JsonElement.Deserialize<…>`, cột `jsonb` lưu enum)? Báo cáo từng chỗ; sửa nếu là lỗi, giữ nếu cố ý.
   ⚠️ `audit_event.before/after` (jsonb) và các snapshot đã LƯU: nếu có dữ liệu cũ chứa enum số mà code đọc lại bằng options này thì
   đọc sẽ vỡ — kiểm và báo.
4. Test không cần DB (Shared): số nguyên, số không định nghĩa, chuỗi chữ số, chuỗi hợp lệ, chuỗi sai tên — mỗi ca một assert;
   ghi vẫn ra chuỗi. Phá thử: bật lại `allowIntegerValues` ⇒ test đỏ (ghi lại trong results).
5. Ghi `.ai/results/JSON-ENUM.md` (tiếng Việt) và một mục ngắn ở `docs/contract-drift.md` (hành vi API đổi với client gửi số: trước
   nhận, nay 400 — đúng Contract, nhưng là thay đổi quan sát được) + dòng trong `AGENTS.md`/`CLAUDE.md` ở mục "Cả JSON cũng hở".

## KHÔNG ĐƯỢC làm
- Không đọc `.env`; không kết nối DB/Docker/MinIO; không chạy test cần DB; không `database update`; không xuất OpenAPI.
- Không đổi hình dạng response, không đổi tên enum, không thêm package, không commit/push, không `git add -A`.
- Không gỡ các kiểm tra cục bộ đã có (ví dụ `Enum.IsDefined(severity)` của BE-41) — chúng là lớp phòng thủ thứ hai.

## Dừng ở đâu
Xong thì dừng hẳn, ghi results. Claude review, chạy test tích hợp + phá thử, lặp tới khi sạch.
