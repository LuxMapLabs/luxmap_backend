#!/usr/bin/env python3
"""Copies the KEPT rows of the local development database into an empty, already-migrated target.

    export LUXMAP_TARGET_URL='postgresql://postgres.<ref>@<pooler-host>:5432/postgres?sslmode=verify-full&sslrootcert=/path/prod-ca-2021.crt'
    export PGPASSWORD='<target password>'          # or put it in the URL; it is never printed
    python3 scripts/copy_dev_to_supabase.py            # preflight + plan, writes NOTHING
    python3 scripts/copy_dev_to_supabase.py --apply    # copy in ONE transaction, then verify

Written for OPS-SUPABASE (decisions D-1..D-7 in .ai/results/OPS-SUPABASE-p1.md, 01/10/2026): option A,
"migrate the target, then copy filtered data". Run it from the separate deploy workspace (D-6), with the
local API and every test run stopped (D-7) so the source does not move under the copy.

WHAT IS KEPT. Communes COM-070 (FO-26 mock), COM-001 Phường Long Phước and COM-002 Phường Long Bình; the
four seed accounts with their password hashes; every business row of those communes, with its own ids
(POLE-0047 stays POLE-0047). WHAT IS LEFT BEHIND: test communes and accounts that integration runs left
in luxmap_dev, every refresh token (everyone signs in again), the EF migration history (the target has
its own) and PostGIS's spatial_ref_sys (the extension provides it).

WHY IT REFUSES rather than adapts. The target must carry exactly the source's migration history and be
empty: copying into a half-filled database or a different schema is how ids collide or columns land in
the wrong place. A table that exists in the source but is not in the plan below stops the run — a new
table must be added here on purpose, not skipped by default. The FK order of the plan is checked against
the source catalogue before anything is read, because the target's foreign keys are not deferrable.

Both sessions run with client encoding UTF8, DateStyle ISO and time zone UTC, and every COPY names its
encoding: inherited PGCLIENTENCODING or DateStyle would otherwise turn Vietnamese names or day/month into
something else while COPY still reports success. Before COMMIT, every table's CONTENT is compared with
the source — an md5 over the text of each row, sorted with the "C" collation so the two servers'
collations cannot reorder it — and any difference aborts the whole transaction. Equal counts prove
nothing about the rows themselves.

A remote target must use sslmode=verify-full with a CA (D-3); only localhost, for rehearsals, may skip TLS.
Inherited PG* variables are dropped so a PGHOSTADDR or PGSSLMODE left in the shell cannot redirect or
weaken the connection. PostgreSQL errors are printed terse, without DETAIL/CONTEXT: a rejected app_user
row would otherwise be echoed whole — password hash included — into the terminal.

Sequences are set to the SOURCE's current value (D-5): ids already handed out — in a browser, a phone's
offline queue, a screenshot — are never issued again for a different row.
"""

import argparse
import os
import re
import subprocess
import sys
import tempfile
from pathlib import Path
from urllib.parse import parse_qs, unquote, urlparse

KEEP_COMMUNES = ("COM-070", "COM-001", "COM-002")
SEED_USERS = ("admin", "agency", "engineer", "crew")

IN_COMMUNES = f"commune_id IN ({', '.join(repr(c) for c in KEEP_COMMUNES)})"
IN_USERS = f"username IN ({', '.join(repr(u) for u in SEED_USERS)})"

# (table, row filter, ORDER BY). Order = foreign-key order; checked against the catalogue at runtime.
PLAN = [
    ("administrative_unit", IN_COMMUNES, "commune_id"),
    ("app_user", IN_USERS, "user_id"),
    ("app_user_commune", f"{IN_COMMUNES} AND user_id IN (SELECT user_id FROM public.app_user WHERE {IN_USERS})",
     "user_id, commune_id"),
    ("road_segment", IN_COMMUNES, "segment_id"),
    ("feeder", IN_COMMUNES, "feeder_id"),
    ("pole", IN_COMMUNES, "pole_id"),
    ("fixture", IN_COMMUNES, "fixture_id"),
    ("pole_current_status", IN_COMMUNES, "pole_id"),
    ("iot_node", IN_COMMUNES, "node_id"),
    ("feeder_control", IN_COMMUNES, "feeder_id"),
    ("fault_cluster", IN_COMMUNES, "cluster_id"),
    ("fault", IN_COMMUNES, "fault_id"),
    ("lux_reading", IN_COMMUNES, "lux_id"),
    # A follow-up work order points at its parent and root: roots first, then by creation time.
    ("work_order", IN_COMMUNES, "root_work_order_id IS NOT NULL, created_at, work_order_id"),
    ("work_order_fault", IN_COMMUNES, "work_order_id, fault_id"),
    ("audit_event", IN_COMMUNES, "audit_id"),
]
SKIPPED = {
    "refresh_token": "phiên đăng nhập — mọi người đăng nhập lại (D-5)",
    "__ef_migrations_history": "đích có lịch sử riêng do migration ghi",
    "spatial_ref_sys": "bảng của PostGIS, extension ở đích tự có",
}


# Fixed for BOTH sessions: an inherited setting must not change how text, dates or times are read.
SESSION = {"PGCLIENTENCODING": "UTF8", "PGDATESTYLE": "ISO", "PGTZ": "UTC"}


LOCAL_HOSTS = ("localhost", "127.0.0.1", "::1")


class Refused(Exception):
    pass


def terse(stderr: str) -> str:
    """Error text without DETAIL/CONTEXT/HINT lines, which can carry whole rows of data."""
    return "\n".join(line for line in stderr.strip().splitlines()
                     if not line.lstrip().startswith(("DETAIL:", "CONTEXT:", "HINT:")))


def target_env() -> dict:
    url = os.environ.get("LUXMAP_TARGET_URL")
    if not url:
        raise Refused("Thiếu LUXMAP_TARGET_URL (chuỗi postgresql://… của DB đích).")
    parts = urlparse(url)
    if parts.scheme not in ("postgresql", "postgres") or not parts.hostname:
        raise Refused("LUXMAP_TARGET_URL phải có dạng postgresql://user@host:port/db?sslmode=…")
    query = {k: v[-1] for k, v in parse_qs(parts.query).items()}
    if parts.hostname not in LOCAL_HOSTS:
        if query.get("sslmode") != "verify-full":
            raise Refused("đích không phải localhost phải dùng sslmode=verify-full (D-3)")
        cert = query.get("sslrootcert")
        if not cert or (cert != "system" and not Path(cert).is_file()):
            raise Refused("verify-full cần sslrootcert trỏ tới file chứng chỉ CA có thật của project")
    password = unquote(parts.password) if parts.password else os.environ.get("PGPASSWORD")
    # Every inherited PG* variable goes: only what this URL says may shape the connection.
    env = {k: v for k, v in os.environ.items() if not k.startswith("PG")}
    env.update(PGHOST=parts.hostname, PGPORT=str(parts.port or 5432),
               PGDATABASE=unquote(parts.path.lstrip("/") or "postgres"), PGUSER=unquote(parts.username or ""))
    if password:
        env["PGPASSWORD"] = password
    for key, var in (("sslmode", "PGSSLMODE"), ("sslrootcert", "PGSSLROOTCERT")):
        if key in query:
            env[var] = query[key]
    return env | SESSION


class Db:
    def __init__(self, name: str, argv: list[str], env: dict | None = None):
        self.name, self.argv, self.env = name, argv, env

    def run(self, sql: str = "", *, script: str | None = None) -> str:
        argv = [*self.argv, "-X", "-q", "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=terse"]
        argv += ["-f", script] if script else ["-At", "-F", "\t", "-c", sql]
        result = subprocess.run(argv, env=self.env, capture_output=True, text=True, encoding="utf-8")
        if result.returncode != 0:
            raise Refused(f"{self.name}: {terse(result.stderr)}")
        return result.stdout

    def copy_out(self, sql: str) -> bytes:
        """COPY … TO STDOUT as raw bytes, so no locale ever decodes or re-encodes the data."""
        result = subprocess.run([*self.argv, "-X", "-q", "-v", "ON_ERROR_STOP=1", "-v", "VERBOSITY=terse",
                                 "-c", sql], env=self.env, capture_output=True)
        if result.returncode != 0:
            raise Refused(f"{self.name}: {terse(result.stderr.decode('utf-8', 'replace'))}")
        return result.stdout

    def rows(self, sql: str) -> list[list[str]]:
        return [line.split("\t") for line in self.run(sql).splitlines() if line]

    def scalar(self, sql: str) -> str:
        return self.run(sql).strip()


def digest_sql(table: str, cols: str, where: str | None) -> str:
    """md5 over every row's text, sorted under the "C" collation so server collations cannot differ."""
    filtered = f" WHERE {where}" if where else ""
    return (f"SELECT md5(coalesce(string_agg(r, E'\\n' ORDER BY r COLLATE \"C\"), '')) "
            f"FROM (SELECT ROW({cols})::text AS r FROM public.{table}{filtered}) AS rows")


def unsafe_sequences(states: dict[str, tuple[int, bool]], maxima: dict[str, int]) -> list[str]:
    """Sequences whose NEXT value would hand out an id that a kept row already has."""
    problems = []
    for name, top in maxima.items():
        last, called = states[name]
        following = last + 1 if called else last
        if following <= top:
            problems.append(f"{name}: lần cấp kế tiếp {following} ≤ ID lớn nhất đang giữ {top}")
    return problems


def check_sequences(src: Db) -> dict[str, tuple[int, bool]]:
    """Reads every sequence and refuses a source that would reissue a kept id (D-5 keeps its value)."""
    owners = {}  # sequence -> (table, column), from the catalogue rather than a hand-kept list
    for table, column, default in src.rows(
            "SELECT c.relname, a.attname, pg_get_expr(d.adbin, d.adrelid) FROM pg_attrdef d "
            "JOIN pg_class c ON c.oid = d.adrelid JOIN pg_attribute a ON a.attrelid = d.adrelid AND a.attnum = d.adnum "
            "WHERE c.relnamespace = 'public'::regnamespace AND pg_get_expr(d.adbin, d.adrelid) LIKE '%nextval(%'"):
        if found := re.search(r"nextval\('([a-z0-9_]+)'", default):
            owners[found.group(1)] = (table, column)
    for table, column, sequence in src.rows(
            "SELECT table_name, column_name, pg_get_serial_sequence('public.' || table_name, column_name) "
            "FROM information_schema.columns WHERE table_schema = 'public' AND is_identity = 'YES'"):
        owners[sequence.removeprefix("public.")] = (table, column)

    states = {}
    for (name,) in src.rows("SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' ORDER BY 1"):
        last, called = src.rows(f"SELECT last_value, is_called FROM public.{name}")[0]
        states[name] = (int(last), called == "t")
    where = {table: filt for table, filt, _ in PLAN}
    maxima = {}
    for name, (table, column) in owners.items():
        if table in where and name in states:
            maxima[name] = int(src.scalar(
                f"SELECT coalesce(max(substring({column}::text from '[0-9]+$')::bigint), 0) "
                f"FROM public.{table} WHERE {where[table]}"))
    if problems := unsafe_sequences(states, maxima):
        raise Refused("sequence ở nguồn sẽ cấp lại ID đã có — đẩy sequence nguồn lên trước khi chép: "
                      + "; ".join(problems))
    print(f"sequence: {len(maxima)} cái kiểm, đều cấp tiếp trên ID lớn nhất đang giữ")
    return states


def preflight(src: Db, dst: Db) -> dict[str, list[str]]:
    if (dbname := src.scalar("SELECT current_database()")) != "luxmap_dev":
        raise Refused(f"nguồn là {dbname!r}, không phải luxmap_dev")
    where, user, superuser = dst.rows("SELECT current_database(), current_user, "
                                      "(SELECT rolsuper FROM pg_roles WHERE rolname = current_user)")[0]
    print(f"đích: database {where}, user {user}, superuser {superuser}")

    history = "SELECT migration_id FROM public.__ef_migrations_history ORDER BY migration_id"
    src_history, dst_history = src.run(history).split(), dst.run(history).split()
    if src_history != dst_history:
        missing = sorted(set(src_history) - set(dst_history))
        raise Refused(f"lịch sử migration khác nhau (đích {len(dst_history)}, nguồn {len(src_history)}; "
                      f"đích thiếu {missing[:3]}…) — chạy `dotnet ef database update` ở đích trước")
    print(f"migration: {len(src_history)} khớp nhau, mới nhất {src_history[-1]}")
    postgis = dst.scalar("SELECT extnamespace::regnamespace FROM pg_extension WHERE extname = 'postgis'")
    if not postgis:
        raise Refused("đích chưa có PostGIS")
    print(f"PostGIS ở đích: schema {postgis}")

    tables = set(src.run("SELECT tablename FROM pg_tables WHERE schemaname = 'public'").split())
    planned = [t for t, _, _ in PLAN]
    if unplanned := sorted(tables - set(planned) - set(SKIPPED)):
        raise Refused(f"bảng chưa có trong kế hoạch: {unplanned} — thêm vào PLAN có chủ đích")
    position = {t: i for i, t in enumerate(planned)}
    for child, parent in src.rows(
            "SELECT c.conrelid::regclass::text, c.confrelid::regclass::text FROM pg_constraint c "
            "WHERE c.contype = 'f' AND c.connamespace = 'public'::regnamespace"):
        child, parent = child.removeprefix("public."), parent.removeprefix("public.")
        if child in position and parent != child and position.get(parent, -1) >= position[child]:
            raise Refused(f"thứ tự khoá ngoại sai: {child} trỏ tới {parent} nhưng {parent} không đứng trước")

    columns = {}
    for table in planned:
        query = ("SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' "
                 f"AND table_name = '{table}' AND is_generated = 'NEVER' ORDER BY ordinal_position")
        cols, dst_cols = src.run(query).split(), dst.run(query).split()
        if sorted(cols) != sorted(dst_cols):
            raise Refused(f"{table}: cột ở nguồn và đích khác nhau")
        if (count := int(dst.scalar(f"SELECT count(*) FROM public.{table}"))) != 0:
            raise Refused(f"{table} ở đích đã có {count} dòng — script chỉ chép vào đích rỗng")
        columns[table] = cols
    return columns


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--apply", action="store_true", help="chép thật; thiếu cờ này chỉ kiểm và in kế hoạch")
    parser.add_argument("--container", default="luxmap_postgres")
    parser.add_argument("--user", default="luxmap")
    args = parser.parse_args()

    try:
        session = [arg for key, value in SESSION.items() for arg in ("-e", f"{key}={value}")]
        src = Db("nguồn", ["docker", "exec", "-i", *session, args.container, "psql", "-U", args.user,
                           "-d", "luxmap_dev"])
        dst = Db("đích", ["psql"], target_env())
        columns = preflight(src, dst)

        print("\nkế hoạch (bảng: giữ / bỏ lại):")
        expected = {}
        for table, where, _ in PLAN:
            kept = int(src.scalar(f"SELECT count(*) FROM public.{table} WHERE {where}"))
            total = int(src.scalar(f"SELECT count(*) FROM public.{table}"))
            expected[table] = kept
            print(f"  {table:22} {kept:6} / {total - kept}")
        for table, why in SKIPPED.items():
            print(f"  {table:22} bỏ qua — {why}")
        values = {name: [str(last), "t" if called else "f"] for name, (last, called) in check_sequences(src).items()}
        if not args.apply:
            print("\nChưa ghi gì. Thêm --apply để chép.")
            return 0

        with tempfile.TemporaryDirectory(prefix="luxmap-copy-") as tmp:  # holds password hashes: 0700, removed after
            lines, checks = ["BEGIN;"], []
            for table, where, order in PLAN:
                cols = ", ".join(f'"{c}"' for c in columns[table])
                data = Path(tmp) / f"{table}.copy"
                data.write_bytes(src.copy_out(f"COPY (SELECT {cols} FROM public.{table} WHERE {where} "
                                              f"ORDER BY {order}) TO STDOUT WITH (ENCODING 'UTF8')"))
                lines.append(f"\\copy public.{table} ({cols}) FROM '{data}' WITH (ENCODING 'UTF8')")
                expected_digest = src.scalar(digest_sql(table, cols, where))
                checks.append(f"DO $check$ BEGIN IF ({digest_sql(table, cols, None)}) IS DISTINCT FROM "
                              f"'{expected_digest}' THEN RAISE EXCEPTION 'nội dung bảng {table} lệch nguồn'; "
                              f"END IF; END $check$;")
            # Content first: setval is not undone by ROLLBACK, so nothing touches a sequence until every
            # table is proven identical to the source.
            lines += checks
            for name, (last, called) in values.items():
                lines.append(f"SELECT setval('public.{name}', {last}, {'true' if called == 't' else 'false'});")
            lines.append("COMMIT;")
            script = Path(tmp) / "load.sql"
            script.write_text("\n".join(lines) + "\n")
            dst.run(script=str(script))

        print("\nđã chép. kiểm lại:")
        bad = 0
        for table, _, _ in PLAN:
            got = int(dst.scalar(f"SELECT count(*) FROM public.{table}"))
            bad += got != expected[table]
            print(f"  {table:22} {got:6} {'✅' if got == expected[table] else f'❌ mong đợi {expected[table]}'}")
        for name, (last, called) in values.items():
            got = dst.rows(f"SELECT last_value, is_called FROM public.{name}")[0]
            bad += got != [last, called]
            if got != [last, called]:
                print(f"  ❌ sequence {name}: {got} ≠ {[last, called]}")
        print(f"  sequence: {len(values)} cái {'khớp' if not bad else 'có lệch'}")
        print(f"  nội dung: {len(PLAN)} bảng trùng md5 với nguồn (đã kiểm TRƯỚC commit)")
        return 1 if bad else 0
    except Refused as error:
        print(f"DỪNG: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
