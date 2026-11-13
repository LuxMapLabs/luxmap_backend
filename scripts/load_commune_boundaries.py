#!/usr/bin/env python3
"""Sets commune outlines (COMMUNE-BOUNDARY) through the admin API, one PUT per feature.

    python3 scripts/load_commune_boundaries.py                       # list what would be sent, change nothing
    python3 scripts/load_commune_boundaries.py --apply               # send it to http://localhost:5141
    python3 scripts/load_commune_boundaries.py --apply --api https://example.org

The file is a GeoJSON FeatureCollection; each feature needs `properties.commune_id` and a Polygon or MultiPolygon geometry.
The default, scripts/data/commune-boundaries.geojson, holds the two study communes as surveyed on 28/09/2026:
COM-001 = Phường Long Phước (OSM relation 7342308), COM-002 = Phường Long Bình (OSM relation 7342307), downloaded from
api.openstreetmap.org on 01/10/2026. Data © OpenStreetMap contributors, ODbL — credit the source wherever it is shown.

It goes through PUT /api/v1/admin/communes/{id}/boundary rather than SQL so the server's validation runs on every outline.
The system admin signs in with LUXMAP_ADMIN_USERNAME (default `admin`) and LUXMAP_ADMIN_PASSWORD, read from the environment —
never from a file in the repository.
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

DEFAULT_FILE = Path(__file__).resolve().parent / "data" / "commune-boundaries.geojson"


def request(method, url, body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = None if body is None else json.dumps(body).encode("utf-8")
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    try:
        with urllib.request.urlopen(req) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, json.loads(error.read() or b"{}")


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("file", nargs="?", type=Path, default=DEFAULT_FILE)
    parser.add_argument("--api", default="http://localhost:5141", help="API origin, without /api/v1")
    parser.add_argument("--apply", action="store_true", help="send the PUTs; without it nothing is changed")
    args = parser.parse_args()

    features = json.loads(args.file.read_text(encoding="utf-8"))["features"]
    for feature in features:
        geometry = feature["geometry"]
        print(f"{feature['properties']['commune_id']}: {geometry['type']}, {feature['properties'].get('name', '')}")
    if not args.apply:
        print("Dry run — pass --apply to send.")
        return 0

    password = os.environ.get("LUXMAP_ADMIN_PASSWORD")
    if not password:
        print("LUXMAP_ADMIN_PASSWORD is not set.", file=sys.stderr)
        return 2
    base = args.api.rstrip("/") + "/api/v1"
    status, login = request("POST", f"{base}/auth/login",
                            {"username": os.environ.get("LUXMAP_ADMIN_USERNAME", "admin"), "password": password})
    if status != 200:
        print(f"Sign-in failed ({status}): {login.get('error', login)}", file=sys.stderr)
        return 1

    failed = 0
    for feature in features:
        commune_id = feature["properties"]["commune_id"]
        status, body = request("PUT", f"{base}/admin/communes/{commune_id}/boundary",
                               {"boundary": feature["geometry"]}, login["access_token"])
        if status == 200:
            print(f"{commune_id}: set (has_boundary={body['has_boundary']})")
        else:
            failed += 1
            print(f"{commune_id}: {status} {body.get('error', body)}", file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
