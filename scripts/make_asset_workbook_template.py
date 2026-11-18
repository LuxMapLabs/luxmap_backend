#!/usr/bin/env python3
"""Generate the one-sheet inventory template and example (openpyxl).

The example commune defaults to COM-070 from the companion *.example.csv files.
Seed IDs are generated; use --commune-id with the study_site ID of another database.
"""
import argparse
from datetime import date
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.utils import get_column_letter
from openpyxl.worksheet.datavalidation import DataValidation

ROOT = Path(__file__).resolve().parent.parent
# Sheet columns and Vietnamese instructions follow ASSET-IMPORT-WORKBOOK §3 / WB-1…WB-6.
GROUPS = [
    ("Chung", [
        ("commune_id", "Bắt buộc. Mã xã tồn tại và thuộc phạm vi người nạp."),
        ("data_source", "Bắt buộc. Nguồn dữ liệu cho tuyến, tủ, cột, bộ đèn; dữ liệu testbed không dùng field."),
    ]),
    ("Tuyến", [
        ("segment_ref", "Mã tuyến vĩnh viễn; trống: cột mới chưa tuyến, cột đã có giữ tuyến."),
        ("segment_name", "Bắt buộc khi có segment_ref. Tên tuyến."),
        ("road_class", "Bắt buộc khi có segment_ref: inter_commune / inter_village."),
        ("segment_length_m", "Số nguyên ≥ 1; trống giữ chiều dài đã lưu. Tuyến mới chưa hình được để trống."),
    ]),
    ("Tủ điện", [
        ("cabinet_ref", "Mã tủ vĩnh viễn. Có mã thì bắt buộc tên và cả hai tọa độ."),
        ("cabinet_name", "Tên tủ điện, bắt buộc khi có cabinet_ref."),
        ("cabinet_lat", "Vĩ độ [-90,90], số hữu hạn, bắt buộc khi có cabinet_ref."),
        ("cabinet_lng", "Kinh độ [-180,180], số hữu hạn, bắt buộc khi có cabinet_ref."),
    ]),
    ("Mạch", [
        ("feeder_ref", "Mã mạch vĩnh viễn. Thuộc tủ cabinet_ref cùng dòng; ô tủ trống giữ tủ đã có."),
        ("feeder_name", "Tên mạch, bắt buộc khi có feeder_ref."),
        ("cabinet_source", "Nhãn mạch → tủ: verified / inferred. Trống: giữ nhãn nếu cùng tủ, quan hệ mới là inferred."),
    ]),
    ("Cột đèn", [
        ("pole_ref", "Bắt buộc. Mã cột vĩnh viễn; mỗi dòng một cột, không trùng trong cùng xã."),
        ("pole_lat", "Vĩ độ [-90,90]; điền cả hai tọa độ hoặc trống cả hai. Trống giữ vị trí đã có."),
        ("pole_lng", "Kinh độ [-180,180]; cột mới trống cả hai tọa độ thì chưa GPS."),
        ("near_sensitive_poi", "true / false; trống mặc định false. Cột gần trường, chợ, cầu…"),
        ("pole_note", "Ghi chú vị trí ≤ 1000 ký tự. Có chữ ghi đè; trống giữ ghi chú cũ."),
        ("feeder_source", "Nhãn cột → mạch: verified / inferred. Ô feeder_ref trống tháo mạch của cột."),
    ]),
    ("Bộ đèn", [
        ("fixture_type", "led_road_lamp. Điền bất kỳ ô bộ đèn thì bắt buộc loại, nguồn điện, công suất, ngày lắp."),
        ("power_source", "grid; bắt buộc nếu có bộ đèn."),
        ("lamp_watt", "Số nguyên: công suất một bóng, bắt buộc nếu có bộ đèn."),
        ("lamp_count", "Số bóng giống nhau trong một bộ, 1–4; trống = 1; chao đôi = 2."),
        ("install_date", "Ngày lắp: YYYY-MM-DD hoặc ô ngày Excel, không có phần giờ; bắt buộc nếu có bộ đèn."),
        ("warranty_expiry", "Ngày hết bảo hành. Trống = không nói gì khi so bộ đang dùng."),
        ("fixture_note", "Ghi chú bộ đèn ≤ 1000 ký tự. Trống = không nói gì khi so bộ đang dùng."),
    ]),
]
COLUMNS = [name for _, fields in GROUPS for name, _ in fields]
DROPDOWNS = {
    "data_source": "field,public_imagery,calibration_rig,simulated",
    "road_class": "inter_commune,inter_village", "fixture_type": "led_road_lamp", "power_source": "grid",
    "cabinet_source": "verified,inferred", "feeder_source": "verified,inferred", "near_sensitive_poi": "true,false",
}


def build(path, commune_id, example=False):
    book = Workbook()
    sheet = book.active
    sheet.title = "import"
    sheet.sheet_view.showGridLines = False
    sheet.freeze_panes = "C2"
    sheet.append(COLUMNS)
    for cell in sheet[1]:
        cell.font = Font(name="Arial", size=10, bold=True, color="FFFFFF")
        cell.fill = PatternFill("solid", fgColor="25364A")
        cell.alignment = Alignment(horizontal="center", vertical="center")
    sheet.row_dimensions[1].height = 25
    for index, name in enumerate(COLUMNS, 1):
        letter = get_column_letter(index)
        sheet.column_dimensions[letter].width = max(18, len(name) + 3)
        if name in ("pole_note", "fixture_note", "segment_name", "cabinet_name", "feeder_name"):
            sheet.column_dimensions[letter].width = 35
        if name in ("install_date", "warranty_expiry"):
            sheet.column_dimensions[letter].number_format = "yyyy-mm-dd"
        if name in DROPDOWNS:
            validation = DataValidation(type="list", formula1=f'"{DROPDOWNS[name]}"', allow_blank=True)
            validation.errorTitle = "Giá trị không hợp lệ"
            validation.error = "Chọn giá trị trong danh sách."
            validation.showErrorMessage = True
            sheet.add_data_validation(validation)
            validation.add(f"{letter}2:{letter}5001")
    if example:
        for i in range(1, 6):
            row = dict(commune_id=commune_id, data_source="public_imagery", segment_ref="WB-TUYEN-A",
                       segment_name="Tuyến minh họa A", road_class="inter_village", segment_length_m=1600,
                       cabinet_ref="WB-TU-A", cabinet_name="Tủ minh họa A", cabinet_lat=10.97, cabinet_lng=106.49,
                       feeder_ref="WB-MACH-A", feeder_name="Mạch minh họa A", pole_ref=f"WB-COT-{i:03}",
                       pole_lat=10.97 + i * .0001, pole_lng=106.49 + i * .0001, near_sensitive_poi=i == 1)
            if i == 3:
                row["pole_lat"] = row["pole_lng"] = None
                row["pole_note"] = "Chưa GPS; kỹ sư bổ sung vị trí sau"
            if i != 4:
                row.update(fixture_type="led_road_lamp", power_source="grid", lamp_watt=100,
                           lamp_count=2 if i == 5 else 1, install_date=date(2022, 3, 24), warranty_expiry=date(2024, 3, 24))
            sheet.append([row.get(c) for c in COLUMNS])
        for row in sheet.iter_rows(min_row=2):
            for cell in row:
                cell.font = Font(name="Arial", size=10)
                if COLUMNS[cell.column - 1] in ("install_date", "warranty_expiry"):
                    cell.number_format = "yyyy-mm-dd"
    sheet.auto_filter.ref = f"A1:Z{sheet.max_row}"
    guide = book.create_sheet("Hướng dẫn")
    guide.sheet_view.showGridLines = False
    guide.append(["Nhóm", "Cột", "Quy tắc"])
    for group, fields in GROUPS:
        for name, description in fields:
            guide.append([group, name, description])
    guide.append(["Nạp file", "", "Chỉ sheet đầu; tối đa 5000 dòng, 200 cột, 2000 ký tự/ô; file ≤ 10 MB."])
    guide.append(["Gộp trùng", "", "Tuyến/tủ/mạch: dòng đầu định nghĩa, dòng sau trống không nói gì; khác giá trị là lỗi."])
    guide.append(["Nguyên tử", "", "Có bất kỳ lỗi nào thì không ghi gì. Sửa lỗi theo số dòng và tên cột được trả về."])
    guide.append(["Bộ đang dùng", "", "Nạp lại giống thì unchanged; khác là lỗi. Thay bộ đèn qua form tài sản."])
    guide.append(["Tọa độ", "", "Dấu chấm thập phân; không dùng 10,97. Sheet không sửa hình tuyến đã có."])
    guide.append(["Mã xã mẫu", "", f"{commune_id}: theo *.example.csv; DB seed khác phải thay bằng mã study_site thực tế."])
    for cell in guide[1]:
        cell.fill = PatternFill("solid", fgColor="25364A")
        cell.font = Font(name="Arial", size=10, bold=True, color="FFFFFF")
    guide.column_dimensions["A"].width = 20
    guide.column_dimensions["B"].width = 26
    guide.column_dimensions["C"].width = 105
    for row in guide.iter_rows(min_row=2):
        for cell in row:
            cell.font = Font(name="Arial", size=10)
            cell.alignment = Alignment(wrap_text=True, vertical="top")
        guide.row_dimensions[row[0].row].height = 32
    guide.freeze_panes = "C2"
    book.save(path)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--commune-id", default="COM-070")
    args = parser.parse_args()
    directory = ROOT / "docs/templates"
    build(directory / "assets-workbook.xlsx", args.commune_id)
    build(directory / "assets-workbook.example.xlsx", args.commune_id, example=True)
