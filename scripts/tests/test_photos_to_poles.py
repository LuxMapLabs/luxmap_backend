"""Offline CLI tests: synthetic JPEG/EXIF only, standard library only."""
import csv
import json
import math
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'photos_to_poles.py'


def jpeg(lat, lng, second=0):
    """Minimal JPEG envelope with TIFF EXIF date/offset and GPS rational fields."""
    data = bytearray(b'II\x2a\x00\x08\x00\x00\x00')

    def ifd(entries):
        offset = len(data)
        data.extend(struct.pack('<H', len(entries)) + bytes(12 * len(entries) + 4))
        for i, (tag, kind, count, raw) in enumerate(entries):
            value = raw.ljust(4, b'\0') if len(raw) <= 4 else struct.pack('<I', len(data))
            if len(raw) > 4:
                data.extend(raw)
            struct.pack_into('<HHI4s', data, offset + 2 + 12 * i, tag, kind, count, value)
        return offset

    root = ifd([(0x8769, 4, 1, bytes(4)), (0x8825, 4, 1, bytes(4))])
    date = f'2026:10:03 19:00:{second:02d}\0'.encode()
    exif = ifd([(0x9003, 2, len(date), date), (0x9011, 2, 7, b'+07:00\0')])

    def dms(value):
        deg = int(abs(value))
        minutes = int((abs(value) - deg) * 60)
        seconds = round(((abs(value) - deg) * 60 - minutes) * 60 * 1_000_000)
        return struct.pack('<IIIIII', deg, 1, minutes, 1, seconds, 1_000_000)

    gps = ifd([(1, 2, 2, b'N\0' if lat >= 0 else b'S\0'), (2, 5, 3, dms(lat)),
               (3, 2, 2, b'E\0' if lng >= 0 else b'W\0'), (4, 5, 3, dms(lng))])
    struct.pack_into('<I', data, root + 10, exif)
    struct.pack_into('<I', data, root + 22, gps)
    payload = b'Exif\0\0' + data
    return b'\xff\xd8\xff\xe1' + struct.pack('>H', len(payload) + 2) + payload + b'\xff\xd9'


def latitude(metres):
    return 10 + math.degrees(metres / 6_371_008.8)


class PhotosRefsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.photos = self.root / 'photos'
        self.photos.mkdir()
        self.out = self.root / 'out'
        self.photo('a.jpg', 0)

    def photo(self, name, metres, second=0):
        (self.photos / name).write_bytes(jpeg(latitude(metres), 106, second))

    def inventory(self, rows, name='existing.json', pages=False):
        items = [{'external_ref': ref, 'pole_id': f'POLE-{i:04d}',
                  'location': {'lat': latitude(metres), 'lng': 106}}
                 for i, (ref, metres) in enumerate(rows, 1)]
        path = self.root / name
        if name.endswith('.csv'):
            with path.open('w', newline='', encoding='utf-8-sig') as handle:
                writer = csv.writer(handle)
                writer.writerow(['external_ref', 'geom_wkt'])
                writer.writerows((item['external_ref'], f"POINT(106 {item['location']['lat']})") for item in items)
        else:
            path.write_text(json.dumps([{'items': [item]} for item in items] if pages else {'items': items}))
        return path

    def run_tool(self, *args, ok=True):
        result = subprocess.run([sys.executable, str(SCRIPT), str(self.photos), '--out', str(self.out),
                                 '--ref-prefix', 'KS', '--no-interpolate', *map(str, args)],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode == 0, ok, result.stdout + result.stderr)
        if not ok:
            self.assertFalse(self.out.exists(), result.stderr)
            self.assertNotIn('Traceback', result.stderr)
        return result

    def rows(self, name):
        with (self.out / name).open(encoding='utf-8-sig', newline='') as handle:
            return list(csv.DictReader(handle))

    def assert_review(self, kind, count=1):
        self.assertEqual(self.rows('poles.csv'), [])
        self.assertEqual(self.rows('position_updates.csv'), [])
        self.assertTrue(all(row['external_ref'] == '' for row in self.rows('observations.csv')))
        self.assertEqual(sum(row['kind'] == kind for row in self.rows('review.csv')), count)

    def test_match_old_ref_without_asset_or_fixture_update(self):
        existing = self.inventory([('PERMANENT', 3)])
        self.run_tool('--existing', existing, '--fixture-watt', '150', '--fixture-install-date', '2020-01-01')
        self.assertEqual(self.rows('poles.csv'), [])
        self.assertEqual(self.rows('fixtures.csv'), [])
        obs = self.rows('observations.csv')[0]
        self.assertEqual((obs['external_ref'], obs['existing_pole']), ('PERMANENT', 'POLE-0001'))
        update = self.rows('position_updates.csv')[0]
        self.assertEqual(update['external_ref'], 'PERMANENT')
        self.assertEqual(update['distance_m'], '3.0')
        self.assertEqual(update['photo_count'], '1')
        self.assertAlmostEqual(float(update['old_lat']), latitude(3), places=7)
        self.assertEqual(update['proposed_lat'], '10.0000000')
        self.assertEqual((self.out / 'photos/P001_a.jpg').read_bytes(), (self.photos / 'a.jpg').read_bytes())

    def test_two_nearby_old_poles(self):
        self.run_tool('--existing', self.inventory([('A', 2), ('B', 6)]))
        self.assert_review('gan_cot_cu')

    def test_between_match_and_review(self):
        self.run_tool('--existing', self.inventory([('A', 12)]))
        self.assert_review('gan_cot_cu')

    def test_two_groups_claim_one_pole(self):
        self.photo('b.jpg', 4, 40)
        self.run_tool('--existing', self.inventory([('A', 2)]))
        self.assert_review('trung_cot_cu', 2)

    def test_merge_before_matching(self):
        self.photo('b.jpg', 4, 40)
        self.run_tool('--existing', self.inventory([('A', 2)]), '--merge', 'P001=P002')
        self.assertEqual(self.rows('observations.csv')[0]['external_ref'], 'A')
        self.assertEqual(self.rows('position_updates.csv')[0]['photo_count'], '2')
        self.assertFalse(any(r['kind'] == 'trung_cot_cu' for r in self.rows('review.csv')))

    def test_drop_before_matching(self):
        self.photo('b.jpg', 4, 40)
        self.run_tool('--existing', self.inventory([('A', 2)]), '--drop', 'P002')
        self.assertEqual(self.rows('observations.csv')[0]['external_ref'], 'A')

    def test_new_pole_and_repeatable_ref(self):
        existing = self.inventory([('A', 50)])
        self.run_tool('--existing', existing)
        first = self.rows('poles.csv')
        self.assertEqual(first[0]['external_ref'], 'KS-20261003-190000')
        self.assertEqual(self.rows('observations.csv')[0]['external_ref'], first[0]['external_ref'])
        self.out = self.root / 'rerun'
        self.run_tool('--existing', existing)
        self.assertEqual(self.rows('poles.csv'), first)

    def test_match_does_not_consume_new_ref_same_second(self):
        self.photo('b.jpg', 50)
        self.run_tool('--existing', self.inventory([('OLD', 0)]))
        self.assertEqual(self.rows('poles.csv')[0]['external_ref'], 'KS-20261003-190000')
        self.assertEqual([r['external_ref'] for r in self.rows('observations.csv')],
                         ['OLD', 'KS-20261003-190000'])

    def test_drop_preserves_new_suffix(self):
        self.photo('b.jpg', 50)
        self.run_tool('--first-survey', '--drop', 'P001')
        self.assertEqual(self.rows('poles.csv')[0]['external_ref'], 'KS-20261003-190000-2')

    def test_missing_existing_stops(self):
        self.assertIn('--first-survey', self.run_tool(ok=False).stderr)

    def test_first_survey(self):
        self.run_tool('--first-survey')
        self.assertEqual(len(self.rows('poles.csv')), 1)

    def test_csv_and_json_pages_repeated_arguments(self):
        self.photo('b.jpg', 50, 40)
        csv_path = self.inventory([('CSV', 0)], 'poles.csv')
        pages = self.inventory([('JSON', 50), ('FAR', 150)], 'pages.json', pages=True)
        self.run_tool('--existing', csv_path, '--existing', pages)
        self.assertEqual([r['external_ref'] for r in self.rows('observations.csv')], ['CSV', 'JSON'])
        self.assertEqual(self.rows('poles.csv'), [])

    def test_empty_and_duplicate_refs_stop_before_writing(self):
        for name in ('existing.json', 'poles.csv'):
            for rows in ([('', 0)], [('  ', 0)], [('A', 0), ('A', 50)]):
                with self.subTest(name=name, rows=rows):
                    result = self.run_tool('--existing', self.inventory(rows, name), ok=False)
                    self.assertIn('external_ref', result.stderr)

    def test_duplicate_across_files(self):
        a = self.inventory([('A', 0)], 'a.json')
        b = self.inventory([('A', 50)], 'b.csv')
        self.assertIn('trùng', self.run_tool('--existing', a, '--existing', b, ok=False).stderr)

    def test_legacy_geojson_warns_and_never_assigns(self):
        path = self.root / 'old.geojson'
        path.write_text(json.dumps({'type': 'FeatureCollection', 'features': [
            {'properties': {'pole_id': 'POLE-0001'}, 'geometry': {'type': 'Point', 'coordinates': [106, 10]}}]}))
        self.assertIn('không có external_ref', self.run_tool('--existing', path).stderr)
        self.assert_review('gan_cot_cu')

    def test_invalid_existing_stops_cleanly(self):
        for value in ('{broken', '{"wrong": []}', 'external_ref,geom_wkt\nA,LINESTRING(1 2)'):
            with self.subTest(value=value):
                path = self.root / 'bad'
                path.write_text(value)
                self.assertIn('--existing', self.run_tool('--existing', path, ok=False).stderr)

    def test_invalid_match_radius(self):
        for radius in ('nan', '-1', '21'):
            with self.subTest(radius=radius):
                self.run_tool('--first-survey', '--match-m', radius, ok=False)

    def test_new_ref_collision_with_distant_old_pole_stops(self):
        self.run_tool('--existing', self.inventory([('KS-20261003-190000', 50)]), ok=False)

    def test_custom_match_radius(self):
        self.run_tool('--existing', self.inventory([('A', 12)]), '--match-m', '15')
        self.assertEqual(self.rows('observations.csv')[0]['external_ref'], 'A')


if __name__ == '__main__':
    unittest.main()
