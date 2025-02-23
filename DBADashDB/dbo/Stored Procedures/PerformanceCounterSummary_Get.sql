﻿CREATE PROC dbo.PerformanceCounterSummary_Get(
	@InstanceIDs VARCHAR(MAX)=NULL,
	@TagIDs VARCHAR(MAX)=NULL,
	@Counters VARCHAR(MAX)=NULL,
	@InstanceID INT=NULL,
	@FromDate DATETIME2(2),
	@ToDate DATETIME2(2),
	@Search NVARCHAR(128)=NULL,
	@Use60Min BIT=NULL,
	@Debug BIT=0
)
AS
IF @Use60Min IS NULL
BEGIN
	SELECT @Use60Min = CASE WHEN DATEDIFF(hh,@FromDate,@ToDate)>24 THEN 1
						WHEN DATEPART(mi,@FromDate)+DATEPART(s,@FromDate)+DATEPART(ms,@FromDate)=0 
							AND (DATEPART(mi,@ToDate)+DATEPART(s,@ToDate)+DATEPART(ms,@ToDate)=0 
									OR @ToDate>=DATEADD(s,-2,GETUTCDATE())
								)
						THEN 1
						ELSE 0 END
END
DECLARE @SQL NVARCHAR(MAX) =N'
WITH T AS (
	SELECT IC.InstanceID,
			C.CounterID,
			C.object_name,
			C.counter_name,
			C.instance_name,
			' + CASE WHEN @Use60Min=1 THEN 'MAX(PC.Value_Max) AS MaxValue,
			MIN(PC.Value_Min) AS MinValue,
			SUM(PC.Value_Total)/SUM(PC.SampleCount*1.0) AS AvgValue,
			SUM(PC.Value_Total) AS TotalValue,
			SUM(SampleCount) as SampleCount,' 
			ELSE '
			MAX(PC.Value) AS MaxValue,
			MIN(PC.Value) AS MinValue,
			AVG(PC.Value) AS AvgValue,
			SUM(PC.Value) as TotalValue,
			COUNT(*) as SampleCount,' END + '
			(SELECT TOP(1) Value FROM dbo.PerformanceCounters LV WHERE LV.InstanceID = IC.InstanceID AND LV.CounterID = C.CounterID ORDER BY LV.SnapshotDate DESC) AS CurrentValue,
			COALESCE(IC.CriticalFrom,C.CriticalFrom,C.SystemCriticalFrom) AS CriticalFrom,
			COALESCE(IC.CriticalTo,C.CriticalTo,C.SystemCriticalTo) AS CriticalTo,
			COALESCE(IC.WarningFrom,C.WarningFrom,C.SystemWarningFrom) AS WarningFrom,
			COALESCE(IC.WarningTo,C.WarningTo,C.SystemWarningTo) AS WarningTo,
			COALESCE(IC.GoodFrom,C.GoodFrom,C.SystemGoodFrom) AS GoodFrom,
			COALESCE(IC.GoodTo,C.GoodTo,C.SystemGoodTo) AS GoodTo
	FROM dbo.InstanceCounters IC
	JOIN dbo.Counters C ON C.CounterID = IC.CounterID
	JOIN dbo.PerformanceCounters' + CASE WHEN @Use60Min=1 THEN '_60MIN' ELSE '' END + ' PC ON PC.InstanceID = IC.InstanceID AND PC.CounterID = IC.CounterID
	WHERE PC.SnapshotDate>=@FromDate
	AND PC.SnapshotDate<@ToDate
	' + CASE WHEN @InstanceID IS NULL THEN '' ELSE 'AND IC.InstanceID = @InstanceID' END + '
	' + CASE WHEN @InstanceIDs IS NULL THEN '' ELSE 'AND EXISTS(SELECT * FROM STRING_SPLIT(@InstanceIDs,'','') ss WHERE IC.InstanceID = ss.Value)' END + '
	' + CASE WHEN @Counters IS NULL THEN '' ELSE 'AND EXISTS(SELECT * FROM STRING_SPLIT(@Counters,'','') ss WHERE IC.CounterID = ss.Value)' END + '
	' + CASE WHEN @TagIDs IS NULL THEN '' ELSE 'AND EXISTS(SELECT 1 FROM dbo.InstancesMatchingTags(@TagIDs) tg WHERE tg.InstanceID = IC.InstanceID)' END + '
	' + CASE WHEN @Search IS NULL THEN '' ELSE 'AND (C.object_name LIKE @Search
		OR C.instance_name LIKE @Search
		OR C.counter_name LIKE @Search
		)' END + '
GROUP BY	C.CounterID,
			C.object_name,
			C.counter_name,
			C.instance_name,
			IC.InstanceID,
			C.CriticalFrom,
			C.CriticalTo,
			C.WarningFrom,
			C.WarningTo,
			C.GoodFrom,
			C.GoodTo,
			IC.CriticalFrom,
			IC.CriticalTo,
			IC.WarningFrom,
			IC.WarningTo,
			IC.GoodFrom,
			IC.GoodTo,
			C.SystemCriticalFrom,
			C.SystemCriticalTo,
			C.SystemWarningFrom,
			C.SystemWarningTo,
			C.SystemGoodFrom,
			C.SystemGoodTo
)
SELECT *,
		CASE	WHEN MinValueStatus =1 OR MaxValueStatus =1 OR AvgValueStatus=1 OR CurrentValueStatus=1 THEN 1
				WHEN MinValueStatus =2 OR MaxValueStatus =2 OR AvgValueStatus=2 OR CurrentValueStatus=2 THEN 2
				WHEN MinValueStatus =4 OR MaxValueStatus =4 OR AvgValueStatus=4 OR CurrentValueStatus=4 THEN 4
				ELSE 3 END AS Status,
		CASE	WHEN MinValueStatus =1 OR MaxValueStatus =1 OR AvgValueStatus=1 OR CurrentValueStatus=1 THEN 1
				WHEN MinValueStatus =2 OR MaxValueStatus =2 OR AvgValueStatus=2 OR CurrentValueStatus=2 THEN 2
				WHEN MinValueStatus =4 OR MaxValueStatus =4 OR AvgValueStatus=4 OR CurrentValueStatus=4 THEN 4
				WHEN CriticalFrom IS NULL AND CriticalTo IS NULL
					AND WarningFrom IS NULL AND WarningTo IS NULL
					AND GoodFrom IS NULL AND GoodTo IS NULL THEN 6
				ELSE 5 END AS StatusSort
FROM T
OUTER APPLY (SELECT	CASE	WHEN T.MinValue >= T.CriticalFrom AND T.MinValue <= T.CriticalTo THEN 1 
							WHEN T.MinValue >= T.WarningFrom AND T.MinValue <= T.WarningTo THEN 2
							WHEN T.MinValue >= T.GoodFrom AND T.MinValue <= T.GoodTo THEN 4
							ELSE 3 END AS MinValueStatus,
					CASE	WHEN T.MaxValue >= T.CriticalFrom AND T.MaxValue <= T.CriticalTo THEN 1
							WHEN T.MaxValue >= T.WarningFrom AND T.MaxValue <= T.WarningTo THEN 2
							WHEN T.MaxValue >= T.GoodFrom AND T.MaxValue <= T.GoodTo THEN 4
							ELSE 3 END AS MaxValueStatus,
					CASE	WHEN T.AvgValue >= T.CriticalFrom AND T.AvgValue <= T.CriticalTo THEN 1
							WHEN T.AvgValue >= T.WarningFrom AND T.AvgValue <= T.WarningTo THEN 2
							WHEN T.AvgValue >= T.GoodFrom AND T.AvgValue <= T.GoodTo THEN 4
							ELSE 3 END AS AvgValueStatus,
					CASE	WHEN T.CurrentValue >= T.CriticalFrom AND T.CurrentValue <= T.CriticalTo THEN 1
							WHEN T.CurrentValue >= T.WarningFrom AND T.CurrentValue <= T.WarningTo THEN 2
							WHEN T.CurrentValue >= T.GoodFrom AND T.CurrentValue <= T.GoodTo THEN 4
							ELSE 3 END AS CurrentValueStatus
							) AS Calc
ORDER BY StatusSort,
		T.InstanceID,
		T.object_name,
		T.counter_name,
		T.instance_name;'


IF @Debug=1
BEGIN
	PRINT @SQL
END

EXEC sp_executesql @SQL,N'@InstanceID INT,@FromDate DATETIME2(2),@ToDate DATETIME2(2),@Search NVARCHAR(128)=NULL,@InstanceIDs VARCHAR(MAX),@Counters VARCHAR(MAX),@TagIDs VARCHAR(MAX)',@InstanceID, @FromDate, @ToDate, @Search,@InstanceIDs,@Counters,@TagIDs