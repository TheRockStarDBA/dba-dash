﻿CREATE PROC dbo.SystemTags_Upd(
	@InstanceID INT
)
AS
DECLARE @Tags TABLE(
	TagID INT NULL,
	TagName NVARCHAR(50) NOT NULL,
	TagValue NVARCHAR(128) NOT NULL
);
DECLARE @Instance SYSNAME
DECLARE @IsAzure BIT
SELECT @Instance = Instance, @IsAzure=CASE WHEN EditionID = 1674378470 THEN CAST(1 AS BIT) ELSE CAST(0 AS BIT) END
FROM dbo.Instances
WHERE InstanceID = @InstanceID;

WITH T AS (
	SELECT CAST(v.SQLVersionName AS NVARCHAR(128)) AS [Version], 
			CAST(I.Edition AS NVARCHAR(128)) AS Edition,
			CAST(v.SQLVersionName + ' ' + I.ProductLevel + ISNULL(' ' + I.ProductUpdateLevel,'') AS NVARCHAR(128)) AS PatchLevel,
			CAST(I.Collation AS NVARCHAR(128)) Collation,
			CAST(I.SystemManufacturer AS NVARCHAR(128)) AS SystemManufacturer,
			CAST(I.SystemProductName AS NVARCHAR(128)) AS SystemProductName,
			CASE WHEN @IsAzure=1 THEN '    -' ELSE CAST(RIGHT(REPLICATE(' ',5) +  CAST(I.cpu_count as NVARCHAR(50)),5) AS NVARCHAR(128)) END AS CPUCount,
			CAST(I.AgentHostName AS NVARCHAR(128)) DBADashAgent,
			CAST(I.AgentVersion AS NVARCHAR(128)) AS DBADashAgentVersion
	FROM dbo.Instances I
	CROSS APPLY dbo.SQLVersionName(I.EditionID,I.ProductVersion) v
	WHERE I.InstanceID=@InstanceID
)
INSERT INTO @Tags
(
    TagName,
    TagValue
)
SELECT 	'{' + TagName + '}' as TagName,
		ISNULL(TagValue,'')
FROM T
UNPIVOT(TagValue FOR TagName IN(PatchLevel, [Version], Edition, Collation, SystemManufacturer, SystemProductName, CPUCount, DBADashAgent,DBADashAgentVersion)) upvt

INSERT INTO dbo.Tags
(
    TagName,
    TagValue
)
SELECT T.TagName,T.TagValue
FROM @Tags T
WHERE NOT EXISTS(SELECT 1 
			FROM dbo.Tags TG
			WHERE TG.TagName = t.TagName 
			AND TG.TagValue = t.TagValue
			)

UPDATE T 
	SET T.TagID = TG.TagID
FROM @Tags T 
JOIN dbo.Tags TG ON T.TagName = TG.TagName AND T.TagValue = TG.TagValue

DELETE IT 
FROM dbo.InstanceTags IT
JOIN dbo.Tags T ON IT.TagID = T.TagID 
WHERE IT.Instance = @Instance
AND T.TagName LIKE '{%'
AND NOT EXISTS(SELECT 1 
			FROM @Tags tmp
			WHERE T.TagID = tmp.TagID
			)

INSERT INTO dbo.InstanceTags
(
    Instance,
    TagID
)
SELECT @Instance,T.TagID
FROM @Tags T 
WHERE NOT EXISTS(SELECT 1 
			FROM dbo.InstanceTags IT 
			WHERE IT.Instance = @Instance 
			AND IT.TagID = T.TagID)


DELETE T 
FROM dbo.Tags T
WHERE NOT EXISTS(SELECT 1 
				FROM dbo.InstanceTags IT 
				WHERE IT.TagID=T.TagID
				)