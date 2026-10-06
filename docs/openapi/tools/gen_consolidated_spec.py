#!/usr/bin/env python3
"""Builds docs/openapi/luxmap-v1.5.json — the CONSOLIDATED spec that matches api-contract-v1.1.md 1-1.

Run from the repository root after regenerating luxmap-v1.json from code (docs/development.md):

    python3 docs/openapi/tools/gen_consolidated_spec.py
    npx @redocly/cli lint docs/openapi/luxmap-v1.5.json

Source of truth for IMPLEMENTED operations is docs/openapi/luxmap-v1.json, which is exported from
the code and never edited by hand (decision E). This script only ADDS: the endpoints the Contract
specifies but the code does not serve yet (x-luxmap-status = not_implemented), summaries and
operationIds, tag descriptions, the prefixed-id patterns of Contract section 1.2, and the schemas
those endpoints need. Introduced at BE-REVIEW-02 (18/09/2026); the Contract section numbers below
are those of v1.5. GET /auth/me added 19/09/2026 (Contract v1.5). Roles and the capability matrix of
Contract v1.7 (25/09/2026) come from the code as x-luxmap-capability / x-luxmap-roles.
"""
import json, copy, sys, re
from collections import OrderedDict

SRC = "docs/openapi/luxmap-v1.json"
DST = "docs/openapi/luxmap-v1.5.json"

d = json.load(open(SRC), object_pairs_hook=OrderedDict)

# ── info / servers ──────────────────────────────────────────────────────────────
d["info"]["version"] = "1.14"
d["info"]["title"] = "LuxMap API"
# ĐẾM, không gõ tay. Con số này từng là hằng số và nó lệch ngay lần thêm endpoint kế tiếp — cùng lớp
# lỗi với cái tên file `luxmap-v1.4.json` đã trỏ vào hư không. Nguồn chỉ chứa operation đã hiện thực.
HTTP_METHODS = ("get", "post", "put", "patch", "delete")
n_from_code = sum(1 for item in d["paths"].values() for m in item if m in HTTP_METHODS)

d["info"]["description"] = (
    "Contract v1.8 cộng các drift đã hiện thực (BE-23: WO-1…WO-11, FR-2, FR-2a, FR-3; BE-40: F-1…F-6; BE-19: R-1…R-9, P-2, P-3 — nền tạm tới FW). "
    "v1.8 (BE-33a): Quản trị hệ thống tạo tài khoản và mời qua email; POST /auth/register đã gỡ. "
    "v1.9 (POLE-NOTE): ghi chú của kỹ sư trên cột, PUT /assets/poles/{poleId}/note. "
    "v1.10 (BE-27): thông báo trong ứng dụng, đọc bằng polling — /notifications. "
    "v1.11 (N-6): GET /faults/{id} — một sự cố, cùng hình dạng item danh sách. "
    "v1.12 (POLE-NOTE N-4, BREAKING): note thành chuỗi; updated_by/updated_by_name trên tài sản kiểm kê; import nạp ghi chú, kết quả thêm unchanged + warnings[]. "
    "v1.13 (BE-43): GET /sync/bundle + POST /sync/push (offline cho Kỹ sư hiện trường, capability SyncOffline); performed_at? cho bắt đầu / báo xong phiếu. "
    "v1.14 (BE-25): GET /work-orders/agenda — việc đêm nay của một kỹ sư, gom theo tuyến, gần nhất trước khi có near. "
    "Sinh bằng docs/openapi/tools/gen_consolidated_spec.py từ docs/openapi/luxmap-v1.json (spec xuất từ "
    f"code, {n_from_code} operation implemented) cộng các endpoint Contract chưa có code (x-luxmap-status = "
    "not_implemented). Quy ước: JSON snake_case, enum chuỗi thường, ISO 8601 UTC hậu tố Z, EPSG:4326, "
    "base path /api/v1. "
    "Endpoint không ghi [Mobile]/[Web] là dùng chung, xác thực bằng Bearer access token."
)
d["servers"] = [
    {"url": "http://localhost:5141", "description": "Development — launchSettings.json profile 'http'"},
    {"url": "https://localhost:7252", "description": "Development — launchSettings.json profile 'https'"},
]

# ── lint fix that is NOT a code change: ApiError.details additionalProperties needs a type ─────
d["components"]["schemas"]["ApiError"]["properties"]["details"]["additionalProperties"] = {}
d["components"]["schemas"]["ApiError"]["properties"]["details"]["description"] = (
    "Túi ngữ cảnh tự do. Luôn có mặt và luôn chứa correlation_id (Contract §1.4)."
)
# Same lint fix for BE-43's two free-form fields: an untyped `object?` in C# exports `nullable` without `type`.
d["components"]["schemas"]["SyncConflict"]["properties"]["server_state"] = {
    "type": "object", "additionalProperties": True, "nullable": True,
    "description": "Thực thể như hiện tại: WorkOrderDetail (work_order_*) hoặc dòng cột kiểm kê (pole_note); null khi người gọi không còn thấy nó.",
}
d["components"]["schemas"]["SyncError"]["properties"]["details"]["additionalProperties"] = {}

# ── tags ───────────────────────────────────────────────────────────────────────
TAGS = OrderedDict([
    ("Auth", "MOBILE (Android) — refresh token trong body. Riêng GET /auth/me dùng chung cho web và mobile."),
    ("WebAuth", "WEB (trình duyệt) — refresh token chỉ trong cookie HttpOnly, kiểm Origin."),
    ("Assets", "Quản lý kiểm kê tài sản — Contract §5.3. Không phải endpoint bản đồ §5.1. Ghi = Quản lý (cap:manage_assets); đọc = cap:read_network."),
    ("AssetImport", "Nhập tài sản CSV/GeoJSON theo từng loại file — Contract §5.3."),
    ("LuxReadings", "Số đo sáng TƯƠNG ĐỐI bằng điện thoại — Contract §5.7. Ghi = Kỹ sư hiện trường (cap:record_lux_reading)."),
    ("Poles", "Cột đèn trên bản đồ — Contract §5.1. CHƯA HIỆN THỰC (BE-14, BE-20)."),
    ("Segments", "Đoạn đường — Contract §5.2. CHƯA HIỆN THỰC (BE-14)."),
    ("Faults", "Sự cố — Contract §5.4. GET (BE-40, F-1…F-6) và PATCH (BE-19, R-1…R-9) đã hiện thực; POST (BE-41) CHƯA."),
    ("WorkOrders", "Phiếu công việc — BE-23 đã hiện thực, drift WO-1…WO-11 (nền tạm tới FW). Evidence còn BE-24."),
    ("Sweeps", "Phiên khảo sát video — BE-15 P2a đã hiện thực nhận/nộp phiên và đọc (SELF-SIGNED, nền tạm tới FW). Xử lý ở P2b, duyệt ở P2c."),
    ("IotSweeps", "Thumbnail khung hình — Contract §5.6. CHƯA HIỆN THỰC (BE-15 P2b)."),
    ("Sync", "Đồng bộ offline cho Kỹ sư hiện trường — Contract §5.8 (BE-43): gói dữ liệu đầy đủ theo tuyến + hàng chờ thao tác."),
])
d["tags"] = [{"name": k, "description": v} for k, v in TAGS.items()]

# ── helpers ────────────────────────────────────────────────────────────────────
S = d["components"]["schemas"]

def err_content():
    return {"application/json": {"schema": {"$ref": "#/components/schemas/ApiErrorResponse"}}}

def err(desc):
    return {"description": desc, "content": err_content()}

def json_content(ref):
    return {"application/json": {"schema": {"$ref": f"#/components/schemas/{ref}"}}}

def opid(method, path):
    parts = [p for p in path.replace("/api/v1", "").split("/") if p]
    name = "".join(re.sub(r"[{}]", "", p).title().replace("-", "").replace("_", "") for p in parts)
    return f"{method.lower()}{name}"

# Summaries for the implemented operations (from the controllers' XML docs / behaviour)
SUMMARY = {
    ("post", "/api/v1/assets/import/{kind}"): "Nạp MỘT loại file kiểm kê (segments|feeders|poles|fixtures); 200 kèm kết quả theo dòng",
    ("get", "/api/v1/assets/segments"): "Danh sách tuyến — dòng kiểm kê đầy đủ (BE-12b, §5.3.1)",
    ("get", "/api/v1/assets/segments/{segmentId}"): "Một tuyến + geom_wkt + created_at; ngoài phạm vi xã → 404",
    ("post", "/api/v1/assets/segments"): "Tạo tuyến đường; 201 + Location, không body",
    ("get", "/api/v1/assets/feeders"): "Danh sách mạch điện — has_geometry + pole_count; KHÔNG có data_source (§1.6)",
    ("get", "/api/v1/assets/feeders/{feederId}"): "Một mạch điện; geom_wkt null khi chưa khảo sát tuyến cáp",
    ("post", "/api/v1/assets/feeders"): "Tạo mạch điện; 201 + Location, không body",
    ("get", "/api/v1/assets/poles"): "Danh sách cột kiểm kê — có external_ref, data_source, feeder_id, active_fixture (§5.3.1)",
    ("get", "/api/v1/assets/poles/{poleId}"): "Một cột + geom_wkt + segment_name + created_at; ngoài phạm vi xã → 404",
    ("post", "/api/v1/assets/poles"): "Tạo cột; 201 + Location, không body",
    ("post", "/api/v1/assets/fixtures"): "Ghi một lần lắp bóng; commune_id chép từ cột",
    ("put", "/api/v1/assets/segments/{segmentId}"): "Thay thế TOÀN PHẦN một tuyến; commune_id không sửa được",
    ("delete", "/api/v1/assets/segments/{segmentId}"): "Xoá tuyến; khoá ngoại quyết định (409 ASSET_IN_USE)",
    ("put", "/api/v1/assets/feeders/{feederId}"): "Thay thế TOÀN PHẦN một mạch điện; commune_id không sửa được",
    ("delete", "/api/v1/assets/feeders/{feederId}"): "Xoá mạch điện; còn cột đang đấu vào thì 409 ASSET_IN_USE",
    ("put", "/api/v1/assets/poles/{poleId}"): "Thay thế TOÀN PHẦN một cột; THIẾU feeder_id là XOÁ mạch của cột",
    ("delete", "/api/v1/assets/poles/{poleId}"): "Xoá cột; khoá ngoại quyết định (409 ASSET_IN_USE)",
    ("put", "/api/v1/assets/poles/{poleId}/feeder"): "Gán hoặc xoá mạch điện của cột (feeder_id null = không mạch)",
    ("get", "/api/v1/sync/bundle"): "[BE-43] Gói offline đầy đủ cho các tuyến (segment_id lặp, ≤ 20; vắng = tuyến của phiếu mở của tôi): tuyến, cột (+note), sự cố mở, phiếu mở của tôi. Không có since",
    ("post", "/api/v1/sync/push"): "[BE-43] Áp hàng chờ offline theo thứ tự: applied[] / conflicts[] (server thắng, kèm trạng thái hiện tại) / rejected[]; khử trùng lặp theo client_op_id",
    ("put", "/api/v1/assets/poles/{poleId}/note"): "[POLE-NOTE] Kỹ sư hiện trường / Quản lý ghi hoặc xoá ghi chú của cột (≤ 1000 ký tự; null hay rỗng = xoá)",
    ("get", "/api/v1/notifications"): "[BE-27] Thông báo của chính người gọi, mới nhất trước, kèm số chưa đọc (polling)",
    ("get", "/api/v1/notifications/unread-count"): "[BE-27] Số thông báo chưa đọc — gọi định kỳ 30–60 giây cho huy hiệu chuông",
    ("post", "/api/v1/notifications/{notificationId}/read"): "[BE-27] Đánh dấu một thông báo đã đọc (lặp lại không đổi gì; của người khác → 404)",
    ("post", "/api/v1/notifications/read-all"): "[BE-27] Đánh dấu mọi thông báo chưa đọc của người gọi là đã đọc",
    # BE-14 — endpoint bản đồ, đặc tả đầy đủ ở Contract mục 5.1–5.2.
    ("get", "/api/v1/map/poles"): "Bản đồ cột theo bbox; FeatureCollection, properties phẳng; quá 2000 cột → 413 BBOX_TOO_LARGE",
    ("get", "/api/v1/map/poles/{pole_id}"): "[TẠM — BE-20] Chi tiết cột đủ trong MỘT request: bóng đang dùng, trạng thái, baseline theo chiều, 30 điểm lịch sử, sự cố mở, frame gần đây; ngoài phạm vi xã → 404",
    ("get", "/api/v1/map/segments"): "Bản đồ tuyến theo bbox; FeatureCollection của LineString; controller_node_ids[] tính lúc đọc (I-7b)",
    # BE-14b — thiết bị ở tủ điện tổng, drift "BE-14 / IoT".
    ("get", "/api/v1/map/iot-nodes"): "[TẠM — BE-14 / IoT] Thiết bị IoT ở tủ điện theo bbox; không battery_pct, segment_ids/feeder_ids tính lúc đọc",
    # BE-40 — §5.4; lọc CSV, sort, commune_id và mặc định ẩn calibration_rig là drift F-1…F-6.
    ("get", "/api/v1/faults"): "Danh sách sự cố — phân trang JSON, KHÔNG GeoJSON; mặc định -severity rồi cũ trước (P-3)",
    ("post", "/api/v1/faults"): "[TẠM — BE-41] Kỹ sư hiện trường báo sự cố tại chỗ: detected, field_report; cột cho xã/tuyến/data_source; client_op_id gửi lại → 200 cùng sự cố",
    ("post", "/api/v1/faults/{id}/photos"): "[TẠM — BE-41, EV-2] Người báo gắn ảnh observation khi sự cố còn mở; JPEG theo magic bytes; client_op_id cho lần gửi lại",
    ("get", "/api/v1/faults/{id}/photos"): "[TẠM — BE-41] Ảnh của sự cố theo thời điểm chụp, kèm đường dẫn ảnh qua API",
    ("get", "/api/v1/faults/{id}"): "[N-6] Một sự cố, đúng hình dạng item của GET /faults; ngoài phạm vi xã = không tồn tại = 404",
    ("patch", "/api/v1/faults/{id}"): "[TẠM — BE-19] Quản lý duyệt: detected → confirmed|rejected, phân loại lại lamp_out↔lamp_dim, severity, review_note",
    # BE-13 topology — ⚠️ PROVISIONAL, ngoài Contract, drift 46.
    ("get", "/api/v1/assets/feeders/{feederId}/poles"): "[TẠM — drift 46] Cột trên một mạch điện; đầu vào CV-15. Mạch ngoài phạm vi xã → 404",
    ("get", "/api/v1/assets/segments/{segmentId}/poles"): "[TẠM — drift 46] Cột trên một tuyến; có thể gồm cột của xã khác (inter_commune)",
    ("get", "/api/v1/assets/feeders/poles"): "[TẠM — drift 46] Cột CHƯA có mạch; bắt buộc unassigned=true; chỉ listing này trả power_source",
    ("put", "/api/v1/assets/fixtures/{fixtureId}/removal"): "Ngừng dùng bóng bằng removed_date; không có DELETE",
    ("get", "/api/v1/auth/me"): "Người đang đăng nhập, đọc từ DB nên role và commune_ids luôn tươi",
    ("post", "/api/v1/auth/login"): "Đăng nhập mobile; trả đúng bốn trường",
    # BE-33a — Contract v1.8 §4.8 / §4.9, SELF-SIGNED tới FW.
    ("post", "/api/v1/auth/password/set"): "[BE-33a] Đặt mật khẩu bằng link mời / đặt lại trong email; dùng một lần, kết thúc mọi phiên cũ",
    ("post", "/api/v1/auth/password/forgot"): "[BE-33a] Gửi link đặt lại mật khẩu; LUÔN 202 cùng một body; giới hạn 5 lần / 15 phút / địa chỉ",
    ("post", "/api/v1/admin/users"): "[BE-33a] Quản trị tạo tài khoản không mật khẩu và gửi email mời (72 giờ)",
    ("get", "/api/v1/admin/users"): "[BE-33a] Danh sách tài khoản; lọc role, status (invited/active/locked)",
    ("get", "/api/v1/admin/users/{id}"): "[BE-33a] Chi tiết một tài khoản",
    ("patch", "/api/v1/admin/users/{id}"): "[BE-33a] Sửa họ tên, email, vai trò, xã; username không đổi",
    ("post", "/api/v1/admin/users/{id}/lock"): "[BE-33a] Khoá và thu hồi mọi phiên; không tự khoá, không khoá Quản trị cuối",
    ("post", "/api/v1/admin/users/{id}/unlock"): "[BE-33a] Mở khoá",
    ("post", "/api/v1/admin/users/{id}/invite"): "[BE-33a] Gửi lại email mời; link cũ hết hiệu lực",
    ("post", "/api/v1/auth/refresh"): "Xoay vòng refresh token (mobile)",
    ("post", "/api/v1/auth/logout"): "Thu hồi refresh token (mobile); luôn 204",
    ("post", "/api/v1/lux-readings"): "Ghi số đo lux; trùng client_op_id trả 200 kèm bản ghi cũ",
    ("get", "/api/v1/lux-readings"): "Kéo số đo lux hàng loạt kèm nearest_luminance (hiện luôn null)",
    ("get", "/api/v1/lux-readings/poles/{poleId}"): "Chuỗi số đo của một cột, sắp theo measured_at, có phân trang",
    ("post", "/api/v1/auth/web/login"): "Đăng nhập web; refresh token vào cookie",
    ("post", "/api/v1/auth/web/refresh"): "Xoay vòng refresh token từ cookie; không body",
    ("post", "/api/v1/auth/web/logout"): "Thu hồi token trong cookie, luôn xoá cookie, luôn 204",
}
# BE-23 provisional operations, exported from controllers (WO-1…WO-11).
for method, suffix, summary in [
    ("get", "", "Danh sách việc trong phạm vi xã và người được giao"),
    ("get", "/{id}", "Chi tiết việc và các hành động được phép"),
    ("get", "/assignees", "Kỹ sư hiện trường đủ điều kiện trong xã"),
    ("post", "/{id}/evidence", "[BE-24] Kỹ sư được giao tải ảnh khi phiếu đang làm: before/after (sửa chữa), observation (kiểm tra); JPEG theo magic bytes; client_op_id cho lần gửi lại"),
    ("get", "/{id}/evidence", "[BE-24] Ảnh của phiếu theo thời điểm chụp, kèm đường dẫn ảnh qua API"),
    ("get", "/{id}/poles", "[WO-12] Cột trên đoạn được giao + trạng thái đèn lần khảo sát đã duyệt gần nhất, theo thứ tự dọc đường — biết TRƯỚC khi đi"),
    ("get", "/agenda", "[BE-25] Việc đêm nay của một kỹ sư, gom theo tuyến, tuyến gần nhất trước khi có near=lat,lng; kỹ sư xem của mình, vai trò khác phải truyền assigned_to"),
    ("post", "", "Tạo inspection/repair/survey; task_kind bắt buộc. survey: commune_id làm xã neo + segment_ids có thứ tự (BE-15)"),
    ("patch", "/{id}", "Sửa title, due_date, scheduled_date; thiếu giữ nguyên, null xoá ngày"),
    ("put", "/{id}/assignee", "Giao, giao lại hoặc gỡ người được giao"),
    ("post", "/{id}/start", "Người được giao bắt đầu việc"),
    ("post", "/{id}/complete", "Người được giao nộp báo cáo và kết quả kiểm tra"),
    ("post", "/{id}/verify", "Quản lý nghiệm thu và giải phóng liên kết fault"),
    ("post", "/{id}/return", "Quản lý trả việc để làm tiếp"),
    ("post", "/{id}/cancel", "Quản lý huỷ và giải phóng liên kết fault"),
    ("post", "/{id}/follow-up", "[FR-2] Quản lý tạo bước tiếp từ phiếu đã nghiệm thu; server mang sự cố fault_present sang, chung case_id"),
]:
    SUMMARY[(method, "/api/v1/work-orders" + suffix)] = "[TẠM — WO-1…WO-12] " + summary

# BE-15 P2a provisional operations, exported from SweepsController (SELF-SIGNED, temporary until FW).
for method, suffix, summary in [
    ("post", "", "Tạo phiên khảo sát cho phiếu survey đang làm; idempotent theo client_op_id (201 mới, 200 lặp lại)"),
    ("get", "", "Danh sách phiên; lọc work_order_id, segment_id, processing_status, data_source"),
    ("get", "/{id}", "Chi tiết phiên kèm clip và file thô đã nhận"),
    ("put", "/{id}/clips/{clipNo}", "Tải một clip MP4 (stream, tối đa 300 MiB, X-Content-SHA256 bắt buộc)"),
    ("put", "/{id}/raw/{kind}", "Tải file thô gps_track | lux_log (JSONL) hoặc capture_config (JSON); kiểm toàn file trước khi ghi"),
    ("post", "/{id}/submit", "Nộp phiên khi đủ clip + 3 file thô và manifest khớp hash; 202 vào hàng chờ xử lý"),
]:
    SUMMARY[(method, "/api/v1/sweeps" + suffix)] = "[TẠM — BE-15 P2a] " + summary

# BE-15 P2c provisional review operations (SELF-SIGNED, temporary until FW).
for method, path, summary in [
    ("get", "/api/v1/sweeps/{id}/results", "Kết quả từng lượt quét theo cột của run (mặc định run thành công mới nhất), kèm published_as xem trước"),
    ("post", "/api/v1/sweeps/{id}/review", "Quản lý chấp nhận (công bố trạng thái cột, chuỗi độ sáng, baseline, sự cố CV) hoặc trả lại; idempotent theo client_op_id"),
    ("get", "/api/v1/frames/{frame_id}/thumbnail", "Thumbnail JPEG của khung hình khảo sát — proxy qua API, không presigned; 503 khi object thiếu"),
]:
    SUMMARY[(method, path)] = "[TẠM — BE-15 P2c] " + summary

for suffix, summary in [("thumbnail", "Thumbnail JPEG của ảnh phiếu — proxy qua API; quyền theo phiếu cha"),
                        ("original", "Ảnh gốc nguyên byte của phiếu — proxy qua API; quyền theo phiếu cha")]:
    SUMMARY[("get", "/api/v1/evidence/{evidence_id}/" + suffix)] = "[TẠM — BE-24] " + summary

SECTION = {
    "/api/v1/work-orders": "§5.5 + drift WO-1…WO-11",
    "/api/v1/faults": "§5.4 + drift F-1…F-6",
    "/api/v1/auth/me": "§4.7",
    "/api/v1/assets": "§5.3",
    "/api/v1/auth/web": "§4.2",
    "/api/v1/auth": "§4.1",
    "/api/v1/lux-readings": "§5.7",
    "/api/v1/sweeps": "§5.6 + BE-15 P2a (SELF-SIGNED, nền tạm tới FW)",
    "/api/v1/frames": "§2.7 + BE-15 P2c",
    "/api/v1/evidence": "§5.5 + BE-24",
}

for path, item in d["paths"].items():
    for method, op in item.items():
        if method not in HTTP_METHODS:
            continue
        op["summary"] = SUMMARY[(method, path)]
        client = op.get("x-luxmap-client", "shared")
        if client == "mobile":
            op["summary"] = "[Mobile] " + op["summary"]
        elif client == "web":
            op["summary"] = "[Web] " + op["summary"]
        elif method == "get" and path == "/api/v1/auth/me":
            op["summary"] = "[Dùng chung] " + op["summary"]
        op["operationId"] = opid(method, path)
        op["x-luxmap-status"] = "implemented"
        for prefix, sec in SECTION.items():
            if path.startswith(prefix):
                op["x-luxmap-contract"] = sec
                break
        # The auth group is anonymous EXCEPT /auth/me, which requires the bearer token (§4.7).
        is_anonymous = path.startswith("/api/v1/auth") and path != "/api/v1/auth/me"
        if is_anonymous and "security" not in op:
            op["security"] = []          # AllowAnonymous
        if not is_anonymous:
            op["responses"].setdefault("401", err("UNAUTHENTICATED — thiếu / sai / hết hạn access token"))
        # Contract v1.7 §2: every business operation requires ONE capability, published by the code.
        if "x-luxmap-capability" in op:
            op["responses"].setdefault("403", err(
                "ROLE_FORBIDDEN — vai trò không nằm trong x-luxmap-roles; "
                "COMMUNE_FORBIDDEN — commune_id ngoài phạm vi claim"))

# ── shared new schemas ─────────────────────────────────────────────────────────
def pid(prefix, digits):
    return {"type": "string", "pattern": f"^{prefix}-[0-9]{{{digits},}}$",
            "description": f"Contract §1.2 — số chữ số là SÀN ({digits}), không phải trần. Chuỗi đục, không parse."}

S["PoleId"] = pid("POLE", 4)
S["FaultId"] = pid("FAULT", 4)
S["SegmentId"] = pid("SEG", 3)
S["CommuneId"] = pid("COM", 3)
# FixtureId, UserId: dùng pattern nội tuyến (nullable) — không khai component để khỏi thừa
S["NodeId"] = pid("NODE", 3)
S["SweepId"] = pid("SWP", 3)
S["FrameId"] = pid("FRM", 6)
S["WorkOrderId"] = pid("WO", 4)

S["LatLng"] = {"type": "object", "required": ["lat", "lng"], "additionalProperties": False,
               "properties": {"lat": {"type": "number", "format": "double", "minimum": -90, "maximum": 90},
                              "lng": {"type": "number", "format": "double", "minimum": -180, "maximum": 180}}}

S["PoleProperties"] = {
    "type": "object", "additionalProperties": False,
    "description": "Contract §5.1 — 15 thuộc tính phẳng. Không có data_source / external_ref / feeder_id (không emit).",
    "required": ["pole_id", "segment_id", "fixture_status", "power_source", "fixture_type", "lamp_watt",
                 "install_date", "commune_id", "open_fault_count", "has_iot_node", "near_sensitive_poi"],
    "properties": OrderedDict([
        ("pole_id", {"$ref": "#/components/schemas/PoleId"}),
        ("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
        ("fixture_status", {"$ref": "#/components/schemas/FixtureStatus"}),
        ("status_confidence", {"type": "number", "format": "double", "minimum": 0, "maximum": 1, "nullable": True,
                               "description": "null khi và chỉ khi fixture_status = unknown (CHECK ck_pole_current_status_confidence_matches_status)"}),
        ("power_source", {"$ref": "#/components/schemas/PowerSource"}),
        ("fixture_type", {"$ref": "#/components/schemas/FixtureType"}),
        ("lamp_watt", {"type": "integer", "format": "int32"}),
        ("install_date", {"type": "string", "format": "date"}),
        ("warranty_expiry", {"type": "string", "format": "date", "nullable": True}),
        ("commune_id", {"$ref": "#/components/schemas/CommuneId"}),
        ("last_seen_at", {"type": "string", "format": "date-time", "nullable": True}),
        ("last_sweep_id", {"type": "string", "nullable": True, "pattern": "^SWP-[0-9]{3,}$"}),
        ("open_fault_count", {"type": "integer", "format": "int32", "minimum": 0,
                              "description": "Đếm theo tập fault MỞ = detected|confirmed|in_progress (Contract §3.2)"}),
        ("has_iot_node", {"type": "boolean"}),
        ("near_sensitive_poi", {"type": "boolean"}),
    ])}

S["SegmentProperties"] = {
    "type": "object", "additionalProperties": False,
    "description": "Contract §5.2 — 7 thuộc tính. commune_id / data_source / external_ref KHÔNG emit.",
    "required": ["segment_id", "segment_name", "road_class", "length_m", "pole_count", "controller_node_ids", "has_active_segment_fault"],
    "properties": OrderedDict([
        ("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
        ("segment_name", {"type": "string"}),
        ("road_class", {"$ref": "#/components/schemas/RoadClass"}),
        ("length_m", {"type": "integer", "format": "int32", "description": "Giá trị KHAI BÁO, không dẫn xuất từ ST_Length"}),
        ("pole_count", {"type": "integer", "format": "int32", "minimum": 0}),
        ("controller_node_ids", {"type": "array", "items": {"$ref": "#/components/schemas/NodeId"},
                                 "description": "I-7b: thiết bị điều khiển feeder của cột trên tuyến, tính lúc đọc; [] khi không có — không bao giờ null"}),
        ("has_active_segment_fault", {"type": "boolean", "description": "true → FE highlight cả tuyến (đầu ra CV-15)"}),
    ])}
# BE-43: PointGeometry / LineStringGeometry / PoleFeature(Collection) / SegmentFeature(Collection) were only
# referenced by the hand-written SyncBundle stub; with it gone they were orphans. The map layers are exported
# from the code; PoleProperties / SegmentProperties above still override those exported schemas.

# BE-20: PoleDetail and its parts (PoleDetailFixture/Status/Baseline/HistoryPoint/OpenFault/Frame) now come
# from the live code. The hand-written copies stood here and would OVERWRITE the exported schemas
# (CLAUDE.md, BE-14b) — they described the Contract's per-pole iot_node and runtime_history, which drift I-1 and
# I-9 removed.

# BE-40: FaultItem and its page now come from the live code (FaultItem / FaultItemPagedResult).
# The hand-written copies stood here and would OVERWRITE the exported schema (CLAUDE.md, BE-14b).
# BE-19: PatchFaultRequest now comes from the live code; the hand-written copy would overwrite it.
# BE-41: ReportFaultRequest / ReportedFault come from the live code; the hand-written CreateFaultRequest
# (with photo_frame_id, removed by EV-2) and CreatedFaultResponse would describe the old shape.

# BE-23: work-order schemas now come from the live code. Do not replace their
# task_kind, partial-PATCH semantics or detail shape with the old §5.5 placeholders.
# BE-43: SyncBundle / SyncPushRequest / SyncPushResult come from the live code. The hand-written §5.8
# proposals stood here and would OVERWRITE them (SyncPushRequest has the same name).

# ── NOT IMPLEMENTED operations ─────────────────────────────────────────────────
def p(name, schema, required=False, desc=None, where="query"):
    o = {"name": name, "in": where, "required": required, "schema": schema}
    if desc:
        o["description"] = desc
    return o

BBOX = p("bbox", {"type": "string", "pattern": r"^-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?$"}, True,
         "minLng,minLat,maxLng,maxLat — EPSG:4326. BẮT BUỘC, không có endpoint lấy tất cả.")

def ni(method, path, tag, summary, section, ticket, responses, parameters=None, body=None, body_ct="application/json", extra=None):
    op = OrderedDict()
    op["tags"] = [tag]
    op["summary"] = f"[NOT IMPLEMENTED] {summary}"
    op["description"] = f"Contract {section}. Chưa có code — ticket {ticket}. Giữ trong Contract, không xoá."
    op["operationId"] = opid(method, path)
    op["x-luxmap-status"] = "not_implemented"
    op["x-luxmap-client"] = "shared"   # no client-specific endpoint is still unimplemented
    op["x-luxmap-contract"] = section
    op["x-luxmap-ticket"] = ticket
    if parameters:
        op["parameters"] = parameters
    if body:
        op["requestBody"] = {"required": True, "content": {body_ct: {"schema": body}}}
    op["responses"] = OrderedDict(responses)
    op["responses"].setdefault("401", err("UNAUTHENTICATED"))
    if extra:
        op.update(extra)
    d["paths"].setdefault(path, OrderedDict())[method] = op

ENUM_CSV = lambda ref: {"type": "string", "description": f"CSV của {ref}"}

# ⚠️ /api/v1/poles and /api/v1/segments USED TO BE DECLARED HERE as not_implemented. BE-14 serves
# them, so the hand-written versions are gone and the ones EXPORTED FROM THE CODE stand. Leaving
# them would have been worse than untidy: ni() writes the same path key, so the stub OVERWROTE the
# real operation — the consolidated spec kept calling a shipped endpoint unimplemented, and the
# response schemas the exporter had just emitted became orphans. Redocly caught exactly that as two
# no-unused-components warnings.
#
# The lesson for the next ticket that implements a stub: DELETE ITS ni() CALL in the same commit, and
# read the operation counter in the output line.

# BE-41 implements POST /faults; the stub is gone.
# BE-24 implements the evidence upload; the stub and its hand-written EvidenceUpload schema are gone.
# BE-15 P2c implements the authenticated JPEG thumbnail endpoint.
# BE-43 implements GET /sync/bundle and POST /sync/push; the stubs and their hand-written schemas are gone.

# Order paths: implemented first in original order, then the rest as inserted.
json.dump(d, open(DST, "w"), ensure_ascii=False, indent=2)
n_impl = sum(1 for _, i in d["paths"].items() for m, o in i.items() if isinstance(o, dict) and o.get("x-luxmap-status") == "implemented")
n_ni = sum(1 for _, i in d["paths"].items() for m, o in i.items() if isinstance(o, dict) and o.get("x-luxmap-status") == "not_implemented")
print(f"wrote {DST}: paths={len(d['paths'])} implemented_ops={n_impl} not_implemented_ops={n_ni} schemas={len(S)}")
