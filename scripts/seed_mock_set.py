#!/usr/bin/env python3
"""Loads the FO-26 mock set into a development database, keeping the mock's own display ids.

    python3 scripts/seed_mock_set.py            # print the SQL, change nothing
    python3 scripts/seed_mock_set.py --apply    # run it against the compose container

⚠️ A STOPGAP, not BE-39. BE-39 owns demo data and will most likely be a real seeder reached through
`dotnet run -- --seed`, next to IdentitySeeder. This exists because BE-13 and BE-14 need rows to
develop against now, and because re-seeding after `docker compose down -v` should not be a manual job.

WHY THE IDS ARE WRITTEN OUT instead of letting the sequences assign them. The front end hardcodes
`POLE-0047`, so the seeded ids have to be the mock's ids. Loading the same files through the real
import endpoint does NOT achieve that even on an empty database with the sequences reset: EF Core
does not preserve the order rows were added in when the database generates the key, and a measured
run put 102 of 103 poles under a different id than the mock's — `POLE-0047` came out as the mock's
`POLE-0062`. Writing the ids is the approach decision D-6 settled on (docs/contract-drift.md), and it
is a SYSTEM action: Contract section 1.2 forbids a CLIENT from inventing a display id, which this is
not. Sequences are pushed past the seeded range at the end so later inserts cannot collide.

WHAT IT DOES NOT SEED, and why:

  feeder_id          The mock set carries no real circuit (open item O-6). Since BE-14b the script
                     creates ONE TEMPORARY DEMO feeder per segment (FDR-001..003, external_ref
                     DEMO-SEG-00n) and hangs every pole on its segment's feeder, so the cabinet
                     devices have lamps to reach (decision I-11, 28/09/2026). This is NOT circuit
                     data: CV-15 must not treat it as topology, and O-6 replaces it.
  pole_current_status  Its writes belong to BE-15/BE-17 (CLAUDE.md). The mock does carry
                     fixture_status, status_confidence, last_seen_at and last_sweep_id, so this is a
                     deliberate omission rather than a missing feature.
  survey_sweep, survey_frame, luminance_history
                     Those tables do not exist yet.
  iot_node           Seeded from mock-iot-nodes.geojson — the three cabinet devices, each switching
                     its segment's demo feeder on relay 1. data_source = simulated (demo rows, not
                     testbed hardware), supports_remote_control = false. The testbed's own device
                     (two feeders, odd/even lamps) is seeded later, once the rig has coordinates.
  Work orders are linked through work_order_fault (BE-23), with kinds in the companion CSV.
  Their audit history starts empty; existing work order audit prevents re-seeding.

Re-running it is safe for the mock set itself: one transaction, and `--apply` uses ON_ERROR_STOP
so a failure rolls the whole thing back.

⚠️ But the DELETEs are UNQUALIFIED. `fault`, `fault_cluster`, `fixture`, `pole` and `road_segment`
are emptied outright, not filtered to the rows this script wrote — so anything else on that database
(assets imported by hand, faults created while testing) goes too. `lux_reading` and existing work-order audit are protected by RAISE guards at the top of
`statements()`: research readings and audit history must not be erased or attached to reused IDs. Point this at a
development database you are willing to lose, never at anything shared that holds work. Scoping
the deletes to the seeded `external_ref` values belongs to the real BE-39 seeder that replaces it.
"""
import argparse
import csv
import json
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MOCKS = ROOT / "mocks"

# Every seeded row belongs to the commune BE-06 seeds. Resolved in SQL rather than here, so the
# script needs no database connection to produce its output and stays correct when the id changes —
# it comes from a sequence, so it records which run created the row and is never a constant.
COMMUNE = "(SELECT commune_id FROM administrative_unit WHERE seed_key = 'study_site')"

# Branch C: these assets are traced from public night imagery (Contract section 1.6).
DATA_SOURCE = "'public_imagery'"


def quote(value) -> str:
    """A SQL literal, or NULL. Single quotes are doubled; nothing else is interpolated."""
    return "NULL" if value is None else "'" + str(value).replace("'", "''") + "'"


def geometry(geojson: dict) -> str:
    """GeoJSON to a 4326 geometry. The SRID is stamped explicitly: the reader does not supply one."""
    return f"ST_SetSRID(ST_GeomFromGeoJSON({quote(json.dumps(geojson))}), 4326)"


def load(name: str):
    return json.loads((MOCKS / name).read_text(encoding="utf-8"))


def statements() -> list[str]:
    poles = load("mock-poles.geojson")["features"]
    segments = load("mock-segments.geojson")["features"]
    faults = load("mock-faults.json")["items"]
    nodes = load("mock-iot-nodes.geojson")["features"]

    # One temporary demo feeder per segment, in segment-id order: FDR-001 for SEG-001, and so on.
    segment_ids = sorted(f["properties"]["segment_id"] for f in segments)
    demo_feeder = {sid: f"FDR-{i:03d}" for i, sid in enumerate(segment_ids, start=1)}
    for node in nodes:
        p = node["properties"]
        wanted = [demo_feeder[sid] for sid in p["segment_ids"]]
        if p["node_role"] != "segment_controller" or p["feeder_ids"] != wanted:
            raise SystemExit(f"{p['node_id']} does not match the demo wiring {wanted}.")

    orders = load("mock-work-orders.json")["items"]
    with (MOCKS / "mock-work-order-kinds.csv").open(encoding="utf-8", newline="") as file:
        kinds = {row["work_order_id"]: row["task_kind"] for row in csv.DictReader(file)}
    if set(kinds) != {wo["work_order_id"] for wo in orders} or not set(kinds.values()) <= {"inspection", "repair"}:
        raise SystemExit("Work order kinds must cover exactly the mock work orders.")
    in_progress = {fid for wo in orders if kinds[wo["work_order_id"]] == "repair"
                   and wo["wo_status"] == "in_progress" for fid in wo["fault_ids"]}
    sql = ["BEGIN;"]
    sql.append(f"""
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM audit_event WHERE entity_type = 'work_order') THEN
    RAISE EXCEPTION 'Work order audit exists; re-seeding would reuse its entity IDs.';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM app_user u JOIN app_user_commune c USING (user_id)
      WHERE u.username = 'crew' AND u.role = 'field_engineer' AND NOT u.is_locked
      AND c.commune_id = {COMMUNE}) THEN
    RAISE EXCEPTION 'crew is not eligible in study_site';
  END IF;
END $$;""".strip())

    # A lux reading is the ground truth for RQ1 and points at a pole with RESTRICT. If any exist this
    # script must not be the thing that decides they can go.
    sql.append("""
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM lux_reading) THEN
    RAISE EXCEPTION 'lux_reading is not empty. It is the RQ1 ground truth and this script will not delete it; clear it by hand if you really mean to re-seed.';
  END IF;
END $$;""".strip())

    # Foreign-key order: fault before the rows it points at, fixture before pole.
    # feeder_control holds both feeder and iot_node; feeder is freed only once no pole points at it.
    sql.append("DELETE FROM work_order_fault; DELETE FROM work_order; DELETE FROM fault; DELETE FROM fault_cluster; "
               "DELETE FROM feeder_control; DELETE FROM iot_node; "
               "DELETE FROM fixture; DELETE FROM pole; DELETE FROM feeder; DELETE FROM road_segment;")

    for feature in segments:
        p = feature["properties"]
        sql.append(
            "INSERT INTO road_segment (segment_id, segment_name, road_class, length_m, geom, "
            "commune_id, data_source, external_ref) VALUES ("
            f"{quote(p['segment_id'])}, {quote(p['segment_name'])}, {quote(p['road_class'])}, "
            f"{p['length_m']}, {geometry(feature['geometry'])}, {COMMUNE}, {DATA_SOURCE}, "
            # The mock's own id IS the authority's inventory code until a real one exists — the
            # settled position, since Branch C runs no field survey and none is ever coming.
            f"{quote(p['segment_id'])});")

    for sid in segment_ids:
        sql.append(
            "INSERT INTO feeder (feeder_id, feeder_name, commune_id, geom, external_ref) VALUES ("
            f"{quote(demo_feeder[sid])}, {quote(f'Mạch tạm {sid} (demo, chờ O-6)')}, {COMMUNE}, NULL, "
            f"{quote(f'DEMO-{sid}')});")

    for feature in poles:
        p = feature["properties"]
        sql.append(
            "INSERT INTO pole (pole_id, segment_id, feeder_id, commune_id, geom, "
            "near_sensitive_poi, data_source, external_ref) VALUES ("
            f"{quote(p['pole_id'])}, {quote(p['segment_id'])}, {quote(demo_feeder[p['segment_id']])}, {COMMUNE}, "
            f"{geometry(feature['geometry'])}, {str(p['near_sensitive_poi']).lower()}, "
            f"{DATA_SOURCE}, {quote(p['pole_id'])});")

    # Contract section 5.1 flattens the ACTIVE lamp into the pole's properties, so the mock keeps a
    # fixture's fields there and carries no fixture id of its own. FIX-nnnn is aligned with the pole's
    # number, which makes FIX-0047 the lamp on POLE-0047 and is the only readable choice.
    for index, feature in enumerate(poles, start=1):
        p = feature["properties"]
        sql.append(
            "INSERT INTO fixture (fixture_id, pole_id, commune_id, fixture_type, power_source, "
            "lamp_watt, install_date, removed_date, warranty_expiry, data_source) VALUES ("
            f"'FIX-{index:04d}', {quote(p['pole_id'])}, {COMMUNE}, {quote(p['fixture_type'])}, "
            f"{quote(p['power_source'])}, {p['lamp_watt']}, {quote(p['install_date'])}, NULL, "
            f"{quote(p.get('warranty_expiry'))}, {DATA_SOURCE});")

    # One cluster: the segment-wide outage the mock plants on SEG-003, which is what makes
    # has_active_segment_fault true for a whole road rather than for N separate lamps.
    clustered = [f for f in faults if f["cluster_id"]]
    for cluster_id in sorted({f["cluster_id"] for f in clustered}):
        members = [f for f in clustered if f["cluster_id"] == cluster_id]
        segment = sorted({f["segment_id"] for f in members})
        if len(segment) != 1:
            raise SystemExit(f"{cluster_id} spans {segment}; a cluster is one segment's outage.")

        sql.append(
            "INSERT INTO fault_cluster (cluster_id, segment_id, commune_id, clustered_at, "
            "clustering_model_version) VALUES ("
            f"{quote(cluster_id)}, {quote(segment[0])}, {COMMUNE}, "
            # Not in the mock. The earliest detection in the cluster is the defensible reading, and
            # it is recorded here rather than invented as "now" so re-seeding is reproducible.
            f"{quote(min(f['detected_at'] for f in members))}, "
            # BE-34 owns this. NULL says "no clustering run produced it", which is true: CV-15 has
            # not run, these rows are demo data.
            "NULL);")

    for f in faults:
        location = f["location"]
        sql.append(
            "INSERT INTO fault (fault_id, client_op_id, pole_id, fixture_id, segment_id, commune_id, "
            "lat, lng, fault_type, fault_status, severity, source_channel, data_source, "
            "priority_score, status_confidence, cluster_id, detected_at, updated_at, note, "
            "reported_by, confirmed_by, confirmed_at, resolved_by, resolved_at, "
            "detection_model_version) VALUES ("
            f"{quote(f['fault_id'])}, NULL, {quote(f['pole_id'])}, {quote(f['fixture_id'])}, "
            f"{quote(f['segment_id'])}, {COMMUNE}, {location['lat']}, {location['lng']}, "
            f"{quote(f['fault_type'])}, {quote('in_progress' if f['fault_id'] in in_progress else f['fault_status'])}, {quote(f['severity'])}, "
            f"{quote(f['source_channel'])}, {quote(f['data_source'])}, "
            f"{f['priority_score'] if f['priority_score'] is not None else 'NULL'}, "
            f"{f['status_confidence'] if f['status_confidence'] is not None else 'NULL'}, "
            f"{quote(f['cluster_id'])}, {quote(f['detected_at'])}, {quote(f['updated_at'])}, "
            f"{quote(f['note'])}, "
            # reported_by stays NULL for a fault the engine found, and that is the accurate answer
            # rather than missing data: source_channel already names the engine. No synthetic system
            # account — it would appear in every listing of who reports faults.
            f"{quote(f['reported_by'])}, NULL, NULL, NULL, NULL, NULL);")

    for node in nodes:
        p = node["properties"]
        sql.append(
            "INSERT INTO iot_node (node_id, commune_id, node_role, geom, supports_remote_control, "
            "data_source, last_report_at) VALUES ("
            f"{quote(p['node_id'])}, {COMMUNE}, {quote(p['node_role'])}, {geometry(node['geometry'])}, "
            f"{str(p['supports_remote_control']).lower()}, 'simulated', {quote(p['last_report_at'])});")
        for relay, feeder_id in enumerate(p["feeder_ids"], start=1):
            sql.append(
                "INSERT INTO feeder_control (feeder_id, node_id, commune_id, relay_no) VALUES ("
                f"{quote(feeder_id)}, {quote(p['node_id'])}, {COMMUNE}, {relay});")

    for wo in orders:
        assigned = "(SELECT user_id FROM app_user WHERE username = 'crew')" if wo["assigned_to"] else "NULL"
        created = quote(wo["created_at"])
        sql.append(
            "INSERT INTO work_order (work_order_id, commune_id, task_kind, title, wo_status, "
            "segment_id, cluster_id, assigned_to, assigned_at, created_by, due_date, started_at, created_at, updated_at) VALUES ("
            f"{quote(wo['work_order_id'])}, {COMMUNE}, {quote(kinds[wo['work_order_id']])}, {quote(wo['title'])}, "
            f"{quote(wo['wo_status'])}, {quote(wo['segment_id'])}, {quote(wo['cluster_id'])}, {assigned}, "
            f"{created if wo['assigned_to'] else 'NULL'}, (SELECT user_id FROM app_user WHERE username = 'engineer'), "
            f"{quote(wo['due_date'])}, {created if wo['wo_status'] == 'in_progress' else 'NULL'}, {created}, {created});")
        for fault_id in wo["fault_ids"]:
            sql.append("INSERT INTO work_order_fault (work_order_id, fault_id, commune_id, linked_at) VALUES ("
                       f"{quote(wo['work_order_id'])}, {quote(fault_id)}, {COMMUNE}, {created});")

    # Past the seeded range, or the next insert collides with a hand-written primary key. Reading the
    # numeric tail back out of the ids keeps this correct however many rows the mock grows to.
    for sequence, column, table, prefix in [
        ("work_order_id_seq", "work_order_id", "work_order", 4),
        ("feeder_id_seq", "feeder_id", "feeder", 5),
        ("node_id_seq", "node_id", "iot_node", 6),
        ("segment_id_seq", "segment_id", "road_segment", 5),
        ("pole_id_seq", "pole_id", "pole", 6),
        ("fixture_id_seq", "fixture_id", "fixture", 5),
        ("fault_id_seq", "fault_id", "fault", 7),
        ("cluster_id_seq", "cluster_id", "fault_cluster", 5),
    ]:
        sql.append(
            f"SELECT setval('{sequence}', "
            f"COALESCE((SELECT max(substring({column} from {prefix})::int) FROM {table}), 1));")

    sql.append("COMMIT;")
    return sql


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--apply", action="store_true",
                        help="run the SQL against the compose container instead of printing it")
    parser.add_argument("--container", default="luxmap_postgres")
    parser.add_argument("--database", default="luxmap_dev")
    parser.add_argument("--user", default="luxmap")
    args = parser.parse_args()

    sql = "\n".join(statements()) + "\n"

    if not args.apply:
        sys.stdout.write(sql)
        return 0

    # ON_ERROR_STOP matters: without it psql keeps going after a failed statement and the COMMIT at
    # the end would report success over a half-written database.
    result = subprocess.run(
        ["docker", "exec", "-i", args.container, "psql", "-U", args.user, "-d", args.database,
         "-v", "ON_ERROR_STOP=1", "-q"],
        input=sql, text=True)

    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
