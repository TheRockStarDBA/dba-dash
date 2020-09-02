﻿CREATE  PROC [dbo].[ObjectExecutionStats_Get](
	@Instance SYSNAME=NULL,
	@DatabaseID INT=NULL,
	@ObjectName SYSNAME=NULL,
	@SchemaName SYSNAME=NULL,
	@FromDateUTC DATETIME=NULL,
	@ToDateUTC DATETIME=NULL,
	@Measure VARCHAR(30)='TotalDuration',
	@DateAgg VARCHAR(20)='10MIN',
	@UTCOffset INT=0,
	@InstanceID INT=NULL,
	@ObjectID BIGINT=NULL
)
WITH EXECUTE AS OWNER
AS
IF @FromDateUTC IS NULL
	SET @FromDateUTC = CONVERT(DATETIME,STUFF(CONVERT(VARCHAR,DATEADD(mi,-120,GETUTCDATE()),120),16,4,'0:00'),120) 
IF @ToDateUTC IS NULL
	SET @ToDateUTC = GETUTCDATE()

DECLARE @DateAggString NVARCHAR(MAX)
DECLARE @MeasureString NVARCHAR(MAX) 
SELECT @MeasureString = CASE WHEN @Measure IN('TotalCPU','AvgCPU','TotalDuration','AvgDuration','ExecutionCount','ExecutionsPerMin','AvgLogicalReads','AvgPhysicalReads','AvgWrites','TotalWrites','TotalLogicalReads','TotalPhysicalReads') THEN @Measure ELSE NULL END
SELECT @DateAggString = CASE @DateAgg WHEN 'NONE' THEN 'DATEADD(mi, @UTCOffset, PS.SnapshotDate)'
		WHEN '1MIN' THEN 'DATEADD(mi, DATEDIFF(mi, 0, DATEADD(s, 30, DATEADD(mi, @UTCOffset, PS.SnapshotDate))), 0)'
		WHEN '10MIN' THEN 'CONVERT(DATETIME,STUFF(CONVERT(VARCHAR,DATEADD(mi, @UTCOffset, PS.SnapshotDate),120),16,4,''0:00''),120)'
		WHEN '60MIN' THEN 'DATEADD(mi, @UTCOffset, PS.SnapshotDate) '
		WHEN '120MIN' THEN 'DATEADD(hh,DATEPART(hh,PS.SnapshotDate) - DATEPART(hh,PS.SnapshotDate) % 2, CAST(CAST(PS.SnapshotDate AS DATE) AS DATETIME))'
		WHEN 'DAY' THEN 'CAST(DATEADD(mi, @UTCOffset, PS.SnapshotDate) AS DATE)' ELSE NULL END
DECLARE @SQL NVARCHAR(MAX)
SET @SQL = N'
WITH agg AS (
SELECT ' + @DateAggString + N' as SnapshotDate,
       D.name AS DatabaseName,
	   D.DatabaseID,
	   O.ObjectID,
       O.SchemaName + ''.'' + O.objectname as object_name,
	   SUM(PS.total_worker_time)/1000000.0 as TotalCPU,
	   SUM(PS.total_worker_time)/NULLIF(SUM(PS.execution_count),0)/1000000.0 as AvgCPU,
	   SUM(PS.execution_count) as ExecutionCount,
	   SUM(PS.execution_count)/(NULLIF(SUM(PeriodTime),0)/60000000.0) as ExecutionsPerMin,
	   SUM(PS.total_elapsed_time)/1000000.0 AS TotalDuration,
	   SUM(PS.total_elapsed_time)/NULLIF(SUM(PS.execution_count),0)/1000000.0 AS AvgDuration,
	   SUM(PS.total_logical_reads) as TotalLogicalReads,
	   SUM(PS.total_logical_reads)/NULLIF(SUM(PS.execution_count),0) as AvgLogicalReads,
	   SUM(PS.total_physical_reads) as TotalPhysicalReads,
	   SUM(PS.total_physical_reads)/NULLIF(SUM(PS.execution_count),0) as AvgPhysicalReads,
	   SUM(PS.total_logical_writes) as TotalWrites,
	   SUM(PS.total_logical_writes)/NULLIF(SUM(PS.execution_count),0) as AvgWrites
FROM dbo.ObjectExecutionStats' + CASE WHEN @DateAgg IN('60MIN','DAY') THEN N'_60MIN' ELSE N'' END + N' PS
    JOIN dbo.DBObjects O ON PS.ObjectID = O.ObjectID
    JOIN dbo.Databases D ON D.DatabaseID = O.DatabaseID
	JOIN dbo.Instances I ON D.InstanceID = I.InstanceID AND PS.InstanceID = I.InstanceID
WHERE D.IsActive=1
' + CASE WHEN @Instance IS NOT NULL THEN N'AND I.Instance = @Instance' ELSE '' END + N'
' + CASE WHEN @InstanceID IS NOT NULL THEN N'AND I.InstanceID = @InstanceID' ELSE '' END + N'
AND PS.SnapshotDate >= @FromDate 
AND PS.SnapshotDate< @ToDate
' + CASE WHEN @DatabaseID IS NULL THEN N'' ELSE N'AND D.DatabaseID=@DatabaseID' END + N'
' + CASE WHEN @ObjectName IS NULL THEN N'' ELSE N'AND O.objectname=@ObjectName' END + N'
' + CASE WHEN @ObjectID IS NULL THEN N'' ELSE N'AND PS.ObjectID = @ObjectID' END + N'
GROUP BY ' + @DateAggString + N',D.Name,O.objectname,D.DatabaseID,O.SchemaName,O.ObjectID
)
, T AS (
	SELECT agg.*,
		' + @MeasureString + N' as Measure,
		ROW_NUMBER() OVER (PARTITION BY SnapshotDate ORDER BY ' + @MeasureString + N' DESC) ProcRank,
		SUM(' + @MeasureString + N') OVER(PARTITION BY ObjectID) TotalMeasure
	FROM agg
)
SELECT T.*
FROM T
WHERE ProcRank <=20
ORDER BY TotalMeasure DESC,ObjectID'
PRINT @SQL
IF @SQL IS NOT NULL
BEGIN
EXEC sp_executesql @SQL,N'@Instance SYSNAME,@DatabaseID INT,@FromDate DATETIME,@ToDate DATETIME,@ObjectName SYSNAME,@SchemaName SYSNAME,@UTCOffset INT,@InstanceID INT,@ObjectID BIGINT',
	@Instance,@DatabaseID,@FromDateUTC,@ToDateUTC,@ObjectName,@SchemaName,@UTCOffset,@InstanceID,@ObjectID
END 
ELSE
BEGIN
DECLARE  @results TABLE( [SnapshotDate] DATETIME, [DatabaseName] NVARCHAR(128),[DatabaseID] INT, [object_name] NVARCHAR(128), [TotalCPU] DECIMAL(29,9), [AvgCPU] DECIMAL(29,9), [ExecutionCount] BIGINT, [ExecutionsPerMin] DECIMAL(38,9), [TotalDuration] DECIMAL(29,9), [AvgDuration] DECIMAL(29,9), [TotalLogicalReads] BIGINT, [AvgLogicalReads] BIGINT, [TotalPhysicalReads] BIGINT, [AvgPhysicalReads] BIGINT, [TotalWrites] BIGINT, [AvgWrites] BIGINT, [Measure] DECIMAL(29,9), [ProcRank] BIGINT )
SELECT * FROM @Results
END