﻿using DBADashService;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using static DBADash.DBADashConnection;
using System.Linq;
using Serilog;

namespace DBADash
{

    public class BasicConfig
    {

        [JsonIgnore]
        public DBADashConnection DestinationConnection { get; set; }

        public BasicConfig()
        {
            DestinationConnection = new DBADashConnection();
        }

        // Note if destination is SQL connection string, password is encrypted.  Use GetDestination() to return with real password
        public string Destination
        {
            get
            {
                return DestinationConnection.EncryptedConnectionString;
            }
            set
            {
                DestinationConnection = new DBADashConnection(value);
            }
        }

        public string Serialize()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Include,

            });
        }

        public static BasicConfig Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<BasicConfig>(json);
        }

    }

    public class CollectionConfig:BasicConfig
    {
        public Int32 ServiceThreads=-1;
        private string _secretKey;
        private bool wasEncryptionPerformed = false;
        private readonly string myString = "g&hAs2&mVOLwE6DqO!I5";
        public SchemaSnapshotDBOptions SchemaSnapshotOptions=null;
        public bool ScanForAzureDBs { get; set; } = true;
        public Int32 ScanForAzureDBsInterval { get; set; } = 3600;
        public string ServiceName { get; set; } = "DBADashService";

        public bool AutoUpdateDatabase { get; set; } = true;
        public bool LogInternalPerformanceCounters = false;
        public CollectionSchedules CollectionSchedules;

        public CollectionSchedules GetSchedules()
        {
            if (CollectionSchedules == null)
            {
                return CollectionSchedules.DefaultSchedules;
            }
            else
            {
                return CollectionSchedules.CombineWithDefault();
            }
        }

        public List<DBADashSource> SourceConnections = new List<DBADashSource>();

        public static new CollectionConfig Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<CollectionConfig>(json);
        }

        public string AWSProfile { get; set; }

        public string AccessKey { get; set; }
        public string SecretKey
        {
            get
            {
                return _secretKey;
            }
            set
            {
                if (value == "")
                {
                    _secretKey = null;
                }
                else if (value == null || value.StartsWith("¬=!"))
                {
                    _secretKey = value;
                }
                else
                {
                    _secretKey = "¬=!" + EncryptText.EncryptString(value, myString);
                    wasEncryptionPerformed = true;
                }
            }
        }

        public string GetSecretKey()
        {

            if (_secretKey != null && _secretKey.StartsWith("¬=!"))
            {
                return EncryptText.DecryptString(_secretKey.Substring(3), myString);
            }
            else
            {
                return _secretKey;
            }

        }

        private readonly string defaultMaintenanceCron = "0 0 0/12 1/1 * ? *";

        public string MaintenanceScheduleCron { get; set; }

        public string GetMaintenanceCron()
        {
            if (MaintenanceScheduleCron == null || MaintenanceScheduleCron == "")
            {
                return defaultMaintenanceCron;
            }
            else
            {
                return MaintenanceScheduleCron;
            }
        }
        [JsonIgnore]
        public List<DBADashConnection> SecondaryDestinationConnections { get; set; } = new List<DBADashConnection>();

        // Required for serialization
        public string[] SecondaryDestinations
        {
            get
            {
                var encryptedStrings = new List<string>();
                foreach(var d in SecondaryDestinationConnections)
                {
                    encryptedStrings.Add(d.EncryptedConnectionString);
                }
                return encryptedStrings.ToArray();
            }
            set
            {
                SecondaryDestinationConnections = new List<DBADashConnection>();
                foreach(string s in value.Distinct().ToArray())
                {
                      SecondaryDestinationConnections.Add(new DBADashConnection(s));
                }
            }
        }

        [JsonIgnore]
        public List<DBADashConnection> AllDestinations
        {
            get
            {
                var all = new List<DBADashConnection>
                {
                    DestinationConnection
                };
                all.AddRange(SecondaryDestinationConnections);
                return all;
            }
        }

        public bool WasEncrypted()
        {
            if (wasEncryptionPerformed)
            {
                return wasEncryptionPerformed;
            }
            if (DestinationConnection.WasEncrypted)
            {
                wasEncryptionPerformed = true;
            }
            else
            {
                foreach (var c in SourceConnections)
                {
                    if (c.SourceConnection.WasEncrypted)
                    {
                        wasEncryptionPerformed = true;
                    }
                }
            }
            return wasEncryptionPerformed;
        }

        public void ValidateDestination()
        {
            ValidateDestination(DestinationConnection);
        }

        public static void ValidateDestination(DBADashConnection destination)
        {
            if (destination.Type == ConnectionType.Invalid)
            {
                throw new Exception("Invalid connection string");
            }
            else if (destination.Type == DBADashConnection.ConnectionType.SQL)
            {
                if (string.IsNullOrEmpty(destination.InitialCatalog()))
                {
                    throw new Exception("Provide a name for the DBADash repository database through the Initial Catalog property of the connection string");
                }
                // VersionStatus check will throw an error if there is an issue connecting to the DB or the DB isn't valid.
                var status = DBValidations.VersionStatus(destination.ConnectionString);
                if(status.VersionStatus == DBValidations.DBVersionStatusEnum.CreateDB)
                {
                    validateDestinationVersion(destination);
                }
                Log.Information("DB Version Status: {status}", status.VersionStatus);
            }
            else
            {
                destination.Validate();
            }
        }

        private static void validateDestinationVersion(DBADashConnection destination)
        {
            var master = destination.MasterConnection();
            var cinfo = master.ConnectionInfo;

            if (cinfo.MajorVersion < 13 && cinfo.EngineEdition != Microsoft.SqlServer.Management.Common.DatabaseEngineEdition.SqlDatabase && cinfo.EngineEdition != Microsoft.SqlServer.Management.Common.DatabaseEngineEdition.SqlManagedInstance) // 13=2016, 12 might be Azure DB which is OK
            {
                throw new Exception("DBA Dash repository database requires SQL 2016 SP1 or later");
            }
            else if (cinfo.MajorVersion == 13 && (new Version(cinfo.ProductVersion)).CompareTo(new Version("13.0.4001.0")) < 0 && cinfo.EngineEdition != Microsoft.SqlServer.Management.Common.DatabaseEngineEdition.Enterprise)
            { // Check if we are running SP1 for SQL 2016
                throw new Exception("DBA Dash repository database requires SQL 2016 SP1 or later.  Please upgrade to SP1 or later.");
            }
        }

       public DBADashSource GetSourceFromConnectionString(string connectionString,bool? isAzure=null)
        {
            var findConnection = new DBADashConnection(connectionString);
            foreach (var s in SourceConnections)
            {
                if (s.SourceConnection.Type == findConnection.Type)
                {
                    if (s.SourceConnection.Type == ConnectionType.SQL && s.SourceConnection.DataSource().ToLower() == findConnection.DataSource().ToLower()) {
                        // normally we can treat as same connection if we just vary by initial catalog.  For AzureDB, a different DB is a different instance
                        if (s.SourceConnection.InitialCatalog().ToLower() == findConnection.InitialCatalog().ToLower()) {
                            return s;
                        }
                        else if ((new string[] { "master", String.Empty }).Contains(s.SourceConnection.InitialCatalog().ToLower())
                            && (new string[] { "master", String.Empty }).Contains(findConnection.InitialCatalog().ToLower()))
                        {
                            return s;
                        }
                        else
                        {
                            if(isAzure == null)
                            {
                                isAzure = findConnection.IsAzureDB();
                            }
                            if (isAzure==false)
                            {
                                return s;
                            }
                        }
                    }
                    else if (s.SourceConnection.ConnectionString == findConnection.ConnectionString)
                    {
                        return s;
                    }
                }
            }
            return null;
        }

        public bool SourceExists(string connectionString,bool? isAzure=null)
        {
            if (GetSourceFromConnectionString(connectionString,isAzure) != null)
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        public void AddConnections(List<DBADashSource> connections)
        {
            SourceConnections.AddRange(connections);
        }

        public List<DBADashSource> AddAzureDBs()
        {
            var newConnections = GetNewAzureDBConnections();
            lock (SourceConnections)
            {
                SourceConnections.AddRange(newConnections);
            }
            return newConnections;
        }

        public List<DBADashSource> AddAzureDBs(DBADashSource masterConnection)
        {
            var newConnections = GetNewAzureDBConnections(masterConnection);
            SourceConnections.AddRange(newConnections);
            return newConnections;
        }

  
        public List<DBADashSource> GetNewAzureDBConnections()
        {
            var newConnections = new List<DBADashSource>();
            foreach (var cfg in SourceConnections.Where(src=>src.SourceConnection.Type == ConnectionType.SQL))
            {
                try
                {
                    if ((new string[] {"master", String.Empty}).Contains(cfg.SourceConnection.InitialCatalog()) && cfg.SourceConnection.ConnectionInfo.IsAzureMasterDB)
                    {             
                        newConnections.AddRange(GetNewAzureDBConnections(cfg));
                    }
 
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error getting Azure DB connections from master {connection}", cfg.SourceConnection.ConnectionForPrint);
                }
            }
            return newConnections;
        }

        public List<DBADashSource> GetNewAzureDBConnections(DBADashSource masterConnection)
        {
            var newConnections = new List<DBADashSource>();
            SqlConnection cn = new SqlConnection(masterConnection.SourceConnection.ConnectionString);
            using (cn)
            {
                cn.Open();
                SqlCommand cmd = new SqlCommand("SELECT name from sys.databases WHERE name <> 'master'", cn);
                var rdr = cmd.ExecuteReader();
                var builder = new SqlConnectionStringBuilder(masterConnection.SourceConnection.ConnectionString);
                
                while (rdr.Read())
                {
                    builder.InitialCatalog = rdr.GetString(0);
                    DBADashSource dbCn = masterConnection.DeepCopy();
                    dbCn.SourceConnection.ConnectionString = builder.ConnectionString;
                    dbCn.ConnectionID = string.Empty;

                    if (!SourceExists(dbCn.SourceConnection.ConnectionString,true))
                    {
                        newConnections.Add(dbCn);
                        Log.Information("Add Azure DB connection {DataSource}|{DB}", builder.DataSource, builder.InitialCatalog);
                    }
                }
            }
            return newConnections;
        }


    }
}