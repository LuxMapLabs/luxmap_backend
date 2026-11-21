#!/usr/bin/env python3
"""Generate the WB-12 multisheet inventory template and CSV-based example (openpyxl)."""
import csv
from datetime import date
from pathlib import Path

from openpyxl import Workbook
from openpyxl.styles import Alignment, Font, PatternFill
from openpyxl.workbook.defined_name import DefinedName
from openpyxl.worksheet.datavalidation import DataValidation
from openpyxl.utils import get_column_letter

ROOT = Path(__file__).resolve().parent.parent
# Same CSV vocabulary and ordering as WorkbookSheets; committed-template tests guard drift.
SHEETS = [
    ('Tuyến đường', 'segments', ['external_ref', 'segment_name', 'road_class', 'length_m', 'geom_wkt'], 3),
    ('Tủ điện', 'cabinets', ['external_ref', 'cabinet_name', 'geom_wkt'], 3),
    ('Mạch điện', 'feeders', ['external_ref', 'feeder_name', 'cabinet_external_ref', 'cabinet_source'], 2),
    ('Cột điện', 'poles', ['external_ref', 'segment_external_ref', 'feeder_external_ref', 'feeder_source', 'geom_wkt', 'near_sensitive_poi', 'note', 'initial_status'], 1),
    ('Bóng đèn', 'fixtures', ['pole_external_ref', 'fixture_type', 'power_source', 'lamp_watt', 'install_date', 'lamp_count', 'removed_date', 'warranty_expiry', 'note'], 5),
]
DROPDOWNS = {
    'road_class': '"inter_commune,inter_village"', 'fixture_type': '"led_road_lamp"', 'power_source': '"grid"',
    'cabinet_source': '"verified,inferred"', 'feeder_source': '"verified,inferred"', 'near_sensitive_poi': '"true,false"', 'initial_status': '"normal,dim,out"',
    'lamp_count': '"1,2,3,4"', 'segment_external_ref': 'ds_tuyen_duong', 'cabinet_external_ref': 'ds_tu_dien',
    'feeder_external_ref': 'ds_mach_dien', 'pole_external_ref': 'ds_cot_dien',
}
GUIDE = [
    'LUXMAP · MẪU NẠP TÀI SẢN CHIẾU SÁNG',
    'Nạp cả file một lần; chọn xã và data_source khi upload. Có lỗi: không ghi gì.',
    'external_ref là mã kiểm kê của xã (CTA-001, TUYEN-A), không dùng mã hệ thống POLE-0001 / SEG-001.',
    'Cột mới không GPS phải có note mô tả vị trí. Bộ đèn khớp theo cột và ngày lắp; sửa qua form.',
    'Mỗi sheet tối đa 5000 dòng, 200 cột, 2000 ký tự/ô; file 10 MB, giải nén 100 MB.',
    'geom_wkt: POINT(lng lat) / LINESTRING(lng lat, …). Tuyến trống: giữ hình; xoá qua form. Mạch điện không có geom_wkt.',
    'File nhiều xã không nạp lại được; upload chỉ nhận một xã.',
    'Nạp lại dùng một data_source; tài sản khác nguồn sẽ theo nguồn chọn khi upload (bộ đèn có sẵn không sửa).',
    'Nguồn: field / public_imagery / calibration_rig / simulated. Testbed không bao giờ chọn field.',
    'Dòng 1 là header; giữ nguyên tên cột. Không đặt commune_id hoặc data_source trong sheet.',
    'Ngày YYYY-MM-DD hoặc ngày Excel nguyên. Mã cha lấy từ dropdown; mỗi tài sản một dòng.',
    'Bóng đèn nhận cả bộ đã tháo (removed_date). Chỉ một bộ đang dùng trên mỗi cột.',
]


def build(path, example=False):
    book = Workbook()
    guide = book.active
    guide.title = 'Hướng dẫn'
    guide.sheet_view.showGridLines = False
    guide.column_dimensions['A'].width = 115
    for text in GUIDE:
        guide.append([text])
        guide.cell(guide.max_row, 1).alignment = Alignment(wrap_text=True, vertical='top')
        guide.row_dimensions[guide.max_row].height = 36
    guide['A1'].fill = PatternFill('solid', fgColor='0F172A')
    guide['A1'].font = Font(name='Arial', size=16, bold=True, color='FFFFFF')
    for title, kind, columns, required_count in SHEETS:
        sheet = book.create_sheet(title)
        sheet.sheet_view.showGridLines = False
        sheet.freeze_panes = 'A2'
        sheet.append(columns)
        sheet.row_dimensions[1].height = 30
        for index, name in enumerate(columns, 1):
            letter = get_column_letter(index)
            header = sheet.cell(1, index)
            header.fill = PatternFill('solid', fgColor='1E3A5F' if index <= required_count else '64748B')
            header.font = Font(name='Arial', size=11, bold=True, color='FFFFFF')
            header.alignment = Alignment(vertical='center')
            sheet.column_dimensions[letter].width = 60 if name in ('geom_wkt', 'note') else max(23, len(name) + 3)
            if 'ref' in name:
                sheet.column_dimensions[letter].number_format = '@'
            elif name.endswith('date') or name == 'warranty_expiry':
                sheet.column_dimensions[letter].number_format = 'yyyy-mm-dd'
            if name in DROPDOWNS:
                validation = DataValidation(type='list', formula1=DROPDOWNS[name], allow_blank=True)
                validation.showErrorMessage = True
                sheet.add_data_validation(validation)
                validation.add(f'{letter}2:{letter}5001')
        if example:
            with (ROOT / 'docs/templates' / f'{kind}.example.csv').open() as source:
                for row in csv.DictReader(source):
                    values = []
                    for name in columns:
                        value = row.get(name) or None
                        if value and name in ('length_m', 'lamp_watt', 'lamp_count'):
                            value = int(value)
                        elif value and (name.endswith('date') or name == 'warranty_expiry'):
                            value = date.fromisoformat(value)
                        values.append(value)
                    sheet.append(values)
            for row in sheet.iter_rows(min_row=2):
                for cell in row:
                    name = columns[cell.column - 1]
                    if 'ref' in name:
                        cell.number_format = '@'
                    elif name.endswith('date') or name == 'warranty_expiry':
                        cell.number_format = 'yyyy-mm-dd'
        sheet.auto_filter.ref = f'A1:{get_column_letter(len(columns))}{sheet.max_row}'
    for name, title in [('ds_tuyen_duong', 'Tuyến đường'), ('ds_tu_dien', 'Tủ điện'), ('ds_mach_dien', 'Mạch điện'), ('ds_cot_dien', 'Cột điện')]:
        book.defined_names.add(DefinedName(name, attr_text=f"'{title}'!$A$2:$A$5001"))
    book.save(path)


if __name__ == '__main__':
    build(ROOT / 'docs/templates/assets-workbook.xlsx')
    build(ROOT / 'docs/templates/assets-workbook.example.xlsx', example=True)
