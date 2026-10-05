-- BE-33a (D-7): remove the 14 test accounts BE-12a/BE-12b left on a shared dev database on 21/09/2026.
--
-- They are system_admin rows WITHOUT system-wide scope, which the BE-33a CHECK
-- ck_app_user_system_wide_scope_matches_role forbids, so migration AdminCreatedAccounts fails on a
-- database that still holds them. Run this BEFORE `dotnet ef database update` on such a database:
--
--   docker exec -i luxmap_postgres psql -U luxmap -d luxmap_dev -v ON_ERROR_STOP=1 < scripts/cleanup_be12_leftover_accounts.sql
--
-- Safe to run twice: the second run finds nothing and changes nothing. It stops without deleting if
-- any business row still points at one of these accounts (every such FK is RESTRICT); only their
-- commune assignments and refresh tokens go with them (both cascade).

BEGIN;

DO $$
DECLARE
    leftover text[];
    referenced bigint;
    deleted int;
BEGIN
    SELECT array_agg(user_id) INTO leftover
    FROM app_user
    WHERE username ~ '^be12[ab]-[0-9a-f]{14}$'
      AND role = 'system_admin'
      AND NOT has_system_wide_scope;

    IF leftover IS NULL THEN
        RAISE NOTICE 'No BE-12a/BE-12b leftover accounts: nothing to do.';
        RETURN;
    END IF;

    SELECT (SELECT count(*) FROM audit_event        WHERE actor_user_id = ANY (leftover))
         + (SELECT count(*) FROM fault              WHERE reported_by = ANY (leftover)
                                                       OR confirmed_by = ANY (leftover)
                                                       OR resolved_by = ANY (leftover))
         + (SELECT count(*) FROM luminance_baseline WHERE created_by = ANY (leftover))
         + (SELECT count(*) FROM luminance_history  WHERE published_by = ANY (leftover))
         + (SELECT count(*) FROM lux_reading        WHERE measured_by = ANY (leftover))
         + (SELECT count(*) FROM repair_evidence    WHERE uploaded_by = ANY (leftover))
         + (SELECT count(*) FROM survey_sweep       WHERE captured_by = ANY (leftover)
                                                       OR reviewed_by = ANY (leftover))
         + (SELECT count(*) FROM work_order         WHERE assigned_to = ANY (leftover)
                                                       OR created_by = ANY (leftover))
      INTO referenced;

    IF referenced > 0 THEN
        RAISE EXCEPTION '% business rows still reference the leftover accounts; nothing deleted.', referenced;
    END IF;

    DELETE FROM app_user WHERE user_id = ANY (leftover);
    GET DIAGNOSTICS deleted = ROW_COUNT;

    IF deleted <> 14 THEN
        RAISE EXCEPTION 'Expected the 14 accounts of 21/09/2026, found %; nothing deleted.', deleted;
    END IF;

    RAISE NOTICE 'Deleted % leftover accounts.', deleted;
END
$$;

COMMIT;
