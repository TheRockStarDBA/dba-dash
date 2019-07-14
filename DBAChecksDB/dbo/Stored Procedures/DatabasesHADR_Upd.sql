﻿CREATE PROC DatabasesHADR_Upd(@DBs DatabasesHADR READONLY,@InstanceID INT,@SnapshotDate DATETIME)
AS
SET XACT_ABORT ON;
BEGIN TRAN;
DELETE hadr
FROM dbo.DatabasesHADR hadr
WHERE EXISTS
(
    SELECT 1
    FROM dbo.Databases D
    WHERE D.DatabaseID = hadr.DatabaseID
    AND   D.InstanceID = @InstanceID
);
INSERT INTO dbo.DatabasesHADR
(
    DatabaseID,
    group_database_id,
    is_primary_replica,
    synchronization_state,
    synchronization_health,
    is_suspended,
    suspend_reason
)
SELECT d.DatabaseID,
       hadr.group_database_id,
       hadr.is_primary_replica,
       hadr.synchronization_state,
       hadr.synchronization_health,
       hadr.is_suspended,
       hadr.suspend_reason
FROM @DBs hadr
    JOIN dbo.Databases d ON hadr.database_id = d.database_id
WHERE d.InstanceID = @InstanceID;
COMMIT;