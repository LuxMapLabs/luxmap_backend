"""Exercise the real PLAN predicates as SQL without opening any source/target database."""
import runpy
import sqlite3
import unittest
from pathlib import Path


class PolePhotoCopyPlanTests(unittest.TestCase):
    def test_excluded_photo_drops_its_fault_and_every_child_but_keeps_unrelated_faults(self):
        plan = runpy.run_path(str(Path(__file__).resolve().parents[1] / "copy_dev_to_supabase.py"))["PLAN"]
        filters = {table: predicate for table, predicate, _ in plan}
        # Predicates are ordinary SQL IN/NULL checks; an attached in-memory schema supplies the
        # PostgreSQL public qualifier. Execute the production expressions, not a Python rewrite.
        with sqlite3.connect(":memory:") as db:
            db.execute("ATTACH DATABASE ':memory:' AS public")
            db.executescript("""
                CREATE TABLE public.app_user(user_id TEXT, username TEXT);
                CREATE TABLE public.pole(pole_id TEXT, commune_id TEXT);
                CREATE TABLE public.work_order(work_order_id TEXT, commune_id TEXT);
                CREATE TABLE public.work_order_pole_photo(photo_id TEXT, work_order_id TEXT, pole_id TEXT,
                    commune_id TEXT, uploaded_by TEXT, decided_by TEXT);
                CREATE TABLE public.fault(fault_id TEXT, commune_id TEXT, origin_pole_photo_id TEXT);
                CREATE TABLE public.work_order_fault(work_order_id TEXT, fault_id TEXT, commune_id TEXT);
                CREATE TABLE public.repair_evidence(evidence_id TEXT, fault_id TEXT, commune_id TEXT);
                CREATE TABLE public.citizen_report(report_id TEXT, fault_id TEXT, commune_id TEXT, decided_by TEXT);
                CREATE TABLE public.qr_code(qr_id TEXT, commune_id TEXT, created_by TEXT, revoked_by TEXT);
                CREATE TABLE public.citizen_submission(submission_id TEXT, report_id TEXT, qr_id TEXT, commune_id TEXT);
                CREATE TABLE public.citizen_photo(photo_id TEXT, submission_id TEXT);
                INSERT INTO public.app_user VALUES ('kept-user','crew'), ('left-user','not-copied');
                INSERT INTO public.pole VALUES ('pole','COM-001');
                INSERT INTO public.work_order VALUES ('order','COM-001');
                INSERT INTO public.work_order_pole_photo VALUES
                    ('keep-photo','order','pole','COM-001','kept-user','kept-user'),
                    ('drop-photo','order','pole','COM-001','left-user','kept-user');
                INSERT INTO public.fault VALUES ('keep-fault','COM-001','keep-photo'),
                    ('drop-fault','COM-001','drop-photo'), ('ordinary-fault','COM-001',NULL);
                INSERT INTO public.work_order_fault VALUES ('order','keep-fault','COM-001'), ('order','drop-fault','COM-001');
                INSERT INTO public.repair_evidence VALUES ('keep-evidence','keep-fault','COM-001'),
                    ('drop-evidence','drop-fault','COM-001'), ('order-evidence',NULL,'COM-001');
                INSERT INTO public.citizen_report VALUES ('keep-report','keep-fault','COM-001','kept-user'),
                    ('drop-report','drop-fault','COM-001','kept-user'), ('pending-report',NULL,'COM-001',NULL);
                INSERT INTO public.qr_code VALUES ('qr','COM-001','kept-user',NULL);
                INSERT INTO public.citizen_submission VALUES ('keep-send','keep-report','qr','COM-001'),
                    ('drop-send','drop-report','qr','COM-001');
                INSERT INTO public.citizen_photo VALUES ('keep-citizen-photo','keep-send'), ('drop-citizen-photo','drop-send');
            """)
            cases = [
                ("work_order_pole_photo", "photo_id", ["keep-photo"]),
                ("fault", "fault_id", ["keep-fault", "ordinary-fault"]),
                ("work_order_fault", "fault_id", ["keep-fault"]),
                ("repair_evidence", "evidence_id", ["keep-evidence", "order-evidence"]),
                ("citizen_report", "report_id", ["keep-report", "pending-report"]),
                ("citizen_submission", "submission_id", ["keep-send"]),
                ("citizen_photo", "photo_id", ["keep-citizen-photo"]),
            ]
            for table, key, expected in cases:
                with self.subTest(table=table):
                    rows = db.execute(f"SELECT {key} FROM public.{table} WHERE {filters[table]}")
                    self.assertEqual(expected, sorted(row[0] for row in rows))
