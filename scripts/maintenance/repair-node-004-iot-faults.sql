-- psql only. Default: preview and rollback. Run after deploying IOT-FAULT-RECOVERY-1.
\set ON_ERROR_STOP on
\if :{?apply}
\else
\set apply false
\endif
BEGIN;
SET LOCAL lock_timeout = '5s';
SELECT node_id FROM iot_node WHERE node_id = 'NODE-004' AND commune_id = 'COM-070' FOR UPDATE;
CREATE TEMP TABLE cleanup_candidates ON COMMIT DROP AS
SELECT f.* FROM fault f
WHERE f.commune_id = 'COM-070' AND f.origin_node_id = 'NODE-004'
  AND f.feeder_id = 'FDR-009' AND f.cabinet_id = 'CAB-011'
  AND f.iot_anomaly_code IN ('telemetry_stale','current_drop','voltage_deviation')
  AND f.source_channel = 'iot' AND f.fault_status = 'rejected'
  AND f.recovered_at IS NOT NULL AND f.confirmed_by IS NULL
  AND EXISTS (SELECT 1 FROM audit_event a WHERE a.entity_type = 'fault'
      AND a.entity_id = f.fault_id AND a.commune_id = f.commune_id
      AND a.actor_kind = 'iot' AND a.action = 'rejected')
  AND NOT EXISTS (SELECT 1 FROM audit_event a WHERE a.entity_type = 'fault'
      AND a.entity_id = f.fault_id AND a.commune_id = f.commune_id AND a.actor_kind = 'user'
      AND a.action IN ('confirmed','rejected'))
  AND NOT EXISTS (SELECT 1 FROM work_order_fault l WHERE l.fault_id = f.fault_id);
SELECT f.fault_id FROM fault f JOIN cleanup_candidates c USING (fault_id) FOR UPDATE OF f;
CREATE TEMP TABLE cleanup_plan ON COMMIT DROP AS
SELECT c.fault_id, c.commune_id, c.iot_anomaly_code,
 COALESCE((SELECT f.fault_id FROM fault f WHERE f.commune_id = c.commune_id
    AND f.origin_node_id = c.origin_node_id AND f.feeder_id = c.feeder_id
    AND f.iot_anomaly_code = c.iot_anomaly_code
    AND f.fault_status IN ('detected','confirmed','in_progress','resolved')
    ORDER BY f.detected_at DESC, length(f.fault_id) DESC, f.fault_id DESC LIMIT 1),
  first_value(c.fault_id) OVER (PARTITION BY c.iot_anomaly_code
    ORDER BY c.detected_at DESC, length(c.fault_id) DESC, c.fault_id DESC)) AS canonical_id,
 false AS referenced
FROM cleanup_candidates c;
-- Inspect ALL incoming FK types, including composite references and episode history.
DO $block$
DECLARE fk record; item record; condition text; referenced boolean;
BEGIN
 FOR fk IN SELECT oid, conrelid, conkey, confkey FROM pg_constraint
   WHERE contype = 'f' AND confrelid = 'fault'::regclass LOOP
  SELECT string_agg(format('child.%I = parent.%I', ca.attname, pa.attname), ' AND ')
    INTO condition FROM unnest(fk.conkey, fk.confkey) AS k(child_att, parent_att)
    JOIN pg_attribute ca ON ca.attrelid = fk.conrelid AND ca.attnum = k.child_att
    JOIN pg_attribute pa ON pa.attrelid = 'fault'::regclass AND pa.attnum = k.parent_att;
  FOR item IN SELECT * FROM cleanup_plan WHERE fault_id <> canonical_id LOOP
   EXECUTE format('SELECT EXISTS (SELECT 1 FROM %s child JOIN fault parent ON %s WHERE parent.fault_id = $1)',
       fk.conrelid::regclass, condition) INTO referenced USING item.fault_id;
   IF referenced THEN UPDATE cleanup_plan SET referenced = true WHERE fault_id = item.fault_id; END IF;
  END LOOP;
 END LOOP;
END $block$;
SELECT *, CASE WHEN fault_id = canonical_id THEN 'restore detected'
 WHEN referenced THEN 'retain: FK reference' ELSE 'archive and merge duplicate' END AS proposed_action
FROM cleanup_plan ORDER BY iot_anomaly_code, fault_id;
\if :apply
-- Operator-only backup, outside API models. Original audits remain append-only.
CREATE TABLE IF NOT EXISTS iot_fault_cleanup_archive (
 fault_id text PRIMARY KEY, canonical_id text NOT NULL, original_row jsonb NOT NULL,
 archived_at timestamptz NOT NULL DEFAULT now());
INSERT INTO iot_fault_cleanup_archive (fault_id, canonical_id, original_row)
 SELECT c.fault_id, p.canonical_id, to_jsonb(c) FROM cleanup_candidates c
 JOIN cleanup_plan p USING (fault_id) ON CONFLICT (fault_id) DO NOTHING;
INSERT INTO audit_event (occurred_at, actor_kind, commune_id, entity_type, entity_id, action,
 before_state, after_state, note, correlation_id)
 SELECT now(), 'system', c.commune_id, 'fault', c.fault_id,
 CASE WHEN p.fault_id = p.canonical_id THEN 'reported' ELSE 'merged' END,
 to_jsonb(c), jsonb_build_object('canonical_id', p.canonical_id,
    'fault_status', CASE WHEN p.fault_id = p.canonical_id THEN 'detected' ELSE 'merged' END),
 'Repair legacy automatic rejection; original row archived; manual decisions excluded.',
 'maintenance:IOT-FAULT-RECOVERY-1'
 FROM cleanup_candidates c JOIN cleanup_plan p USING (fault_id)
 WHERE p.fault_id = p.canonical_id OR NOT p.referenced;
UPDATE fault f SET fault_status = 'detected', confirmed_at = NULL, review_note = NULL,
 updated_at = now(), note = concat_ws(E'\n', nullif(f.note,''), f.review_note,
 'Khôi phục lỗi IoT bị hệ thống tự từ chối; chờ Manager kiểm tra.')
 FROM cleanup_plan p WHERE f.fault_id = p.fault_id AND p.fault_id = p.canonical_id;
DELETE FROM fault f USING cleanup_plan p
 WHERE f.fault_id = p.fault_id AND p.fault_id <> p.canonical_id AND NOT p.referenced;
COMMIT;
\else
ROLLBACK;
\echo 'Preview only. No data changed. Backup DB, review candidates, then rerun with -v apply=true.'
\endif
