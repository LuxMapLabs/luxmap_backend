-- BE-02 — chạy MỘT LẦN khi named volume còn rỗng (docker-entrypoint-initdb.d).
-- Sửa file này sau khi volume đã khởi tạo sẽ KHÔNG có tác dụng; phải
-- `docker compose down -v` rồi `up` lại.
--
-- Contract mục 5.3 yêu cầu geometry(Point,4326) / geometry(LineString,4326)
-- kèm GIST index. Bảng và index là việc của BE-03/BE-09 — ở đây chỉ bật extension
-- để EF Core migration sau này có sẵn kiểu geometry mà dùng.

CREATE EXTENSION IF NOT EXISTS postgis;

-- postgis_topology KHÔNG bật: BE-13 dựng topology tuyến đường ở tầng ứng dụng
-- (pole ↔ segment), không dùng mô hình topology của PostGIS.
-- pgvector KHÔNG bật: dự án đã bỏ toàn bộ phần RAG (BE-02, ghi chú task list).
