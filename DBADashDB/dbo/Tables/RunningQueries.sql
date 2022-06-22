﻿CREATE TABLE dbo.RunningQueries(
    InstanceID INT NOT NULL,
    SnapshotDateUTC DATETIME2(7) NOT NULL,
    session_id SMALLINT NOT NULL,
    statement_start_offset INT NULL,
    statement_end_offset INT NULL,
    command NVARCHAR(32) NULL,
    status NVARCHAR(30) NULL,
    wait_time INT  NULL,
    wait_type NVARCHAR(60) NULL,
    wait_resource NVARCHAR(256) NULL,
    blocking_session_id SMALLINT NOT NULL,
    cpu_time INT NULL,
    logical_reads BIGINT NULL,
    reads BIGINT NULL,
    writes BIGINT NULL,
    granted_query_memory INT NULL,
    percent_complete REAL NULL,
    open_transaction_count INT NULL,
    transaction_isolation_level SMALLINT NULL,
    login_name NVARCHAR(128) NOT NULL,
    host_name NVARCHAR(128) NULL,
    database_id SMALLINT NULL,
    program_name NVARCHAR(128) NULL,
    client_interface_name NVARCHAR(32) NULL,
    start_time_utc DATETIME NULL,
    last_request_start_time_utc DATETIME NOT NULL,
    sql_handle VARBINARY(64) NULL,
    plan_handle VARBINARY(64) NULL,
    query_hash BINARY(8) NULL,
    query_plan_hash BINARY(8) NULL,
    login_time_utc DATETIME NULL,
    CONSTRAINT PK_RunningQueries PRIMARY KEY(InstanceID,SnapshotDateUTC,session_id) WITH (DATA_COMPRESSION = PAGE) ON PS_RunningQueries(SnapshotDateUTC)
) ON PS_RunningQueries(SnapshotDateUTC);
GO
CREATE NONCLUSTERED INDEX IX_RunningQueries_sql_handle ON dbo.RunningQueries(sql_handle)
GO
CREATE NONCLUSTERED INDEX IX_RunningQueries_query_plan_hash ON dbo.RunningQueries(query_plan_hash,plan_handle,statement_start_offset,statement_end_offset)