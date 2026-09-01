-- BE-02 — runs ONCE while the named volume is still empty (docker-entrypoint-initdb.d).
-- Editing this file after the volume has been initialised has NO effect; you must
-- `docker compose down -v` and then `up` again.
--
-- Contract section 5.3 requires geometry(Point,4326) / geometry(LineString,4326) with GIST indexes.
-- Tables and indexes belong to BE-03/BE-09 — this file only enables the extension so later EF Core
-- migrations already have the geometry types available.

CREATE EXTENSION IF NOT EXISTS postgis;

-- postgis_topology is NOT enabled: BE-13 builds road topology at the application level
-- (pole to segment), it does not use the PostGIS topology model.
-- pgvector is NOT enabled: the project dropped RAG entirely (BE-02, per the task-list note).
