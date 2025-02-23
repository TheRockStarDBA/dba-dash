﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Data.SqlClient;
using static DBADashGUI.Main;
using System.IO;
using System.Diagnostics;
using DBADashGUI.Checks;
using static DBADashGUI.DBADashStatus;

namespace DBADashGUI
{
    public partial class Summary : UserControl
    {

        public List<Int32> InstanceIDs;
        private List<Int32> refreshInstanceIDs;

        DateTime lastRefresh;

        public Summary()
        {
            InitializeComponent();
        }

        DataView dv;

        Dictionary<string, bool> statusColumns;

        private bool focusedView = false;


        private void resetStatusCols()
        {
           statusColumns= new Dictionary<string, bool> { { "FullBackupStatus", false }, { "LogShippingStatus", false }, { "DiffBackupStatus", false }, { "LogBackupStatus", false }, { "DriveStatus", false },
                                                            { "JobStatus", false }, { "CollectionErrorStatus", false }, { "AGStatus", false }, {"LastGoodCheckDBStatus",false}, {"SnapshotAgeStatus",false },
                                                            {"MemoryDumpStatus",false }, {"UptimeStatus",false }, {"CorruptionStatus",false }, {"AlertStatus",false }, {"FileFreeSpaceStatus",false },
                                                            {"CustomCheckStatus",false }, {"MirroringStatus",false },{"ElasticPoolStorageStatus",false},{"PctMaxSizeStatus",false}, {"QueryStoreStatus",false },
                                                            {"LogFreeSpaceStatus",false },{"DBMailStatus",false } };
        }


        private Task<DataTable> getSummaryAsync()
        {
            return Task<DataTable>.Factory.StartNew(() =>
            {
                using (var cn = new SqlConnection(Common.ConnectionString))
                using (var cmd = new SqlCommand("dbo.Summary_Get", cn) { CommandType = CommandType.StoredProcedure })
                {
                    cn.Open();

                    cmd.Parameters.AddWithValue("InstanceIDs", string.Join(",", InstanceIDs));

                    SqlDataAdapter da = new SqlDataAdapter(cmd);
                    DataTable dt = new DataTable();
                    da.Fill(dt);
                    return dt;
                }
            });
        }

        public void RefreshDataIfStale()
        {
            if (DateTime.Now.Subtract(lastRefresh).TotalMinutes > 5 || instanceIDsChanged())
            {
                RefreshData();
            }
            else
            {
                dgvSummary.Columns[0].Frozen = Common.FreezeKeyColumn;
            }
        }

        private bool instanceIDsChanged()
        {
            return !(InstanceIDs.Count == refreshInstanceIDs.Count() && refreshInstanceIDs.All(InstanceIDs.Contains));
        }
        
        public void RefreshData()
        {
            dgvSummary.Columns[0].Frozen = Common.FreezeKeyColumn;
            resetStatusCols();
            refresh1.Visible = true;
            dgvSummary.Visible = false;
            refreshInstanceIDs = new List<int>(InstanceIDs);
            getSummaryAsync().ContinueWith(task =>
            {
                DataTable dt = task.Result;
                dgvSummary.AutoGenerateColumns = false;

                var cols = (statusColumns.Keys).ToList<string>();
                dt.Columns.Add("IsFocusedRow", typeof(bool));
                foreach (DataRow row in dt.Rows)
                {
                    bool isFocusedRow = false;
                    foreach (string col in cols)
                    {
                        var status = (DBADashStatus.DBADashStatusEnum)Convert.ToInt32(row[col] == DBNull.Value ? 3 : row[col]);
                        if (!(status == DBADashStatus.DBADashStatusEnum.NA || (status == DBADashStatus.DBADashStatusEnum.OK && focusedView)))
                        {
                            statusColumns[col] = true;
                            isFocusedRow = true;
                        }
                    }

                    if (row["IsAgentRunning"] != DBNull.Value && (bool)row["IsAgentRunning"] == false)
                    {
                        isFocusedRow = true;
                        statusColumns["JobStatus"] = true;
                    }
                    row["IsFocusedRow"] = isFocusedRow;
                }
                // hide columns that all have status N/A
                foreach (var col in statusColumns)
                {
                    dgvSummary.Invoke((Action)(() => dgvSummary.Columns[col.Key].Visible = col.Value));
                }
                string rowFilter = "";
                if (focusedView)
                {
                    rowFilter = "IsFocusedRow=1";
                }
                dv = new DataView(dt, rowFilter, "Instance", DataViewRowState.CurrentRows);
                dgvSummary.Invoke((Action)(() =>
                {
                    dgvSummary.DataSource = dv;
                    dgvSummary.AutoResizeColumns(DataGridViewAutoSizeColumnsMode.DisplayedCells);
                }
                ));
                lastRefresh = DateTime.Now;
                toolStrip1.Invoke((Action)(() =>
                {
                    lblRefreshTime.Text = "Refresh Time: " + lastRefresh.ToString();
                    lblRefreshTime.ForeColor = DBADashStatusEnum.OK.GetColor();
                }
                ));
                refresh1.Invoke((Action)(() => refresh1.Visible = false));
                dgvSummary.Invoke((Action)(() => dgvSummary.Visible = true));
                timer1.Enabled = true;
                
            });
                        
        }

        private void dgvSummary_RowAdded(object sender, DataGridViewRowsAddedEventArgs e)
        {
     
            for (Int32 idx = e.RowIndex; idx < e.RowIndex + e.RowCount; idx += 1)
            {
                var row = (DataRowView)dgvSummary.Rows[idx].DataBoundItem;
                bool isAzure = row["InstanceID"] == DBNull.Value;
                var cols = (statusColumns.Keys).ToList<string>();
                foreach (var col in cols)
                {
                    var status = (DBADashStatus.DBADashStatusEnum)Convert.ToInt32(row[col] == DBNull.Value ? 3 : row[col]);
                    dgvSummary.Rows[idx].Cells[col].SetStatusColor(status);                    
                }
                string DBMailStatus = Convert.ToString(row["DBMailStatusDescription"]);
                dgvSummary.Rows[idx].Cells["DBMailStatus"].ToolTipText = DBMailStatus;

                dgvSummary.Rows[idx].Cells["FullBackupStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["DiffBackupStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["LogBackupStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["DriveStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["JobStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["LogShippingStatus"].Value = isAzure ? "" : "View";
                dgvSummary.Rows[idx].Cells["AGStatus"].Value = (int)row["AGStatus"]==3 ? "" : "View";
                dgvSummary.Rows[idx].Cells["QueryStoreStatus"].Value = (int)row["QueryStoreStatus"] == 3 ? "" : "View";
                if (row["IsAgentRunning"]!=DBNull.Value && (bool)row["IsAgentRunning"] == false)
                {
                    dgvSummary.Rows[idx].Cells["JobStatus"].SetStatusColor(Color.Black);
                    dgvSummary.Rows[idx].Cells["JobStatus"].Value = "Not Running";
                }              

                string uptimeString;
                if (row["sqlserver_uptime"] != DBNull.Value)
                {
                    Int32 uptime = (Int32)row["sqlserver_uptime"];
                    Int32 addUptime = (Int32)row["AdditionalUptime"];
                    if (uptime < 120)
                    {
                        uptimeString = uptime.ToString() + " Mins (+" + addUptime.ToString() + "mins)";
                    }
                    else if (uptime < 1440)
                    {
                        uptimeString = (uptime / 60).ToString("0") + " Hours  (+" + addUptime.ToString() + "mins)";
                    }
                    else
                    {
                        uptimeString = (uptime / 1440).ToString("0") + " days";
                    }
                    dgvSummary.Rows[idx].Cells["UptimeStatus"].Value = uptimeString;
                }

                if (row["SnapshotAgeMin"] == DBNull.Value || row["SnapshotAgeMax"] == DBNull.Value)
                {
                    dgvSummary.Rows[idx].Cells["SnapshotAgeStatus"].Value = "N/A";
                }
                else
                {
                    Int32 snapshotAgeMin = (Int32)row["SnapshotAgeMin"];
                    Int32 snapshotAgeMax = (Int32)row["SnapshotAgeMax"];
                    if (snapshotAgeMax == snapshotAgeMin)
                    {
                        dgvSummary.Rows[idx].Cells["SnapshotAgeStatus"].Value = snapshotAgeMax + "mins";
                    }
                    else
                    {
                        dgvSummary.Rows[idx].Cells["SnapshotAgeStatus"].Value = snapshotAgeMin.ToString() + " to " + snapshotAgeMax.ToString() + "mins";
                    }
                }
                if (row["DaysSinceLastGoodCheckDB"] != DBNull.Value)
                {
                    dgvSummary.Rows[idx].Cells["LastGoodCheckDBStatus"].Value = ((Int32)row["DaysSinceLastGoodCheckDB"]).ToString() + " days";
                }
                string oldestLastGoodCheckDB="Unknown";
                if(row["OldestLastGoodCheckDBTime"] !=DBNull.Value)
                {
                    if((DateTime)row["OldestLastGoodCheckDBTime"] == DateTime.Parse("1900-01-01"))
                    {
                        oldestLastGoodCheckDB = "Never";
                        dgvSummary.Rows[idx].Cells["LastGoodCheckDBStatus"].Value = "Never";
                    }
                    else
                    {
                        oldestLastGoodCheckDB = ((DateTime)row["OldestLastGoodCheckDBTime"]).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    }

                }
                if (row["LastGoodCheckDBCriticalCount"] != DBNull.Value)
                {
                    dgvSummary.Rows[idx].Cells["LastGoodCheckDBStatus"].ToolTipText = "Last Good CheckDB Critical:" + (Int32)row["LastGoodCheckDBCriticalCount"] + Environment.NewLine +
                                                                               "Last Good CheckDB Warning:" + (Int32)row["LastGoodCheckDBWarningCount"] + Environment.NewLine +
                                                                               "Last Good CheckDB Good:" + (Int32)row["LastGoodCheckDBHealthyCount"] + Environment.NewLine +
                                                                               "Last Good CheckDB NA:" + (Int32)row["LastGoodCheckDBNACount"] + Environment.NewLine +
                                                                               "Oldest Last Good CheckDB:" + oldestLastGoodCheckDB;
                    ;
                }
                if (row["LastMemoryDump"]!= DBNull.Value)
                {
                    DateTime lastMemoryDump = (DateTime)row["LastMemoryDump"];
                    Int32 memoryDumpCount = (Int32)row["MemoryDumpCount"];
                    var lastMemoryDumpHrs = DateTime.UtcNow.Subtract(lastMemoryDump).TotalHours;
                    var lastMemoryDumpDays = DateTime.UtcNow.Subtract(lastMemoryDump).TotalDays;
                    if (DateTime.UtcNow.Subtract(lastMemoryDump).TotalHours< 24)
                    {
                        dgvSummary.Rows[idx].Cells["MemoryDumpStatus"].Value = lastMemoryDumpHrs.ToString("0") + "hrs";
                    }
                    else
                    {
                        dgvSummary.Rows[idx].Cells["MemoryDumpStatus"].Value = lastMemoryDumpDays.ToString("0") + " days";
                    }
                    dgvSummary.Rows[idx].Cells["MemoryDumpStatus"].ToolTipText = "Last Memory Dump:" + lastMemoryDump.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + "Total Memory Dumps:" + memoryDumpCount;


                }
                string lastAlert = "Never";
                string lastAlertDays = "Never";
                string lastCriticalAlert = "Never";
                if (row["LastAlert"] != DBNull.Value)
                {
                    DateTime lastAlertD = (DateTime)row["LastAlert"];
                    lastAlert = lastAlertD.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
                    if(DateTime.UtcNow.Subtract(lastAlertD).TotalHours < 24)
                    {
                        lastAlertDays = DateTime.UtcNow.Subtract(lastAlertD).TotalHours.ToString("0") + "hrs";
                    }
                    else
                    {
                        lastAlertDays = DateTime.UtcNow.Subtract(lastAlertD).TotalDays.ToString("0") + " days";
                    }
                }
               if(row["LastCritical"] != DBNull.Value)
                {
                    lastCriticalAlert = (((DateTime)row["LastCritical"]).ToLocalTime()).ToString("yyyy-MM-dd HH:mm");
                }
                Int32 totalAlerts = row["TotalAlerts"] == DBNull.Value ? 0 : (Int32)row["TotalAlerts"];

                dgvSummary.Rows[idx].Cells["AlertStatus"].Value = lastAlertDays;
                dgvSummary.Rows[idx].Cells["AlertStatus"].ToolTipText = "Last Alert:" + lastAlert + Environment.NewLine + 
                                                                        "Last Critical Alert:" + lastCriticalAlert + Environment.NewLine + 
                                                                        "Total Alerts:" + totalAlerts;
                dgvSummary.Rows[idx].Cells["ElasticPoolStorageStatus"].Value = isAzure ? "View" : "";
            }
        }

        private void dgvSummary_ColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            var sortCol = "";
            if(dgvSummary.Columns[e.ColumnIndex] == FullBackupStatus)
            {
                sortCol = "FullBackupStatus";
            }
            if(dgvSummary.Columns[e.ColumnIndex] == AlertStatus)
            {
                sortCol = "AlertStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == SnapshotAgeStatus)
            {
                sortCol = "SnapshotAgeMax";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == JobStatus)
            {
                sortCol = "JobStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == AGStatus)
            {
                sortCol = "AGStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == CorruptionStatus)
            {
                sortCol = "CorruptionStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == UptimeStatus)
            {
                sortCol = "sqlserver_uptime";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == DriveStatus)
            {
                sortCol = "DriveStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == MemoryDumpStatus)
            {
                sortCol = "LastMemoryDump";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LastGoodCheckDBStatus )
            {
                sortCol = "LastGoodCheckDBStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == DiffBackupStatus)
            {
                sortCol = "DiffBackupStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LogBackupStatus)
            {
                sortCol = "LogBackupStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LogShippingStatus)
            {
                sortCol = "LogShippingStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == FileFreeSpaceStatus)
            {
                sortCol = "FileFreeSpaceStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == LogFreeSpaceStatus)
            {
                sortCol = "LogFreeSpaceStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == CollectionErrorStatus)
            {
                sortCol = "CollectionErrorStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == ElasticPoolStorageStatus)
            {
                sortCol = "ElasticPoolStorageStatus";
            }
            if (dgvSummary.Columns[e.ColumnIndex] == PctMaxSizeStatus)
            {
                sortCol = "PctMaxSizeStatus";
            }
            if (sortCol != "")
            {
                if (dv.Sort == sortCol) {
                    dv.Sort = sortCol += " DESC";
                }
                else
                {
                    dv.Sort = sortCol;
                }
            }
        }

        private void tsRefresh_Click(object sender, EventArgs e)
        {
            RefreshData();
        }

        private void tsCopy_Click(object sender, EventArgs e)
        {
            Common.CopyDataGridViewToClipboard(dgvSummary);
        }

        public event EventHandler<InstanceSelectedEventArgs> Instance_Selected;

        private void dgvSummary_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                DataRowView row = (DataRowView)dgvSummary.Rows[e.RowIndex].DataBoundItem;
                if (e.ColumnIndex== LastGoodCheckDBStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabLastGood" });
                    }                   
                }
                else if(e.ColumnIndex == FullBackupStatus.Index || e.ColumnIndex == DiffBackupStatus.Index || e.ColumnIndex == LogBackupStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabBackups" });
                    }
                }
                else if (e.ColumnIndex == LogShippingStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabLogShipping" });
                    }
                }
                else if(e.ColumnIndex == DriveStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabDrives" });
                    }
                }
                else if (e.ColumnIndex == JobStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabJobs" });
                    }
                }
                else if (e.ColumnIndex == FileFreeSpaceStatus.Index || e.ColumnIndex == PctMaxSizeStatus.Index || e.ColumnIndex == LogFreeSpaceStatus.Index)
                {
                     Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabFiles" });
                }
                else if (e.ColumnIndex == CustomCheckStatus.Index)
                {
                       Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabCustomChecks" });
                }
                else if (e.ColumnIndex ==  CollectionErrorStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabDBADashErrorLog" });
                    }
                    else
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID=-1, Instance = (string)row["InstanceGroupName"], Tab = "tabDBADashErrorLog" });
                    }
                }
                else if (e.ColumnIndex == SnapshotAgeStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabCollectionDates" });                   
                }
                else if (e.ColumnIndex == AlertStatus.Index)
                {
                    if (row["InstanceID"] != DBNull.Value)
                    {
                        Instance_Selected(this, new InstanceSelectedEventArgs() { InstanceID = (Int32)row["InstanceID"], Tab = "tabAlerts" });
                    }
                }
                else if(e.ColumnIndex == ElasticPoolStorageStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabAzureSummary" });
                }
                else if(e.ColumnIndex== AGStatus.Index)
                {
                    Instance_Selected(this, new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabAG" });
                }
                else if ( e.ColumnIndex == QueryStoreStatus.Index)
                {
                    Instance_Selected(this,new InstanceSelectedEventArgs() { Instance = (string)row["InstanceGroupName"], Tab = "tabQS" });
                }
                else if (e.ColumnIndex== UptimeStatus.Index)
                {
                    var frm = new UptimeThresholdConfig() { InstanceID = (Int32)row["InstanceID"] };
                    frm.ShowDialog();
                    if(frm.DialogResult == DialogResult.OK)
                    {
                        RefreshData();
                    }
                }
            }
        }

        private void tsExcel_Click(object sender, EventArgs e)
        {
            Common.PromptSaveDataGridView(ref dgvSummary);
        }

        private void focusedViewToolStripMenuItem_Click(object sender, EventArgs e)
        {
            focusedView = focusedViewToolStripMenuItem.Checked;
            RefreshData();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (DateTime.Now.Subtract(lastRefresh).TotalMinutes > 60){
                lblRefreshTime.ForeColor = DBADashStatusEnum.Critical.GetColor();
                timer1.Enabled = false;
            }
            else if(DateTime.Now.Subtract(lastRefresh).TotalMinutes>10)
            {
                lblRefreshTime.ForeColor = DBADashStatusEnum.Warning.GetColor();
            }
        }

        private void configureThresholdsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using(var frm = new MemoryDumpThresholdsConfig())
            {
                frm.ShowDialog(this);
                if(frm.DialogResult == DialogResult.OK)
                {
                    RefreshData();
                }
            }
        }

        private void acknowledgeDumpsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MemoryDumpThresholds.Acknowledge();
            MessageBox.Show("Memory dump acknowledge date updated", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshData();
        }
    }
}
