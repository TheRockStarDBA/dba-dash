﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.SqlClient;
using DBAChecksGUI.Performance;

namespace DBAChecksGUI
{
    public partial class SlowQueries : UserControl
    {
        public SlowQueries()
        {
            InitializeComponent();
        }

        public List<Int32> InstanceIDs;
        public string ConnectionString;
        string groupBy = "ConnectionID";

        Int32 mins = 15;
        private DateTime _from = DateTime.MinValue;
        private DateTime _to = DateTime.MinValue;
        private DateTime fromDate
        {
            get
            {
                if (_from == DateTime.MinValue)
                {
                    return DateTime.UtcNow.AddMinutes(-mins);
                }
                else
                {
                    return _from;
                }
            }
        }

        private DateTime toDate
        {
            get
            {
                if (_to == DateTime.MinValue)
                {
                    return DateTime.UtcNow;
                }
                else
                {
                    return _to;
                }

            }
        }

        public void ResetFilters()
        {
            txtText.Text = "";
            txtClient.Text = "";
            txtDatabase.Text = "";
            txtInstance.Text = "";
            txtObject.Text = "";
            txtText.Text = "";
            txtUser.Text = "";
            txtApp.Text = "";
            groupBy = "ConnectionID";
            selectGroupBy();
        }


        public void RefreshData()
        {
            dgvSlow.DataSource = null;
            SqlConnection cn = new SqlConnection(ConnectionString);
            using (cn)
            {
                cn.Open();
                SqlCommand cmd = new SqlCommand("dbo.SlowQueriesSummary", cn);
                cmd.Parameters.AddWithValue("InstanceIDs", string.Join(",", InstanceIDs));
                cmd.Parameters.AddWithValue("FromDate", fromDate);
                cmd.Parameters.AddWithValue("ToDate", toDate);
                cmd.Parameters.AddWithValue("GroupBy", groupBy);
                if (txtClient.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ClientHostName", txtClient.Text);
                }
                if (txtInstance.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ConnectionID", txtInstance.Text);
                }
                if (txtApp.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ClientAppName", txtApp.Text);
                }
                if (txtDatabase.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("DatabaseName", txtDatabase.Text);
                }
                if (txtObject.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ObjectName", txtObject.Text);
                }
                if (txtUser.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("UserName", txtUser.Text);
                }
                if (txtText.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("Text", txtText.Text);
                }
                cmd.CommandType = CommandType.StoredProcedure;
                SqlDataAdapter da = new SqlDataAdapter(cmd);
                var dt = new DataTable();
                da.Fill(dt);
                dgvSummary.AutoGenerateColumns = false;
                dgvSummary.DataSource = dt;

            }

        }

        private void tsTime_Click(object sender, EventArgs e)
        {
            var itm = (ToolStripMenuItem)sender;
            mins = Int32.Parse((string)itm.Tag);
            _from = DateTime.MinValue;
            _to = DateTime.MinValue;
            RefreshData();
            checkTime();
        }

        private void checkTime()
        {
            foreach (var ts in tsTime.DropDownItems)
            {
                if (ts.GetType() == typeof(ToolStripMenuItem))
                {
                    var tsmi = (ToolStripMenuItem)ts;
                    tsmi.Checked = Int32.Parse((string)tsmi.Tag) == mins;
                }
            }
        }

        private void tsCustom_Click(object sender, EventArgs e)
        {
            var frm = new CustomTimePicker();
            frm.FromDate = fromDate;
            frm.ToDate = toDate;
            frm.ShowDialog();
            if (frm.DialogResult == DialogResult.OK)
            {
                _from = frm.FromDate;
                _to = frm.ToDate;
                mins = 0;
                checkTime();
            }
            RefreshData();
            tsCustom.Checked = true;
        }

        private void GroupBy_Click(object sender, EventArgs e)
        {
            var selected = (ToolStripMenuItem)sender;
            groupBy = (string)selected.Tag;
            selectGroupBy();
            RefreshData();
        }

        private void selectGroupBy()
        {
            foreach (ToolStripMenuItem mnu in tsGroup.DropDownItems)
            {
                mnu.Checked = (string)mnu.Tag == groupBy;
                if (mnu.Checked)
                {
                    Grp.HeaderText = mnu.Text;
                }
            }
        }


        private void dgvSummary_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (dgvSummary.Columns[e.ColumnIndex] == Grp)
            {
                var row = (DataRowView)dgvSummary.Rows[e.RowIndex].DataBoundItem;
                string value = row["Grp"] == DBNull.Value ? "" : (string)row["Grp"];
                if (groupBy == "ConnectionID")
                {
                    txtInstance.Text = value;
                }
                else if (groupBy == "client_hostname")
                {
                    txtClient.Text = value;
                }
                else if (groupBy == "client_app_name")
                {
                    txtApp.Text = value;
                }
                else if (groupBy == "DatabaseName")
                {
                    txtDatabase.Text = value;
                }
                else if (groupBy == "object_name")
                {
                    txtObject.Text= value;
                }
                else if(groupBy == "username")
                {
                    txtUser.Text = value;
                }
                else
                {
                    throw new Exception("Invalid group by");
                }

                if (txtInstance.Text.Length == 0)
                {
                    groupBy = "ConnectionID";
                }
                else if (txtDatabase.Text.Length == 0)
                {
                    groupBy = "DatabaseName";
                }
                else if (txtApp.Text.Length == 0)
                {
                    groupBy = "client_app_name";
                }
                else if (txtClient.Text.Length == 0)
                {
                    groupBy = "client_hostname";
                }
                else if (txtObject.Text.Length == 0)
                {
                    groupBy = "object_name";
                }
                else
                {
                    groupBy = "username";
                }
                selectGroupBy();
                RefreshData();
            }
            else if(dgvSummary.Columns[e.ColumnIndex] == Total)
            {
                loadSlowQueriesDetail();
            }
            else if(dgvSummary.Columns[e.ColumnIndex] == _1hrPlus)
            {
                loadSlowQueriesDetail(3600, -1);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _30to60min)
            {
                loadSlowQueriesDetail(1800,3600);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _10to30min)
            {
                loadSlowQueriesDetail(600, 1800);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _5to10min)
            {
                loadSlowQueriesDetail(300,600);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _1to5min)
            {
                loadSlowQueriesDetail(60, 300);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _30to60)
            {
                loadSlowQueriesDetail(30,60);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _20to30)
            {
                loadSlowQueriesDetail(20,30);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _10to20)
            {
                loadSlowQueriesDetail(10,20);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _5to10)
            {
                loadSlowQueriesDetail(5,10);
            }
            else if (dgvSummary.Columns[e.ColumnIndex] == _1to5)
            {
                loadSlowQueriesDetail(1,5);
            }
        }

        private void SlowQueries_Load(object sender, EventArgs e)
        {
            selectGroupBy();
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ResetFilters();
            RefreshData();
        }

        private void txtInstance_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtInstance, lblInstance);
        }

        private void setFilterHighlight(ToolStripTextBox txt, ToolStripMenuItem lbl)
        {
            if (txt.Text.Length > 0)
            {
                lbl.Font = new Font(lbl.Font, FontStyle.Bold);
            }
            else
            {
                lbl.Font = new Font(lbl.Font, FontStyle.Regular);
            }
        }

        private void txtClient_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtClient, lblClient);
        }

        private void txtApp_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtApp, lblApp);
        }

        private void txtDatabase_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtDatabase, lblDatabase);
        }

        private void txtObject_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtObject, lblObject);
        }

        private void txtUser_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtUser, lblUser);
        }

        private void txtText_TextChanged(object sender, EventArgs e)
        {
            setFilterHighlight(txtText, lblText);
        }

        private void loadSlowQueriesDetail(Int32 durationFrom=-1,Int32 durationTo=-1)
        {
        
            SqlConnection cn = new SqlConnection(ConnectionString);
            using (cn)
            {
                cn.Open();
                SqlCommand cmd = new SqlCommand("dbo.SlowQueriesDetail", cn);
                cmd.Parameters.AddWithValue("InstanceIDs", string.Join(",", InstanceIDs));
                cmd.Parameters.AddWithValue("FromDate", fromDate);
                cmd.Parameters.AddWithValue("ToDate", toDate);
                cmd.Parameters.AddWithValue("Top", 1000);
                if (txtClient.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ClientHostName", txtClient.Text);
                }
                if (txtInstance.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ConnectionID", txtInstance.Text);
                }
                if (txtApp.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ClientAppName", txtApp.Text);
                }
                if (txtDatabase.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("DatabaseName", txtDatabase.Text);
                }
                if (txtObject.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("ObjectName", txtObject.Text);
                }
                if (txtUser.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("UserName", txtUser.Text);
                }
                if (txtText.Text.Length > 0)
                {
                    cmd.Parameters.AddWithValue("Text", txtText.Text);
                }
                if (durationFrom > 0)
                {
                    cmd.Parameters.AddWithValue("DurationFromSec", durationFrom);
                }
                if (durationTo > 0)
                {
                    cmd.Parameters.AddWithValue("DurationToSec", durationTo);
                }
                cmd.CommandType = CommandType.StoredProcedure;
                SqlDataAdapter da = new SqlDataAdapter(cmd);
                var dt = new DataTable();
                da.Fill(dt);
               dgvSlow.AutoGenerateColumns = false;
                dgvSlow.DataSource = dt;

            }
        }

        private void tsRefresh_Click(object sender, EventArgs e)
        {
            RefreshData();
        }
    }
}
