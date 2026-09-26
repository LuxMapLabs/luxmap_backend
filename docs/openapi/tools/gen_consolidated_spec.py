#!/usr/bin/env python3
"""Builds docs/openapi/luxmap-v1.5.json — the CONSOLIDATED spec that matches api-contract-v1.1.md 1-1.

Run from the repository root after regenerating luxmap-v1.json from code (README):

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
d["info"]["version"] = "1.7"
d["info"]["title"] = "LuxMap API"
# ĐẾM, không gõ tay. Con số này từng là hằng số và nó lệch ngay lần thêm endpoint kế tiếp — cùng lớp
# lỗi với cái tên file `luxmap-v1.4.json` đã trỏ vào hư không. Nguồn chỉ chứa operation đã hiện thực.
HTTP_METHODS = ("get", "post", "put", "patch", "delete")
n_from_code = sum(1 for item in d["paths"].values() for m in item if m in HTTP_METHODS)

d["info"]["description"] = (
    "Bản hợp nhất khớp 1-1 với docs/api-contract-v1.1.md (Contract v1.7, 25/09/2026). "
    "Sinh bằng docs/openapi/tools/gen_consolidated_spec.py từ docs/openapi/luxmap-v1.json (spec xuất từ "
    f"code, {n_from_code} operation implemented) cộng các endpoint Contract chưa có code (x-luxmap-status = "
    "not_implemented). Quy ước: JSON snake_case, enum chuỗi thường, ISO 8601 UTC hậu tố Z, EPSG:4326, "
    "base path /api/v1."
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

# ── tags ───────────────────────────────────────────────────────────────────────
TAGS = OrderedDict([
    ("Auth", "Nhóm mobile — Contract §4.1. Refresh token trong body."),
    ("WebAuth", "Nhóm web — Contract §4.2. Refresh token chỉ trong cookie HttpOnly __Secure-luxmap_rt."),
    ("Assets", "Quản lý kiểm kê tài sản — Contract §5.3. Không phải endpoint bản đồ §5.1. Ghi = Quản lý (cap:manage_assets); đọc = cap:read_network."),
    ("AssetImport", "Nhập tài sản CSV/GeoJSON theo từng loại file — Contract §5.3."),
    ("LuxReadings", "Số đo sáng TƯƠNG ĐỐI bằng điện thoại — Contract §5.7. Ghi = Kỹ sư hiện trường (cap:record_lux_reading)."),
    ("Poles", "Cột đèn trên bản đồ — Contract §5.1. CHƯA HIỆN THỰC (BE-14, BE-20)."),
    ("Segments", "Đoạn đường — Contract §5.2. CHƯA HIỆN THỰC (BE-14)."),
    ("Faults", "Sự cố — Contract §5.4. CHƯA HIỆN THỰC (BE-40, BE-19, BE-41)."),
    ("WorkOrders", "Phiếu công việc — Contract §5.5. CHƯA HIỆN THỰC (BE-21..BE-24)."),
    ("IotSweeps", "IoT node, sweep, thumbnail — Contract §5.6. CHƯA HIỆN THỰC (BE-14, BE-17, BE-15)."),
    ("Sync", "Đồng bộ offline — Contract §5.8. CHƯA HIỆN THỰC (BE-43)."),
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
    ("get", "/api/v1/assets/segments"): "Danh sách ID tuyến (chỗ giữ chỗ BE-12b)",
    ("post", "/api/v1/assets/segments"): "Tạo tuyến đường; 201 + Location, không body",
    ("get", "/api/v1/assets/feeders"): "Danh sách ID mạch điện (chỗ giữ chỗ BE-12b)",
    ("post", "/api/v1/assets/feeders"): "Tạo mạch điện; 201 + Location, không body",
    ("get", "/api/v1/assets/poles"): "Danh sách ID cột (chỗ giữ chỗ BE-12b)",
    ("post", "/api/v1/assets/poles"): "Tạo cột; 201 + Location, không body",
    ("post", "/api/v1/assets/fixtures"): "Ghi một lần lắp bóng; commune_id chép từ cột",
    ("put", "/api/v1/assets/segments/{segmentId}"): "Thay thế TOÀN PHẦN một tuyến; commune_id không sửa được",
    ("delete", "/api/v1/assets/segments/{segmentId}"): "Xoá tuyến; khoá ngoại quyết định (409 ASSET_IN_USE)",
    ("put", "/api/v1/assets/feeders/{feederId}"): "Thay thế TOÀN PHẦN một mạch điện; commune_id không sửa được",
    ("delete", "/api/v1/assets/feeders/{feederId}"): "Xoá mạch điện; còn cột đang đấu vào thì 409 ASSET_IN_USE",
    ("put", "/api/v1/assets/poles/{poleId}"): "Thay thế TOÀN PHẦN một cột; THIẾU feeder_id là XOÁ mạch của cột",
    ("delete", "/api/v1/assets/poles/{poleId}"): "Xoá cột; khoá ngoại quyết định (409 ASSET_IN_USE)",
    ("put", "/api/v1/assets/poles/{poleId}/feeder"): "Gán hoặc xoá mạch điện của cột (feeder_id null = không mạch)",
    # BE-14 — endpoint bản đồ, đặc tả đầy đủ ở Contract mục 5.1–5.2.
    ("get", "/api/v1/poles"): "Bản đồ cột theo bbox; FeatureCollection, properties phẳng; quá 2000 cột → 413 BBOX_TOO_LARGE",
    ("get", "/api/v1/segments"): "Bản đồ tuyến theo bbox; FeatureCollection của LineString",
    # BE-13 topology — ⚠️ PROVISIONAL, ngoài Contract, drift 46.
    ("get", "/api/v1/assets/feeders/{feederId}/poles"): "[TẠM — drift 46] Cột trên một mạch điện; đầu vào CV-15. Mạch ngoài phạm vi xã → 404",
    ("get", "/api/v1/assets/segments/{segmentId}/poles"): "[TẠM — drift 46] Cột trên một tuyến; có thể gồm cột của xã khác (inter_commune)",
    ("get", "/api/v1/assets/feeders/poles"): "[TẠM — drift 46] Cột CHƯA có mạch; bắt buộc unassigned=true; chỉ listing này trả power_source",
    ("put", "/api/v1/assets/fixtures/{fixtureId}/removal"): "Ngừng dùng bóng bằng removed_date; không có DELETE",
    ("get", "/api/v1/auth/me"): "Người đang đăng nhập, đọc từ DB nên role và commune_ids luôn tươi",
    ("post", "/api/v1/auth/login"): "Đăng nhập mobile; trả đúng bốn trường",
    ("post", "/api/v1/auth/register"): "Đăng ký mở; tạo danh tính, không tạo quyền",
    ("post", "/api/v1/auth/refresh"): "Xoay vòng refresh token (mobile)",
    ("post", "/api/v1/auth/logout"): "Thu hồi refresh token (mobile); luôn 204",
    ("post", "/api/v1/lux-readings"): "Ghi số đo lux; trùng client_op_id trả 200 kèm bản ghi cũ",
    ("get", "/api/v1/lux-readings"): "Kéo số đo lux hàng loạt kèm nearest_luminance (hiện luôn null)",
    ("get", "/api/v1/lux-readings/poles/{poleId}"): "Chuỗi số đo của một cột, sắp theo measured_at, có phân trang",
    ("post", "/api/v1/auth/web/login"): "Đăng nhập web; refresh token vào cookie",
    ("post", "/api/v1/auth/web/refresh"): "Xoay vòng refresh token từ cookie; không body",
    ("post", "/api/v1/auth/web/logout"): "Thu hồi token trong cookie, luôn xoá cookie, luôn 204",
}
SECTION = {
    "/api/v1/auth/me": "§4.7",
    "/api/v1/assets": "§5.3",
    "/api/v1/auth/web": "§4.2",
    "/api/v1/auth": "§4.1",
    "/api/v1/lux-readings": "§5.7",
}

for path, item in d["paths"].items():
    for method, op in item.items():
        if method not in HTTP_METHODS:
            continue
        op["summary"] = SUMMARY[(method, path)]
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
S["ClusterId"] = pid("CLS", 3)

S["LatLng"] = {"type": "object", "required": ["lat", "lng"], "additionalProperties": False,
               "properties": {"lat": {"type": "number", "format": "double", "minimum": -90, "maximum": 90},
                              "lng": {"type": "number", "format": "double", "minimum": -180, "maximum": 180}}}
S["PointGeometry"] = {"type": "object", "required": ["type", "coordinates"], "additionalProperties": False,
                      "properties": {"type": {"type": "string", "enum": ["Point"]},
                                     "coordinates": {"type": "array", "minItems": 2, "maxItems": 2,
                                                     "items": {"type": "number", "format": "double"},
                                                     "description": "[lng, lat] — EPSG:4326"}}}
S["LineStringGeometry"] = {"type": "object", "required": ["type", "coordinates"], "additionalProperties": False,
                           "properties": {"type": {"type": "string", "enum": ["LineString"]},
                                          "coordinates": {"type": "array", "minItems": 2,
                                                          "items": {"type": "array", "minItems": 2, "maxItems": 2,
                                                                    "items": {"type": "number", "format": "double"}},
                                                          "description": "[[lng, lat], …] — EPSG:4326"}}}

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
S["PoleFeature"] = {"type": "object", "required": ["type", "geometry", "properties"], "additionalProperties": False,
                    "description": "Không dùng feature.id — dùng properties.pole_id.",
                    "properties": {"type": {"type": "string", "enum": ["Feature"]},
                                   "geometry": {"$ref": "#/components/schemas/PointGeometry"},
                                   "properties": {"$ref": "#/components/schemas/PoleProperties"}}}
S["PoleFeatureCollection"] = {"type": "object", "required": ["type", "features"], "additionalProperties": False,
                              "properties": {"type": {"type": "string", "enum": ["FeatureCollection"]},
                                             "features": {"type": "array", "items": {"$ref": "#/components/schemas/PoleFeature"}}}}

S["SegmentProperties"] = {
    "type": "object", "additionalProperties": False,
    "description": "Contract §5.2 — 7 thuộc tính. commune_id / data_source / external_ref KHÔNG emit.",
    "required": ["segment_id", "segment_name", "road_class", "length_m", "pole_count", "has_active_segment_fault"],
    "properties": OrderedDict([
        ("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
        ("segment_name", {"type": "string"}),
        ("road_class", {"$ref": "#/components/schemas/RoadClass"}),
        ("length_m", {"type": "integer", "format": "int32", "description": "Giá trị KHAI BÁO, không dẫn xuất từ ST_Length"}),
        ("pole_count", {"type": "integer", "format": "int32", "minimum": 0}),
        ("controller_node_id", {"$ref": "#/components/schemas/NodeId"}),
        ("has_active_segment_fault", {"type": "boolean", "description": "true → FE highlight cả tuyến (đầu ra CV-15)"}),
    ])}
S["SegmentProperties"]["properties"]["controller_node_id"] = {"type": "string", "nullable": True, "pattern": "^NODE-[0-9]{3,}$"}
S["SegmentFeature"] = {"type": "object", "required": ["type", "geometry", "properties"], "additionalProperties": False,
                       "properties": {"type": {"type": "string", "enum": ["Feature"]},
                                      "geometry": {"$ref": "#/components/schemas/LineStringGeometry"},
                                      "properties": {"$ref": "#/components/schemas/SegmentProperties"}}}
S["SegmentFeatureCollection"] = {"type": "object", "required": ["type", "features"], "additionalProperties": False,
                                 "properties": {"type": {"type": "string", "enum": ["FeatureCollection"]},
                                                "features": {"type": "array", "items": {"$ref": "#/components/schemas/SegmentFeature"}}}}

S["PoleDetailFixture"] = {"type": "object", "additionalProperties": False,
    "required": ["fixture_type", "power_source", "lamp_watt", "install_date"],
    "properties": OrderedDict([
        ("fixture_type", {"$ref": "#/components/schemas/FixtureType"}),
        ("power_source", {"$ref": "#/components/schemas/PowerSource"}),
        ("lamp_watt", {"type": "integer", "format": "int32"}),
        ("install_date", {"type": "string", "format": "date"}),
        ("warranty_expiry", {"type": "string", "format": "date", "nullable": True}),
    ]),
    "description": "Thông số lắp đặt của bóng ĐANG DÙNG (duy nhất, Contract §3.3)."}
S["PoleCurrentStatus"] = {"type": "object", "additionalProperties": False,
    "required": ["fixture_status", "determined_at", "source_channel"],
    "properties": OrderedDict([
        ("fixture_status", {"$ref": "#/components/schemas/FixtureStatus"}),
        ("status_confidence", {"type": "number", "format": "double", "minimum": 0, "maximum": 1, "nullable": True}),
        ("determined_at", {"type": "string", "format": "date-time"}),
        ("source_channel", {"$ref": "#/components/schemas/SourceChannel"}),
    ])}
S["PoleIotNode"] = {"type": "object", "additionalProperties": False,
    "required": ["node_id", "node_status", "last_report_at"],
    "properties": OrderedDict([
        ("node_id", {"$ref": "#/components/schemas/NodeId"}),
        ("node_status", {"$ref": "#/components/schemas/NodeStatus"}),
        ("last_report_at", {"type": "string", "format": "date-time", "nullable": True}),
    ])}
S["LuminanceBaseline"] = {"type": "object", "additionalProperties": False,
    "required": ["baseline_value", "baseline_window_nights", "dim_threshold_ratio", "out_threshold_ratio", "computed_at"],
    "description": "Ngưỡng cấu hình qua BE-33 (mặc định dim 0.80, out 0.15), không hard-code.",
    "properties": OrderedDict([
        ("baseline_value", {"type": "number", "format": "double"}),
        ("baseline_window_nights", {"type": "integer", "format": "int32"}),
        ("dim_threshold_ratio", {"type": "number", "format": "double"}),
        ("out_threshold_ratio", {"type": "number", "format": "double"}),
        ("computed_at", {"type": "string", "format": "date-time"}),
    ])}
S["LuminancePoint"] = {"type": "object", "additionalProperties": False,
    "required": ["observed_at", "sweep_id", "normalized_luminance", "baseline_ratio", "classified_as"],
    "description": "baseline_ratio và classified_as TÍNH Ở BACKEND — FE không tự tính.",
    "properties": OrderedDict([
        ("observed_at", {"type": "string", "format": "date-time"}),
        ("sweep_id", {"type": "string"}),
        ("normalized_luminance", {"type": "number", "format": "double"}),
        ("baseline_ratio", {"type": "number", "format": "double"}),
        ("classified_as", {"$ref": "#/components/schemas/FixtureStatus"}),
    ])}
S["RuntimePoint"] = {"type": "object", "additionalProperties": False,
    "required": ["night_of", "runtime_hours", "source"],
    "description": "Phiên đêm cắt qua nửa đêm; night_of là ngày bắt đầu phiên.",
    "properties": OrderedDict([
        ("night_of", {"type": "string", "format": "date"}),
        ("runtime_hours", {"type": "number", "format": "double"}),
        ("on_at", {"type": "string", "nullable": True, "description": "HH:mm:ssZ theo mock — định dạng chưa chốt"}),
        ("off_at", {"type": "string", "nullable": True}),
        ("source", {"type": "string", "enum": ["iot"]}),
    ])}
S["OpenFaultSummary"] = {"type": "object", "additionalProperties": False,
    "required": ["fault_id", "fault_type", "severity", "fault_status"],
    "properties": OrderedDict([
        ("fault_id", {"$ref": "#/components/schemas/FaultId"}),
        ("fault_type", {"$ref": "#/components/schemas/FaultType"}),
        ("severity", {"$ref": "#/components/schemas/Severity"}),
        ("fault_status", {"$ref": "#/components/schemas/FaultStatus"}),
        ("priority_score", {"type": "number", "format": "double", "nullable": True}),
    ])}
S["RecentFrame"] = {"type": "object", "additionalProperties": False,
    "required": ["frame_id", "sweep_id", "captured_at", "thumbnail_url"],
    "properties": OrderedDict([
        ("frame_id", {"$ref": "#/components/schemas/FrameId"}),
        ("sweep_id", {"$ref": "#/components/schemas/SweepId"}),
        ("captured_at", {"type": "string", "format": "date-time"}),
        ("thumbnail_url", {"type": "string", "description": "Đường dẫn TƯƠNG ĐỐI qua API, không presigned: /api/v1/frames/{frame_id}/thumbnail"}),
        ("distance_m", {"type": "number", "format": "double"}),
        ("heading_deg", {"type": "number", "format": "double"}),
    ])}
S["PoleDetail"] = {"type": "object", "additionalProperties": False,
    "description": "Contract §5.1 — đủ trong MỘT request. Hình dạng theo mock-pole-detail.json.",
    "required": ["pole_id", "segment_id", "segment_name", "commune_id", "location", "fixture", "current_status",
                 "iot_node", "luminance_baseline", "luminance_history", "open_faults", "recent_frames"],
    "properties": OrderedDict([
        ("pole_id", {"$ref": "#/components/schemas/PoleId"}),
        ("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
        ("segment_name", {"type": "string"}),
        ("commune_id", {"$ref": "#/components/schemas/CommuneId"}),
        ("location", {"$ref": "#/components/schemas/LatLng"}),
        ("fixture", {"$ref": "#/components/schemas/PoleDetailFixture"}),
        ("current_status", {"$ref": "#/components/schemas/PoleCurrentStatus"}),
        ("iot_node", {"nullable": True, "allOf": [{"$ref": "#/components/schemas/PoleIotNode"}], "type": "object"}),
        ("luminance_baseline", {"$ref": "#/components/schemas/LuminanceBaseline"}),
        ("luminance_history", {"type": "array", "items": {"$ref": "#/components/schemas/LuminancePoint"}}),
        ("runtime_history", {"type": "array", "nullable": True, "items": {"$ref": "#/components/schemas/RuntimePoint"},
                             "description": "Chỉ có khi cột có IoT node"}),
        ("open_faults", {"type": "array", "items": {"$ref": "#/components/schemas/OpenFaultSummary"}}),
        ("recent_frames", {"type": "array", "items": {"$ref": "#/components/schemas/RecentFrame"}}),
    ])}

S["FaultItem"] = {"type": "object", "additionalProperties": False,
    "description": "Contract §5.4 — một item. work_order_id LUÔN null cho tới BE-21.",
    "required": ["fault_id", "location", "fault_type", "fault_status", "severity", "source_channel", "data_source",
                 "detected_at", "updated_at", "work_order_id"],
    "properties": OrderedDict([
        ("fault_id", {"$ref": "#/components/schemas/FaultId"}),
        ("pole_id", {"type": "string", "nullable": True, "pattern": "^POLE-[0-9]{4,}$"}),
        ("fixture_id", {"type": "string", "nullable": True, "pattern": "^FIX-[0-9]{4,}$"}),
        ("segment_id", {"type": "string", "nullable": True, "pattern": "^SEG-[0-9]{3,}$"}),
        ("location", {"$ref": "#/components/schemas/LatLng"}),
        ("fault_type", {"$ref": "#/components/schemas/FaultType"}),
        ("fault_status", {"$ref": "#/components/schemas/FaultStatus"}),
        ("severity", {"$ref": "#/components/schemas/Severity"}),
        ("source_channel", {"$ref": "#/components/schemas/SourceChannel"}),
        ("data_source", {"$ref": "#/components/schemas/DataSource"}),
        ("priority_score", {"type": "number", "format": "double", "nullable": True, "description": "CV-16 tính; null khi chưa chấm"}),
        ("status_confidence", {"type": "number", "format": "double", "minimum": 0, "maximum": 1, "nullable": True}),
        ("cluster_id", {"type": "string", "nullable": True, "pattern": "^CLS-[0-9]{3,}$"}),
        ("detected_at", {"type": "string", "format": "date-time"}),
        ("updated_at", {"type": "string", "format": "date-time"}),
        ("work_order_id", {"type": "string", "nullable": True, "pattern": "^WO-[0-9]{4,}$"}),
        ("note", {"type": "string", "nullable": True}),
        ("reported_by", {"type": "string", "nullable": True, "pattern": "^USR-[0-9]{3,}$"}),
    ])}
S["FaultPagedResult"] = {"type": "object", "additionalProperties": False,
    "required": ["page", "page_size", "total", "items"],
    "properties": OrderedDict([
        ("page", {"type": "integer", "format": "int32"}),
        ("page_size", {"type": "integer", "format": "int32", "maximum": 200}),
        ("total", {"type": "integer", "format": "int32"}),
        ("items", {"type": "array", "items": {"$ref": "#/components/schemas/FaultItem"}}),
    ])}
S["PatchFaultRequest"] = {"type": "object", "additionalProperties": False, "required": ["fault_status"],
    "description": "Contract §5.4 (PATCH). Chuyển sai luồng → 409.",
    "properties": OrderedDict([
        ("fault_status", {"$ref": "#/components/schemas/FaultStatus"}),
        ("override_fault_type", {"nullable": True, "allOf": [{"$ref": "#/components/schemas/FaultType"}], "type": "string"}),
        ("note", {"type": "string", "nullable": True}),
    ])}
S["CreateFaultRequest"] = {"type": "object", "additionalProperties": False,
    "required": ["client_op_id", "fault_type", "note"],
    "description": "Contract §5.4 (POST). Server áp cứng source_channel=field_report, data_source=field, fault_status=detected, reported_by=JWT.",
    "properties": OrderedDict([
        ("client_op_id", {"type": "string", "format": "uuid"}),
        ("pole_id", {"type": "string", "nullable": True, "pattern": "^POLE-[0-9]{4,}$"}),
        ("fixture_id", {"type": "string", "nullable": True, "pattern": "^FIX-[0-9]{4,}$"}),
        ("location", {"nullable": True, "allOf": [{"$ref": "#/components/schemas/LatLng"}], "type": "object",
                      "description": "Bắt buộc khi pole_id null (LOCATION_REQUIRED)"}),
        ("commune_id", {"type": "string", "nullable": True, "pattern": "^COM-[0-9]{3,}$",
                        "description": "Chỉ khi pole_id null VÀ user có nhiều xã. Có pole thì server tra từ pole (gửi → 400)."}),
        ("fault_type", {"$ref": "#/components/schemas/FaultType"}),
        ("severity", {"nullable": True, "allOf": [{"$ref": "#/components/schemas/Severity"}], "type": "string", "description": "Mặc định medium"}),
        ("note", {"type": "string", "minLength": 10}),
        ("photo_frame_id", {"type": "string", "nullable": True}),
    ])}
S["CreatedFaultResponse"] = {"allOf": [{"$ref": "#/components/schemas/FaultItem"},
                                        {"type": "object", "required": ["client_op_id"],
                                         "properties": {"client_op_id": {"type": "string", "format": "uuid"}}}],
                             "description": "§5.4: item kèm client_op_id đã gửi lên."}

S["WorkOrderItem"] = {"type": "object", "additionalProperties": False,
    "description": "Contract §5.5 — hình dạng theo mock-work-orders.json.",
    "required": ["work_order_id", "title", "fault_ids", "wo_status", "created_at"],
    "properties": OrderedDict([
        ("work_order_id", {"$ref": "#/components/schemas/WorkOrderId"}),
        ("title", {"type": "string"}),
        ("segment_id", {"type": "string", "nullable": True, "pattern": "^SEG-[0-9]{3,}$"}),
        ("cluster_id", {"type": "string", "nullable": True, "pattern": "^CLS-[0-9]{3,}$"}),
        ("fault_ids", {"type": "array", "items": {"$ref": "#/components/schemas/FaultId"}}),
        ("wo_status", {"$ref": "#/components/schemas/WorkOrderStatus"}),
        ("assigned_to", {"type": "string", "nullable": True, "pattern": "^USR-[0-9]{3,}$"}),
        ("priority_score", {"type": "number", "format": "double", "nullable": True}),
        ("created_at", {"type": "string", "format": "date-time"}),
        ("due_date", {"type": "string", "format": "date", "nullable": True}),
    ])}
S["WorkOrderPagedResult"] = {"type": "object", "additionalProperties": False,
    "required": ["page", "page_size", "total", "items"],
    "properties": OrderedDict([
        ("page", {"type": "integer", "format": "int32"}),
        ("page_size", {"type": "integer", "format": "int32", "maximum": 200}),
        ("total", {"type": "integer", "format": "int32"}),
        ("items", {"type": "array", "items": {"$ref": "#/components/schemas/WorkOrderItem"}}),
    ])}
S["CreateWorkOrderRequest"] = {"type": "object", "additionalProperties": False, "required": ["title", "fault_ids"],
    "properties": OrderedDict([
        ("title", {"type": "string"}),
        ("fault_ids", {"type": "array", "minItems": 1, "items": {"$ref": "#/components/schemas/FaultId"}}),
        ("assigned_to", {"type": "string", "nullable": True}),
        ("due_date", {"type": "string", "format": "date", "nullable": True}),
    ])}
S["PatchWorkOrderRequest"] = {"type": "object", "additionalProperties": False,
    "description": "§5.5: đổi wo_status, gán người. Luồng wo_status: Open item O-3.",
    "properties": OrderedDict([
        ("wo_status", {"nullable": True, "allOf": [{"$ref": "#/components/schemas/WorkOrderStatus"}], "type": "string"}),
        ("assigned_to", {"type": "string", "nullable": True}),
    ])}
S["EvidenceUpload"] = {"type": "object", "required": ["file", "kind", "captured_at", "lat", "lng"],
    "properties": OrderedDict([
        ("file", {"type": "string", "format": "binary", "description": "JPEG, quyết bằng magic bytes FF D8 FF (BE-11)"}),
        ("kind", {"type": "string", "enum": ["before", "after"]}),
        ("captured_at", {"type": "string", "format": "date-time"}),
        ("lat", {"type": "number", "format": "double"}),
        ("lng", {"type": "number", "format": "double"}),
    ])}

S["IotNodeProperties"] = {"type": "object", "additionalProperties": False,
    "description": "Contract §5.6 — theo mock-iot-nodes.geojson.",
    "required": ["node_id", "node_role", "node_status", "segment_id"],
    "properties": OrderedDict([
        ("node_id", {"type": "string", "pattern": "^NODE-[0-9]{3,}$"}),
        ("node_role", {"$ref": "#/components/schemas/NodeRole"}),
        ("node_status", {"$ref": "#/components/schemas/NodeStatus"}),
        ("pole_id", {"type": "string", "nullable": True, "pattern": "^POLE-[0-9]{4,}$"}),
        ("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
        ("battery_pct", {"type": "number", "format": "double", "nullable": True}),
        ("last_report_at", {"type": "string", "format": "date-time", "nullable": True}),
    ])}
S["IotNodeFeature"] = {"type": "object", "required": ["type", "geometry", "properties"], "additionalProperties": False,
                       "properties": {"type": {"type": "string", "enum": ["Feature"]},
                                      "geometry": {"$ref": "#/components/schemas/PointGeometry"},
                                      "properties": {"$ref": "#/components/schemas/IotNodeProperties"}}}
S["IotNodeFeatureCollection"] = {"type": "object", "required": ["type", "features"], "additionalProperties": False,
                                 "properties": {"type": {"type": "string", "enum": ["FeatureCollection"]},
                                                "features": {"type": "array", "items": {"$ref": "#/components/schemas/IotNodeFeature"}}}}
S["SweepItem"] = {"type": "object", "additionalProperties": False,
    "description": "Contract §5.6. processing_status chưa có enum — Open item O-4.",
    "required": ["sweep_id", "started_at", "segment_ids", "frame_count", "coverage_pct", "processing_status", "data_source"],
    "properties": OrderedDict([
        ("sweep_id", {"$ref": "#/components/schemas/SweepId"}),
        ("started_at", {"type": "string", "format": "date-time"}),
        ("ended_at", {"type": "string", "format": "date-time", "nullable": True}),
        ("segment_ids", {"type": "array", "items": {"$ref": "#/components/schemas/SegmentId"}}),
        ("frame_count", {"type": "integer", "format": "int32", "minimum": 0}),
        ("coverage_pct", {"type": "number", "format": "double", "minimum": 0, "maximum": 100}),
        ("processing_status", {"type": "string"}),
        ("data_source", {"$ref": "#/components/schemas/DataSource"}),
    ])}
S["SweepPagedResult"] = {"type": "object", "additionalProperties": False,
    "required": ["page", "page_size", "total", "items"],
    "properties": OrderedDict([
        ("page", {"type": "integer", "format": "int32"}),
        ("page_size", {"type": "integer", "format": "int32", "maximum": 200}),
        ("total", {"type": "integer", "format": "int32"}),
        ("items", {"type": "array", "items": {"$ref": "#/components/schemas/SweepItem"}}),
    ])}

S["SyncBundle"] = {"type": "object", "additionalProperties": False,
    "description": "Contract §5.8 — hình dạng đề xuất, chốt ở FW kế tiếp (Open item O-5).",
    "required": ["poles", "segments", "open_faults", "work_orders", "generated_at"],
    "properties": OrderedDict([
        ("generated_at", {"type": "string", "format": "date-time"}),
        ("poles", {"$ref": "#/components/schemas/PoleFeatureCollection"}),
        ("segments", {"$ref": "#/components/schemas/SegmentFeatureCollection"}),
        ("open_faults", {"type": "array", "items": {"$ref": "#/components/schemas/FaultItem"}}),
        ("work_orders", {"type": "array", "items": {"$ref": "#/components/schemas/WorkOrderItem"}}),
    ])}
S["SyncOperation"] = {"type": "object", "additionalProperties": False, "required": ["client_op_id", "op_type", "payload"],
    "description": "Contract §5.8 — hình dạng đề xuất (Open item O-5); op_type dự kiến: create_fault | create_lux_reading | patch_fault | patch_work_order.",
    "properties": OrderedDict([
        ("client_op_id", {"type": "string", "format": "uuid"}),
        ("op_type", {"type": "string"}),
        ("payload", {"type": "object", "additionalProperties": True}),
    ])}
S["SyncPushRequest"] = {"type": "object", "additionalProperties": False, "required": ["operations"],
    "properties": {"operations": {"type": "array", "items": {"$ref": "#/components/schemas/SyncOperation"}}}}
S["SyncConflict"] = {"type": "object", "additionalProperties": False, "required": ["client_op_id", "reason"],
    "properties": OrderedDict([
        ("client_op_id", {"type": "string", "format": "uuid"}),
        ("reason", {"type": "string"}),
        ("server_state", {"type": "object", "additionalProperties": True, "nullable": True}),
    ])}
S["SyncPushResponse"] = {"type": "object", "additionalProperties": False, "required": ["applied", "conflicts"],
    "description": "Contract §5.8 — xung đột: server thắng, trả conflicts[]. Ánh xạ client_op_id → id thật (§1.2). Hình dạng đề xuất (Open item O-5).",
    "properties": OrderedDict([
        ("applied", {"type": "array", "items": {"type": "object", "additionalProperties": False, "required": ["client_op_id", "id"],
                                                 "properties": {"client_op_id": {"type": "string", "format": "uuid"},
                                                                "id": {"type": "string"}}}}),
        ("conflicts", {"type": "array", "items": {"$ref": "#/components/schemas/SyncConflict"}}),
    ])}

# ── NOT IMPLEMENTED operations ─────────────────────────────────────────────────
def p(name, schema, required=False, desc=None, where="query"):
    o = {"name": name, "in": where, "required": required, "schema": schema}
    if desc:
        o["description"] = desc
    return o

BBOX = p("bbox", {"type": "string", "pattern": r"^-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?,-?\d+(\.\d+)?$"}, True,
         "minLng,minLat,maxLng,maxLat — EPSG:4326. BẮT BUỘC, không có endpoint lấy tất cả.")
PAGE = [p("page", {"type": "integer", "format": "int32", "minimum": 1, "default": 1}),
        p("page_size", {"type": "integer", "format": "int32", "minimum": 1, "maximum": 200, "default": 50},
          desc="Vượt 200 bị kẹp im lặng về 200 — client đọc page_size trong response (Contract §1.3)")]

def ni(method, path, tag, summary, section, ticket, responses, parameters=None, body=None, body_ct="application/json", extra=None):
    op = OrderedDict()
    op["tags"] = [tag]
    op["summary"] = f"[NOT IMPLEMENTED] {summary}"
    op["description"] = f"Contract {section}. Chưa có code — ticket {ticket}. Giữ trong Contract, không xoá."
    op["operationId"] = opid(method, path)
    op["x-luxmap-status"] = "not_implemented"
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

ni("get", "/api/v1/poles/{pole_id}", "Poles", "Chi tiết cột + lịch sử, đủ trong MỘT request", "§5.1", "BE-20",
   [("200", {"description": "Chi tiết cột", "content": json_content("PoleDetail")}),
    ("404", err("Không tồn tại HOẶC ngoài phạm vi xã — cùng một câu trả lời (§7)"))],
   parameters=[p("pole_id", {"$ref": "#/components/schemas/PoleId"}, True, where="path")])
ni("get", "/api/v1/faults", "Faults", "Danh sách sự cố — phân trang JSON, KHÔNG phải GeoJSON", "§5.4", "BE-40",
   [("200", {"description": "Trang sự cố; sắp mặc định -priority_score", "content": json_content("FaultPagedResult")}),
    ("403", err("COMMUNE_FORBIDDEN"))],
   parameters=[p("bbox", {"type": "string"}, desc="Tuỳ chọn ở endpoint này"),
               p("status", ENUM_CSV("fault_status")), p("severity", ENUM_CSV("severity")),
               p("fault_type", ENUM_CSV("fault_type")), p("source_channel", ENUM_CSV("source_channel")),
               p("data_source", ENUM_CSV("data_source")),
               p("pole_id", {"$ref": "#/components/schemas/PoleId"},
                 desc="MỘT giá trị. Cột không tồn tại và cột ngoài phạm vi trả GIỐNG NHAU: 200 + rỗng (Contract §5.4)"),
               p("segment_id", {"$ref": "#/components/schemas/SegmentId"}),
               p("cluster_id", {"$ref": "#/components/schemas/ClusterId"}),
               p("sort", {"type": "string", "default": "-priority_score"})] + PAGE)
ni("patch", "/api/v1/faults/{fault_id}", "Faults", "Kỹ sư xác nhận / bác bỏ / phân loại lại", "§5.4", "BE-19",
   [("200", {"description": "Fault sau khi đổi", "content": json_content("FaultItem")}),
    ("400", err("VALIDATION_FAILED")),
    ("404", err("Không tồn tại hoặc ngoài phạm vi")),
    ("409", err("Chuyển trạng thái sai luồng: detected→confirmed|rejected; confirmed→in_progress→resolved→verified"))],
   parameters=[p("fault_id", {"$ref": "#/components/schemas/FaultId"}, True, where="path")],
   body={"$ref": "#/components/schemas/PatchFaultRequest"})
ni("post", "/api/v1/faults", "Faults", "Kỹ sư hiện trường báo sự cố tại chỗ", "§5.4", "BE-41",
   [("201", {"description": "Fault đầy đủ kèm client_op_id", "content": json_content("CreatedFaultResponse")}),
    ("200", {"description": "DUPLICATE_OP — client_op_id đã xử lý, trả fault đã tạo (KHÔNG phải lỗi)", "content": json_content("CreatedFaultResponse")}),
    ("400", err("LOCATION_REQUIRED | FAULT_TYPE_NOT_REPORTABLE | VALIDATION_FAILED")),
    ("404", err("POLE_NOT_FOUND"))],
   body={"$ref": "#/components/schemas/CreateFaultRequest"})
ni("get", "/api/v1/work-orders", "WorkOrders", "Danh sách phiếu công việc (phân trang)", "§5.5", "BE-21",
   [("200", {"description": "Trang phiếu", "content": json_content("WorkOrderPagedResult")})],
   parameters=[p("wo_status", ENUM_CSV("wo_status")), p("assigned_to", {"type": "string"}),
               p("segment_id", {"$ref": "#/components/schemas/SegmentId"})] + PAGE)
ni("post", "/api/v1/work-orders", "WorkOrders", "Tạo phiếu công việc từ các sự cố", "§5.5", "BE-21",
   [("201", {"description": "Phiếu đã tạo", "content": json_content("WorkOrderItem")}),
    ("400", err("VALIDATION_FAILED"))],
   body={"$ref": "#/components/schemas/CreateWorkOrderRequest"})
ni("patch", "/api/v1/work-orders/{work_order_id}", "WorkOrders", "Đổi wo_status, gán người", "§5.5", "BE-22/BE-23",
   [("200", {"description": "Phiếu sau khi đổi", "content": json_content("WorkOrderItem")}),
    ("404", err("Không tồn tại hoặc ngoài phạm vi")),
    ("409", err("Chuyển trạng thái sai luồng (luồng wo_status: Open item O-3)"))],
   parameters=[p("work_order_id", {"$ref": "#/components/schemas/WorkOrderId"}, True, where="path")],
   body={"$ref": "#/components/schemas/PatchWorkOrderRequest"})
ni("post", "/api/v1/work-orders/{work_order_id}/evidence", "WorkOrders", "Ảnh before/after cho phiếu (multipart)", "§5.5", "BE-24",
   [("201", {"description": "Đã lưu; hình dạng response chưa đặc tả"}),
    ("404", err("Không tồn tại hoặc ngoài phạm vi")),
    ("415", err("UNSUPPORTED_IMAGE_FORMAT — không phải JPEG theo magic bytes"))],
   parameters=[p("work_order_id", {"$ref": "#/components/schemas/WorkOrderId"}, True, where="path")],
   body={"$ref": "#/components/schemas/EvidenceUpload"}, body_ct="multipart/form-data")
ni("get", "/api/v1/iot-nodes", "IotSweeps", "IoT node theo bbox (FeatureCollection)", "§5.6", "BE-14",
   [("200", {"description": "FeatureCollection", "content": json_content("IotNodeFeatureCollection")}),
    ("400", err("VALIDATION_FAILED — thiếu bbox"))],
   parameters=[BBOX])
ni("get", "/api/v1/sweeps", "IotSweeps", "Lịch sử các đợt quét", "§5.6", "BE-17",
   [("200", {"description": "Trang sweep", "content": json_content("SweepPagedResult")})],
   parameters=PAGE)
ni("get", "/api/v1/frames/{frame_id}/thumbnail", "IotSweeps", "Thumbnail JPEG của một khung hình — proxy qua API, không presigned", "§5.6", "BE-15",
   [("200", {"description": "JPEG (320px cạnh dài, q80 — TẠM, Open item O-4)", "content": {"image/jpeg": {"schema": {"type": "string", "format": "binary"}}}}),
    ("404", err("Không tồn tại hoặc ngoài phạm vi"))],
   parameters=[p("frame_id", {"$ref": "#/components/schemas/FrameId"}, True, where="path")])
ni("get", "/api/v1/sync/bundle", "Sync", "Gói dữ liệu theo segment để cache offline", "§5.8", "BE-43",
   [("200", {"description": "Bundle trong phạm vi địa bàn của user (§2)", "content": json_content("SyncBundle")}),
    ("400", err("VALIDATION_FAILED — thiếu segment_id"))],
   parameters=[p("segment_id", {"$ref": "#/components/schemas/SegmentId"}, True),
               p("since", {"type": "string", "format": "date-time"}, desc="ISO 8601 UTC hậu tố Z")])
ni("post", "/api/v1/sync/push", "Sync", "Đẩy thay đổi offline; khử trùng lặp theo client_op_id; server thắng khi xung đột", "§5.8", "BE-43",
   [("200", {"description": "applied[] + conflicts[]", "content": json_content("SyncPushResponse")}),
    ("400", err("VALIDATION_FAILED"))],
   body={"$ref": "#/components/schemas/SyncPushRequest"})

# Order paths: implemented first in original order, then the rest as inserted.
json.dump(d, open(DST, "w"), ensure_ascii=False, indent=2)
n_impl = sum(1 for _, i in d["paths"].items() for m, o in i.items() if isinstance(o, dict) and o.get("x-luxmap-status") == "implemented")
n_ni = sum(1 for _, i in d["paths"].items() for m, o in i.items() if isinstance(o, dict) and o.get("x-luxmap-status") == "not_implemented")
print(f"wrote {DST}: paths={len(d['paths'])} implemented_ops={n_impl} not_implemented_ops={n_ni} schemas={len(S)}")
