﻿using DBADash;
using Newtonsoft.Json;
using Polly;
using Quartz;
using Quartz.Impl;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using static DBADash.DBADashConnection;
using Serilog;
using System.IO;

namespace DBADashService
{

    public class ScheduleService
    {
        private readonly IScheduler scheduler;
        public readonly CollectionConfig config;
        System.Timers.Timer azureScanForNewDBsTimer;
        System.Timers.Timer folderCleanupTimer;

        public  ScheduleService()
        {
            config = SchedulerServiceConfig.Config;

            Int32 threads = config.ServiceThreads;
            if (threads < 1)
            {
                threads = 10;
                Log.Logger.Information("Threads {threadcount} (default)", threads);
            }
            else
            {
                Log.Logger.Information("Threads {threadcount} (user)", threads);
            }
            
            NameValueCollection props = new NameValueCollection
        {
            { "quartz.serializer.type", "binary" },
            { "quartz.scheduler.instanceName", "DBADashScheduler" },
            { "quartz.jobStore.type", "Quartz.Simpl.RAMJobStore, Quartz" },
            { "quartz.threadPool.threadCount", threads.ToString() },
            { "quartz.threadPool.maxConcurrency", threads.ToString() }
            };
            
            StdSchedulerFactory factory = new StdSchedulerFactory(props);
            scheduler = factory.GetScheduler().ConfigureAwait(false).GetAwaiter().GetResult();
        }


        private void removeEventSessions(CollectionConfig config)
        {   
            try
            {
                Parallel.ForEach(config.SourceConnections, cfg => {
                    if (cfg.SourceConnection.Type == ConnectionType.SQL)
                    {
                        try
                        {
                            var collector = new DBCollector(cfg.GetSource(), cfg.NoWMI,config.ServiceName);
                            if (cfg.PersistXESessions)
                            {
                                Log.Logger.Information("Stop DBADash event sessions for {connection}", cfg.SourceConnection.ConnectionForPrint);
                                collector.StopEventSessions();
                            }
                            else
                            {
                                Log.Logger.Information("Remove DBADash event sessions for {connection}", cfg.SourceConnection.ConnectionForPrint);
                                collector.RemoveEventSessions();
                            }
                        }
                        catch(Exception ex)
                        {
                            Log.Logger.Error("Error Stop/Remove DBADash event sessions for {connection}", cfg.SourceConnection.ConnectionForPrint);
                        }

                    }
                });
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error removing event sessions");
            }
        }

        private void upgradeDB()
        {
            foreach (var d in config.AllDestinations.Where(dest => dest.Type == ConnectionType.SQL))
            {
                Log.Logger.Information("Version check for repository database {connection}", d.ConnectionForPrint);
                DBValidations.DBVersionStatus status = null;
                Policy.Handle<Exception>()
                  .WaitAndRetry(new[]
                  {
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(20),
                    TimeSpan.FromSeconds(60)
                  }, (exception, timeSpan, context) =>
                  {
                      Log.Error(exception,"Version check for repository database failed");
                  }).Execute(() => status = DBValidations.VersionStatus(d.ConnectionString));

                if (status.VersionStatus == DBValidations.DBVersionStatusEnum.AppUpgradeRequired)
                {
                    Log.Warning("This version of the app is older than the repository database and should be upgraded. DB {dbversion}.  App {appversion}", status.DACVersion, status.DBVersion);
                }
                else if (status.VersionStatus == DBValidations.DBVersionStatusEnum.CreateDB)
                {
                    if (config.AutoUpdateDatabase)
                    {
                        Log.Information("Create repository database");
                        DBValidations.UpgradeDBAsync(d.ConnectionString).Wait();
                        Log.Information("Repository database created");
                    }
                    else
                    {
                        throw new Exception("Repository database needs to be created.  Use to service configuration tool to deploy the repository database.");
                    }
                }
                else if (status.VersionStatus == DBValidations.DBVersionStatusEnum.UpgradeRequired)
                {
                    if (config.AutoUpdateDatabase)
                    {
                        Log.Information("Upgrade DB from {oldversion} to {newversion}", status.DBVersion.ToString(), status.DACVersion.ToString());
                        DBValidations.UpgradeDBAsync(d.ConnectionString).Wait();
                        status = DBValidations.VersionStatus(d.ConnectionString);
                        if (status.VersionStatus == DBValidations.DBVersionStatusEnum.OK)
                        {
                            Log.Information("Repository DB upgrade completed");
                        }
                        else
                        {
                            throw new Exception(string.Format("Database version is {0} is not expected following upgrade to {1}", status.DBVersion.ToString(), status.DACVersion.ToString()));
                        }
                    }
                    else
                    {
                        throw new Exception("Database upgrade is required.  Enable auto updates or run the service configuration tool to update.");
                    }
                }
                else if (status.VersionStatus == DBValidations.DBVersionStatusEnum.OK)
                {
                    Log.Information("Repository database version check OK {version}", status.DBVersion.ToString());
                }

            }
        }


        public void Start()
        {
            scheduler.Start().ConfigureAwait(false).GetAwaiter().GetResult();
            upgradeDB();
            ScheduleJobs();
        }

 

        public void ScheduleJobs()
        {
            Log.Information("Agent Version {version}", Assembly.GetEntryAssembly().GetName().Version);
            if (config.ScanForAzureDBs)
            {
                config.AddAzureDBs();
            }

            removeEventSessions(config);

            Int32 i = 0;
            foreach(DBADashConnection d in config.AllDestinations.Where(cn => cn.Type== ConnectionType.SQL))
            {
                i += 1;
                string maintenanceCron = config.GetMaintenanceCron();

                IJobDetail job = JobBuilder.Create<MaintenanceJob>()
                        .WithIdentity("MaintenanceJob" + i.ToString())
                        .UsingJobData("ConnectionString", d.ConnectionString)
                        .Build();
                ITrigger trigger = TriggerBuilder.Create()
                .StartNow()
                .WithCronSchedule(maintenanceCron)
                .Build();
             
                scheduler.ScheduleJob(job, trigger).ConfigureAwait(false).GetAwaiter().GetResult();
                scheduler.TriggerJob(job.Key);

            }
            scheduleSourceCollection(config.SourceConnections);

            if (config.ScanForAzureDBsInterval > 0)
            {
                Log.Information("Scan for new Azure DBS every {scaninterval} seconds", config.ScanForAzureDBsInterval);
                azureScanForNewDBsTimer = new System.Timers.Timer
                {
                    Enabled = true,
                    Interval = config.ScanForAzureDBsInterval * 1000
                };
                azureScanForNewDBsTimer.Elapsed += new System.Timers.ElapsedEventHandler(ScanForAzureDBs);
            }
            FolderCleanup();
            folderCleanupTimer = new System.Timers.Timer
            {
                Enabled = true,
                Interval = 14400000 // 4hrs
            };
            folderCleanupTimer.Elapsed += new System.Timers.ElapsedEventHandler(FolderCleanup);
        }

        private void scheduleSourceCollection(List<DBADashSource> sourceConnections)
        {       
            foreach (DBADashSource cfg in sourceConnections)
            {
                Log.Information("Schedule collections for {connection}", cfg.SourceConnection.ConnectionForPrint);
                string cfgString = JsonConvert.SerializeObject(cfg);

                foreach (var s in cfg.GetSchedule())
                {
                    IJobDetail job = JobBuilder.Create<DBADashJob>()
                           .UsingJobData("Type", JsonConvert.SerializeObject(s.CollectionTypes))
                           .UsingJobData("Source", cfg.SourceConnection.ConnectionString)
                           .UsingJobData("CFG", cfgString)
                           .UsingJobData("Job_instance_id",0)
                           .UsingJobData("SourceType", JsonConvert.SerializeObject(cfg.SourceConnection.Type))
                          .Build();
                    ITrigger trigger = TriggerBuilder.Create()
                    .StartNow()
                    .WithCronSchedule(s.CronSchedule)
                    .Build();

                    scheduler.ScheduleJob(job, trigger).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (s.RunOnServiceStart)
                    {
                        scheduler.TriggerJob(job.Key);
                    }

                }
                if (cfg.SchemaSnapshotDBs != null && cfg.SchemaSnapshotDBs.Length > 0)
                {
                    IJobDetail job = JobBuilder.Create<SchemaSnapshotJob>()
                          .UsingJobData("Source", cfg.SourceConnection.ConnectionString)
                          .UsingJobData("CFG", cfgString)
                          .UsingJobData("SchemaSnapshotDBs", cfg.SchemaSnapshotDBs)
                             .Build();
                    ITrigger trigger = TriggerBuilder.Create()
                      .StartNow()
                      .WithCronSchedule(cfg.SchemaSnapshotCron)
                      .Build();


                    scheduler.ScheduleJob(job, trigger).ConfigureAwait(false).GetAwaiter().GetResult();
                    if (cfg.SchemaSnapshotOnServiceStart)
                    {
                        scheduler.TriggerJob(job.Key);
                    }

                }
            }
        }

        private void ScanForAzureDBs()
        {
            Log.Information("Scan for new azure DBs");
            scheduleSourceCollection(config.AddAzureDBs());
        }

        private void ScanForAzureDBs(object sender, ElapsedEventArgs e)
        {
            ScanForAzureDBs();
        }

        public void Stop()
        {
            removeEventSessions(config);
            scheduler.Shutdown().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static void FolderCleanup(object sender, ElapsedEventArgs e)
        {
            FolderCleanup();
        }

        public static void FolderCleanup()
        {
            try
            {
                if (Directory.Exists(SchedulerServiceConfig.FailedMessageFolder))
                {
                    Log.Information("Maintenance: Failed Message Folder cleanup");
                    (from f in new DirectoryInfo(SchedulerServiceConfig.FailedMessageFolder).GetFiles()
                     where f.LastWriteTime < DateTime.Now.Subtract(TimeSpan.FromDays(7))
                     && (f.Extension.ToLower() == ".xml" || f.Extension.ToLower() == ".json" || f.Extension.ToLower() == ".bin")
                     && f.Name.ToLower().StartsWith("dbadash")
                     select f
                    ).ToList()
                        .ForEach(f => f.Delete());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Maintenance: FailedMessageFolderCleanup");
            }
        }
    }
}
