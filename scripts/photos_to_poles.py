#!/usr/bin/env python3
"""Turns a folder of clean field photos of street-light poles into a DRAFT for the asset import.

    python3 scripts/photos_to_poles.py <photo-folder> --out <output-folder>
    python3 scripts/photos_to_poles.py img --out out --commune-id COM-070 --segment-ref TUYEN-A \\
        --ref-prefix LP --existing poles.geojson

Standard library only, so it runs on any team machine without installing anything.

WHAT GOES IN. One folder per trip, CLEAN originals only (no stamped copies), copied off the phone by
cable, AirDrop or "download original" — chat apps (Zalo, Messenger) strip the EXIF this tool reads.
JPEG only (the backend accepts nothing else, BE-11). Sub-folders are not scanned.

WHAT COMES OUT, in <output-folder>:

  poles.csv          Header of docs/templates/poles.csv plus a trailing `photo_group` column, which
                     the importer ignores (it only checks that the required columns exist). One row
                     per photo group. `data_source` is always `field`. `segment_external_ref`,
                     `commune_id` and `external_ref` are left blank unless given on the command line —
                     the import rejects blank required cells, so a person fills them before importing.
                     Coordinates are where the PHOTOGRAPHER stood, not the pole: a few metres off.
  observations.csv   One row per group: photos, time, coordinates, and an empty `status` column for a
                     PERSON to fill (on | off | unclear) from the photos. Never imported: the import
                     does not take lamp status, and a photo's auto exposure says nothing reliable about
                     brightness. It is the night visual check the registration counts as ground truth.
  review.csv         Cases the tool will not decide: groups close enough to be the same pole or two
                     facing poles, and groups that sit on a pole already in the system.
  rejected.csv       Every photo left out, with the reason. Nothing is dropped silently.
  photos/            The kept photos, copied byte for byte (EXIF intact), renamed <group>_<name>.

HOW PHOTOS BECOME GROUPS. Sorted by capture time (EXIF DateTimeOriginal + OffsetTimeOriginal +
SubsecTimeOriginal). Consecutive photos less than --same-pole-seconds apart belong to the same pole:
from a vehicle at 15-25 km/h with poles 25-40 m apart, two poles are at least ~3.6 s apart while the
1-2 shots of one pole land within a second. GPS does not decide this — phone apps often reuse the last
fix, so two different poles can carry the same coordinates. A group's position is the median of its
photos.

WHAT IT REFUSES TO DECIDE. A second pass over the same road produces a second group for the same
pole. From the vehicle's position alone that is indistinguishable from two poles facing each other
across a narrow road, so such pairs go to review.csv instead of being merged. All thresholds are
provisional until GPS error is measured in the field.

A PERSON DECIDES, THE TOOL RECORDS. After looking at the photos, re-run with `--merge P002=P005`
(repeatable; `P002=P005=P009` merges three) on the same folder and thresholds, so group names stay
the same. The merged pole keeps the earliest name and its position becomes the median of EVERY photo
from both passes — two independent GPS fixes, a better estimate than deleting one row by hand. A merge
of groups further apart than --review-m is still done, but flagged in review.csv as a likely typo.

The GPS time-stamp is NOT used: GPS Map Camera writes local time into a field the EXIF standard
defines as UTC, and writes altitude 0.
"""

import argparse
import csv
import json
import math
import shutil
import statistics
import struct
import sys
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path

POLES_HEADER = ["external_ref", "segment_external_ref", "feeder_external_ref", "commune_id",
                "geom_wkt", "near_sensitive_poi", "data_source"]
JPEG_MAGIC = b"\xff\xd8\xff"
EARTH_RADIUS_M = 6_371_008.8

TAG_EXIF_IFD, TAG_GPS_IFD = 0x8769, 0x8825
TAG_DATETIME_ORIGINAL, TAG_OFFSET_ORIGINAL, TAG_SUBSEC_ORIGINAL = 0x9003, 0x9011, 0x9291
GPS_LAT_REF, GPS_LAT, GPS_LNG_REF, GPS_LNG, GPS_IMG_DIRECTION = 1, 2, 3, 4, 17

# EXIF field type -> (struct code, bytes per value)
EXIF_TYPES = {1: ("B", 1), 2: ("s", 1), 3: ("H", 2), 4: ("I", 4), 5: ("II", 8),
              7: ("B", 1), 9: ("i", 4), 10: ("ii", 8)}


class Rejected(Exception):
    """A photo the tool cannot use; the message is the reason written to rejected.csv."""


@dataclass
class Photo:
    path: Path
    lat: float
    lng: float
    taken_utc: datetime
    taken_local: datetime
    heading: float | None


@dataclass
class Group:
    name: str
    photos: list[Photo]
    lat: float = field(init=False)
    lng: float = field(init=False)
    existing_pole: str = ""

    def __post_init__(self):
        self.lat = statistics.median(p.lat for p in self.photos)
        self.lng = statistics.median(p.lng for p in self.photos)


def read_exif(data: bytes) -> tuple[dict, dict]:
    """Returns (exif_ifd, gps_ifd) from a JPEG's APP1 segment; empty dicts when there is none."""
    i = 2
    while i + 4 <= len(data):
        if data[i] != 0xFF:
            raise Rejected("JPEG hỏng: không đọc được cấu trúc file")
        marker = data[i + 1]
        if marker == 0xFF:
            i += 1
            continue
        if marker in (0xD9, 0xDA):  # end of image / start of scan: no metadata after this
            break
        length = struct.unpack(">H", data[i + 2:i + 4])[0]
        segment = data[i + 4:i + 2 + length]
        if marker == 0xE1 and segment[:6] == b"Exif\0\0":
            try:
                return parse_tiff(segment[6:])
            except (struct.error, IndexError, KeyError) as error:
                raise Rejected(f"EXIF hỏng ({error})") from None
        i += 2 + length
    return {}, {}


def parse_tiff(tiff: bytes) -> tuple[dict, dict]:
    order = {b"II": "<", b"MM": ">"}.get(tiff[:2])
    if order is None:
        raise Rejected("EXIF hỏng: không rõ thứ tự byte")

    def read_ifd(offset: int) -> dict:
        count = struct.unpack(order + "H", tiff[offset:offset + 2])[0]
        entries = {}
        for n in range(count):
            at = offset + 2 + 12 * n
            tag, kind, values = struct.unpack(order + "HHI", tiff[at:at + 8])
            if kind not in EXIF_TYPES:
                continue
            code, size = EXIF_TYPES[kind]
            if size * values <= 4:
                raw = tiff[at + 8:at + 8 + size * values]
            else:
                start = struct.unpack(order + "I", tiff[at + 8:at + 12])[0]
                raw = tiff[start:start + size * values]
            if kind == 2:
                entries[tag] = raw.split(b"\0")[0].decode("ascii", "replace").strip()
            elif kind in (5, 10):
                pairs = struct.unpack(order + code[0] * (2 * values), raw)
                entries[tag] = [num / den if den else None for num, den in zip(pairs[::2], pairs[1::2])]
            else:
                entries[tag] = list(struct.unpack(order + code * values, raw))
        return entries

    ifd0 = read_ifd(struct.unpack(order + "I", tiff[4:8])[0])
    exif = read_ifd(ifd0[TAG_EXIF_IFD][0]) if TAG_EXIF_IFD in ifd0 else {}
    gps = read_ifd(ifd0[TAG_GPS_IFD][0]) if TAG_GPS_IFD in ifd0 else {}
    return exif, gps


def degrees(dms, ref: str | None, negative: str) -> float | None:
    if not dms or len(dms) != 3 or None in dms or not ref:
        return None
    value = dms[0] + dms[1] / 60 + dms[2] / 3600
    return -value if ref.upper() == negative else value


def parse_offset(text: str) -> timezone:
    try:
        sign = -1 if text[0] == "-" else 1
        hours, minutes = text.lstrip("+-").split(":")
        return timezone(sign * timedelta(hours=int(hours), minutes=int(minutes)))
    except (ValueError, IndexError):
        raise Rejected(f"múi giờ không hợp lệ: {text!r}") from None


def read_photo(path: Path, assume_offset: str | None) -> Photo:
    data = path.read_bytes()
    if not data.startswith(JPEG_MAGIC):
        raise Rejected("không phải JPEG (HEIC/PNG?) — đặt máy ảnh ở định dạng 'Tương thích nhất'")
    exif, gps = read_exif(data)

    lat = degrees(gps.get(GPS_LAT), gps.get(GPS_LAT_REF), "S")
    lng = degrees(gps.get(GPS_LNG), gps.get(GPS_LNG_REF), "W")
    if lat is None or lng is None:
        raise Rejected("thiếu toạ độ GPS trong EXIF (ảnh gửi qua app nhắn tin bị xoá metadata?)")
    if lat == 0 and lng == 0:
        raise Rejected("toạ độ 0,0 — GPS chưa bắt được khi chụp")

    original = exif.get(TAG_DATETIME_ORIGINAL)
    if not original:
        raise Rejected("thiếu thời gian chụp (DateTimeOriginal)")
    offset_text = exif.get(TAG_OFFSET_ORIGINAL) or assume_offset
    if not offset_text:
        raise Rejected("thiếu múi giờ (OffsetTimeOriginal) — chạy lại với --assume-offset nếu chắc chắn")
    try:
        naive = datetime.strptime(original, "%Y:%m:%d %H:%M:%S")
    except ValueError:
        raise Rejected(f"thời gian chụp không hợp lệ: {original!r}") from None
    subsec = "".join(ch for ch in str(exif.get(TAG_SUBSEC_ORIGINAL, "")) if ch.isdigit())
    if subsec:
        naive += timedelta(seconds=int(subsec) / 10 ** len(subsec))
    local = naive.replace(tzinfo=parse_offset(offset_text))

    direction = gps.get(GPS_IMG_DIRECTION)
    heading = direction[0] if direction and direction[0] is not None else None
    return Photo(path, lat, lng, local.astimezone(timezone.utc), local, heading)


def distance_m(lat1: float, lng1: float, lat2: float, lng2: float) -> float:
    """Haversine. Never subtract degrees: 0.0003 degrees is not a distance."""
    p1, p2 = math.radians(lat1), math.radians(lat2)
    dp, dl = p2 - p1, math.radians(lng2 - lng1)
    a = math.sin(dp / 2) ** 2 + math.cos(p1) * math.cos(p2) * math.sin(dl / 2) ** 2
    return 2 * EARTH_RADIUS_M * math.asin(math.sqrt(a))


def drop_duplicates(photos: list[Photo], rejected: list[tuple[str, str]]) -> list[Photo]:
    """One shutter press exported twice (stamped + clean) shares time to the millisecond and position."""
    kept, seen = [], {}
    for photo in sorted(photos, key=lambda p: p.path.name):
        key = (photo.taken_utc, round(photo.lat, 7), round(photo.lng, 7))
        if key in seen:
            rejected.append((photo.path.name, f"cùng một lần bấm với {seen[key]} — kiểm tra file nào là "
                                              "bản in chữ, giữ bản sạch"))
        else:
            seen[key] = photo.path.name
            kept.append(photo)
    return kept


def group_photos(photos: list[Photo], same_pole_seconds: float, max_jump_m: float) -> list[Group]:
    runs: list[list[Photo]] = []
    for photo in sorted(photos, key=lambda p: (p.taken_utc, p.path.name)):
        if runs:
            last = runs[-1][-1]
            gap = (photo.taken_utc - last.taken_utc).total_seconds()
            # The distance guard only stops photos from two phones interleaving in time.
            if gap < same_pole_seconds and distance_m(last.lat, last.lng, photo.lat, photo.lng) <= max_jump_m:
                runs[-1].append(photo)
                continue
        runs.append([photo])
    return [Group(f"P{n:03d}", run) for n, run in enumerate(runs, start=1)]


def merge_groups(groups: list[Group], specs: list[str], review_m: float) -> tuple[list[Group], list[list]]:
    """Applies `--merge A=B[=C]` decisions; returns the merged groups and review rows for far merges."""
    by_name = {group.name: group for group in groups}
    parent = {name: name for name in by_name}

    def root(name: str) -> str:
        while parent[name] != name:
            name = parent[name]
        return name

    reviews = []
    for spec in specs:
        names = [part.strip().upper() for part in spec.split("=")]
        unknown = [name for name in names if name not in by_name]
        if len(names) < 2 or unknown:
            raise ValueError(f"--merge {spec!r}: cần dạng P002=P005 với tên nhóm có thật"
                             + (f" (không có nhóm {', '.join(unknown)})" if unknown else ""))
        first = by_name[names[0]]
        for name in names[1:]:
            other = by_name[name]
            d = distance_m(first.lat, first.lng, other.lat, other.lng)
            if d > review_m:
                reviews.append(["gop_xa", first.name, other.name, f"{d:.1f}", "",
                                f"Đã gộp theo --merge nhưng hai nhóm cách nhau hơn {review_m:g} m — kiểm tra lại "
                                "có gõ nhầm tên nhóm không."])
            # Keep the earliest name, so the merged pole's name and external_ref do not move.
            keep, drop = sorted((root(first.name), root(name)), key=lambda n: int(n[1:]))
            parent[drop] = keep

    merged: dict[str, list[Photo]] = {}
    for group in groups:  # a root always precedes its members, so insertion order stays time order
        merged.setdefault(root(group.name), []).extend(group.photos)
    return [Group(name, sorted(photos, key=lambda p: (p.taken_utc, p.path.name)))
            for name, photos in merged.items()], reviews


def read_existing(path: Path) -> list[tuple[str, float, float]]:
    """Poles already in the system, from a GeoJSON FeatureCollection of Points with properties.pole_id."""
    poles = []
    for feature in json.loads(path.read_text(encoding="utf-8")).get("features", []):
        geometry, props = feature.get("geometry") or {}, feature.get("properties") or {}
        if geometry.get("type") == "Point" and props.get("pole_id"):
            lng, lat = geometry["coordinates"][:2]
            poles.append((props["pole_id"], lat, lng))
    return poles


def find_reviews(groups: list[Group], existing, stopped_m: float, review_m: float) -> list[list]:
    reviews = []
    for i, a in enumerate(groups):
        for j in range(i + 1, len(groups)):
            b = groups[j]
            d = distance_m(a.lat, a.lng, b.lat, b.lng)
            gap = (b.photos[0].taken_utc - a.photos[-1].taken_utc).total_seconds()
            if j == i + 1 and d < stopped_m:
                reviews.append(["lien_nhau_rat_gan", a.name, b.name, f"{d:.1f}", f"{gap:.1f}",
                                "Hai nhóm liền nhau gần như cùng chỗ: xe dừng chụp lại CÙNG cột, GPS dùng lại "
                                "toạ độ cũ, hoặc hai cột ĐỐI DIỆN. Xem ảnh để quyết."])
            elif j > i + 1 and d < review_m:
                reviews.append(["luot_khac_gan", a.name, b.name, f"{d:.1f}", f"{gap:.1f}",
                                "Một lượt đi khác qua gần chỗ này: cùng cột chụp hai lần, hoặc hai cột khác "
                                "nhau. Nếu cùng cột thì xoá một dòng khỏi poles.csv."])
    for group in groups:
        nearest = min(((distance_m(group.lat, group.lng, lat, lng), pole_id) for pole_id, lat, lng in existing),
                      default=None)
        if nearest and nearest[0] < review_m:
            group.existing_pole = nearest[1]
            reviews.append(["da_co_cot", group.name, nearest[1], f"{nearest[0]:.1f}", "",
                            "Đã có cột trong hệ thống gần đây nên nhóm này KHÔNG được ghi vào poles.csv. "
                            "Nếu là cột mới thật thì thêm lại tay."])
    return reviews


def write_csv(path: Path, header: list[str], rows: list[list]) -> None:
    # utf-8-sig so Excel shows Vietnamese correctly; the importer strips the BOM.
    with path.open("w", encoding="utf-8-sig", newline="") as handle:
        writer = csv.writer(handle)
        writer.writerow(header)
        writer.writerows(rows)


def external_ref(prefix: str | None, group: Group, used: set[str]) -> str:
    """Stable across re-runs on the same photos, so re-importing updates instead of duplicating."""
    if not prefix:
        return ""
    ref = f"{prefix}-{group.photos[0].taken_local:%Y%m%d-%H%M%S}"
    candidate, n = ref, 2
    while candidate in used:
        candidate, n = f"{ref}-{n}", n + 1
    used.add(candidate)
    return candidate


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("photos", type=Path, help="thư mục ảnh sạch (không quét thư mục con)")
    parser.add_argument("--out", type=Path, required=True, help="thư mục kết quả (phải trống hoặc chưa có)")
    parser.add_argument("--commune-id", default="", help="điền sẵn commune_id cho mọi dòng")
    parser.add_argument("--segment-ref", default="", help="điền sẵn segment_external_ref (một thư mục = một tuyến)")
    parser.add_argument("--ref-prefix", help="sinh external_ref = <prefix>-<giờ chụp>; bỏ trống thì để người điền")
    parser.add_argument("--existing", type=Path, help="GeoJSON các cột đã có (properties.pole_id) để tránh tạo trùng")
    parser.add_argument("--assume-offset", help="múi giờ dùng khi ảnh thiếu OffsetTimeOriginal, ví dụ +07:00")
    parser.add_argument("--same-pole-seconds", type=float, default=2.0, help="mặc định 2.0")
    parser.add_argument("--max-jump-m", type=float, default=50.0, help="mặc định 50")
    parser.add_argument("--stopped-m", type=float, default=5.0, help="mặc định 5")
    parser.add_argument("--review-m", type=float, default=20.0, help="mặc định 20")
    parser.add_argument("--merge", action="append", default=[], metavar="P002=P005",
                        help="gộp các nhóm người đã xác nhận là cùng một cột (lặp lại được)")
    args = parser.parse_args()

    source, out = args.photos.resolve(), args.out.resolve()
    if not source.is_dir():
        parser.error(f"không thấy thư mục ảnh: {source}")
    if out == source:
        parser.error("--out phải khác thư mục ảnh")
    if out.exists() and any(out.iterdir()):
        parser.error(f"thư mục kết quả đã có dữ liệu, chọn thư mục khác: {out}")

    rejected: list[tuple[str, str]] = []
    photos = []
    for path in sorted(p for p in source.iterdir() if p.is_file() and not p.name.startswith(".")):
        try:
            photos.append(read_photo(path, args.assume_offset))
        except Rejected as reason:
            rejected.append((path.name, str(reason)))
        except OSError as error:
            rejected.append((path.name, f"không đọc được file ({error})"))

    photos = drop_duplicates(photos, rejected)
    groups = group_photos(photos, args.same_pole_seconds, args.max_jump_m)
    try:
        groups, merge_reviews = merge_groups(groups, args.merge, args.review_m)
    except ValueError as error:
        parser.error(str(error))
    existing = read_existing(args.existing) if args.existing else []
    reviews = merge_reviews + find_reviews(groups, existing, args.stopped_m, args.review_m)

    (out / "photos").mkdir(parents=True, exist_ok=True)
    used_refs: set[str] = set()
    pole_rows, observation_rows = [], []
    for group in groups:
        for photo in group.photos:
            shutil.copy2(photo.path, out / "photos" / f"{group.name}_{photo.path.name}")
        if not group.existing_pole:
            pole_rows.append([external_ref(args.ref_prefix, group, used_refs), args.segment_ref, "",
                              args.commune_id, f"POINT({group.lng:.7f} {group.lat:.7f})", "", "field",
                              group.name])
        headings = [p.heading for p in group.photos if p.heading is not None]
        first = group.photos[0]
        observation_rows.append([
            group.name, f"{group.lat:.7f}", f"{group.lng:.7f}",
            first.taken_utc.isoformat(timespec="milliseconds").replace("+00:00", "Z"),
            first.taken_local.isoformat(timespec="milliseconds"), len(group.photos),
            ";".join(f"{group.name}_{p.path.name}" for p in group.photos),
            f"{statistics.median(headings):.0f}" if headings else "", group.existing_pole, "", ""])

    write_csv(out / "poles.csv", POLES_HEADER + ["photo_group"], pole_rows)
    write_csv(out / "observations.csv",
              ["photo_group", "lat", "lng", "taken_at_utc", "taken_at_local", "photo_count", "photos",
               "heading_deg", "existing_pole", "status", "status_note"], observation_rows)
    write_csv(out / "review.csv", ["kind", "photo_group", "other", "distance_m", "gap_s", "note"], reviews)
    write_csv(out / "rejected.csv", ["file", "reason"], rejected)

    print(f"{len(photos)} ảnh dùng được, {len(rejected)} ảnh bị loại -> {len(groups)} nhóm cột")
    print(f"poles.csv: {len(pole_rows)} dòng · review.csv: {len(reviews)} ca cần xem · kết quả ở {out}")
    if not args.ref_prefix or not args.segment_ref or not args.commune_id:
        print("⚠️  poles.csv còn ô bắt buộc để trống (external_ref / segment_external_ref / commune_id) "
              "— điền trước khi import.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
