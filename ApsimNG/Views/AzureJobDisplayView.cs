﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserInterface.Interfaces;
using ApsimNG.Cloud;
using Gtk;
using GLib;
namespace UserInterface.Views
{
    public class AzureJobDisplayView : ViewBase, IAzureJobDisplayView
    {
        private List<JobDetails> jobList;
        private TreeView tree;
        private ListStore store;
        private TreeViewColumn columnName;
        private TreeViewColumn columnId;
        private TreeViewColumn columnState;
        private TreeViewColumn columnProgress;
        private TreeViewColumn columnStartTime;
        private TreeViewColumn columnEndTime;

        private CellRendererText cellName;
        private CellRendererText cellId;
        private CellRendererText cellState;
        private CellRendererText cellProgress;
        private CellRendererText cellStartTime;
        private CellRendererText cellEndTime;
        private TreeModelFilter filterOwner;
        private TreeModelSort sort;
        private VBox vboxPrimary;
        private CheckButton chkFilterOwner;
        private Label lblProgress;
        private ProgressBar loadingProgress;
        private HBox progress;

        public AzureJobDisplayView(ViewBase owner) : base(owner)
        {
            jobList = new List<JobDetails>();
            store = new ListStore(typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string));

            // create colummns
            columnName = new TreeViewColumn
            {
                Title = "Name/Description",
                SortColumnId = 0
            };

            columnId = new TreeViewColumn
            {
                Title = "Job ID",
                SortColumnId = 1
            };


            columnState = new TreeViewColumn
            {
                Title = "Status",
                SortColumnId = 2
            };

            columnProgress = new TreeViewColumn
            {
                Title = "Progress",
                SortColumnId = 3
            };

            columnStartTime = new TreeViewColumn
            {
                Title = "Start Time",
                SortColumnId = 4
            };

            columnEndTime = new TreeViewColumn
            {
                Title = "End Time",
                SortColumnId = 5,
            };

            // create cells for each column
            cellName = new CellRendererText();            
            cellId = new CellRendererText();
            cellState = new CellRendererText();
            cellProgress = new CellRendererText();
            cellStartTime = new CellRendererText();
            cellEndTime = new CellRendererText();

            // bind cells to column
            columnName.PackStart(cellName, false);
            columnId.PackStart(cellId, false);
            columnState.PackStart(cellState, false);
            columnProgress.PackStart(cellProgress, false);
            columnStartTime.PackStart(cellStartTime, false);
            columnEndTime.PackStart(cellEndTime, false);


            columnName.AddAttribute(cellName, "text", 0);
            columnId.AddAttribute(cellId, "text", 1);
            columnState.AddAttribute(cellState, "text", 2);
            columnProgress.AddAttribute(cellProgress, "text", 3);
            columnStartTime.AddAttribute(cellStartTime, "text", 4);
            columnEndTime.AddAttribute(cellEndTime, "text", 5);

            tree = new TreeView();
            tree.AppendColumn(columnName);
            tree.AppendColumn(columnId);
            tree.AppendColumn(columnState);
            tree.AppendColumn(columnProgress);
            tree.AppendColumn(columnStartTime);
            tree.AppendColumn(columnEndTime);


            chkFilterOwner = new CheckButton("Display my jobs only");
            chkFilterOwner.Toggled += RedrawJobs;
            filterOwner = new TreeModelFilter(store, null);
            filterOwner.VisibleFunc = FilterOwnerFunc;

            filterOwner.Refilter();
            sort = new TreeModelSort(filterOwner);
            
            sort.SetSortFunc(0, SortName);
            sort.SetSortFunc(1, SortId);
            sort.SetSortFunc(2, SortState);
            sort.SetSortFunc(3, SortProgress);            
            sort.SetSortFunc(4, SortStartDate);
            sort.SetSortFunc(5, SortStartDate);

            
            tree.Model = sort;

            lblProgress = new Label("Loading: 0.00%");
            lblProgress.Xalign = 0f;

            loadingProgress = new ProgressBar(new Adjustment(0, 0, 100, 0.01, 0.01, 100));
            
            loadingProgress.Adjustment.Lower = 0;
            loadingProgress.Adjustment.Upper = 100;

            vboxPrimary = new VBox();
            vboxPrimary.PackStart(tree, false, false, 0);
            vboxPrimary.PackStart(chkFilterOwner, false, true, 0);

            progress = new HBox();
            progress.PackStart(new Label("Loading Jobs: "), false, false, 0);
            progress.PackStart(loadingProgress, false, false, 0);
            progress.PackStart(new Label(""), false, false, 0);

            vboxPrimary.PackStart(progress, false, false, 0);
            _mainWidget = vboxPrimary;
        }

        private int SortName(TreeModel model, TreeIter a, TreeIter b)
        {
            return SortStrings(model, a, b, 0);
        }

        private int SortId(TreeModel model, TreeIter a, TreeIter b)
        {
            return SortStrings(model, a, b, 1);
        }

        private int SortState(TreeModel model, TreeIter a, TreeIter b)
        {
            return SortStrings(model, a, b, 2);
        }

        private int SortProgress(TreeModel model, TreeIter a, TreeIter b)
        {
            return SortStrings(model, a, b, 3);
        }

        /// <summary>
        /// Event Handler for the "view my jobs only" checkbox. Re-applies the job owner filter.
        /// </summary>
        /// <param name="o"></param>
        /// <param name="e"></param>
        private void RedrawJobs(object o, EventArgs e)
        {
            filterOwner.Refilter();
            AddJobsToTable(jobList);            
        }

        /// <summary>
        /// Tests whether a job should be displayed or not.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="iter"></param>
        /// <returns>True if the display my jobs only checkbox is inactive, or if the job's owner is the same as the user's username. False otherwise.</returns>
        private bool FilterOwnerFunc(TreeModel model, TreeIter iter)
        {
            string owner = GetJobOwner((string)model.GetValue(iter, 1));
            return !chkFilterOwner.Active || owner.ToLower() == Environment.UserName.ToLower();            
        }

        private int SortStartDate(TreeModel model, TreeIter a, TreeIter b)
        {
            return SortDateStrings(model, a, b, 4);
        }

        /// <summary>
        /// Sorts two date strings in the ListStore.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns>-1 if the first date is before the second. 1 otherwise.</returns>
        private int SortEndDate(TreeModel model, TreeIter a, TreeIter b)
        {
            return SortDateStrings(model, a, b, 5);
        }


        /// <summary>
        /// Displays the progress of downloading the job details from the cloud.
        /// </summary>
        /// <param name="proportion"></param>
        public void UpdateJobLoadStatus(double proportion)
        {
            if (jobList.Count != 0) return;
            lblProgress.Text = "Loading: " + Math.Round(proportion, 2).ToString() + "%";
            loadingProgress.Adjustment.Value = proportion;
            //loadingProgress.Text = Math.Round(proportion, 2).ToString() + "%";
            if (Math.Abs(proportion - 100) < Math.Pow(10, -6))
            {
                vboxPrimary.Remove(progress);
            }
            else if (proportion < Math.Pow(10, -6))
            {
                vboxPrimary.PackStart(progress, false, false, 0);
            }
        }

        /// <summary>
        /// Sorts two date/time strings in the ListStore.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="n">The column - 4 for start time, 5 for end time</param>
        /// <returns></returns>
        private int SortDateStrings(TreeModel model, TreeIter a, TreeIter b, int n)
        {
            if (!(n == 4 || n == 5)) return -1;
            DateTime t1 = GetDateTimeFromString((string)model.GetValue(a, n));
            DateTime t2 = GetDateTimeFromString((string)model.GetValue(b, n));

            return DateTime.Compare(t1, t2);
        }

        /// <summary>
        /// Generates a DateTime object from a string.
        /// </summary>
        /// <param name="st">Date time string. MUST be in the format dd/mm/yyyy hh:mm:ss</param>
        /// <returns>A DateTime object representing this string.</returns>
        private DateTime GetDateTimeFromString(string st)
        {
            string[] separated = st.Split(' ');
            string[] date = separated[0].Split('/');
            string[] time = separated[1].Split(':');
            int year, month, day, hour, minute, second;
            try
            {
                day = Int32.Parse(date[0]);
                month = Int32.Parse(date[1]);
                year = Int32.Parse(date[2]);

                hour = Int32.Parse(time[0]);
                minute = Int32.Parse(time[1]);
                second = Int32.Parse(time[2]);

                return new DateTime(year, month, day, hour, minute, second);
            }
            catch (Exception e)
            {
                ShowError(e.Message);
            }
            return new DateTime();
        }

        /// <summary>
        /// Sorts strings from two successive rows in the ListStore. 
        /// </summary>
        /// <param name="model">The ListStore containing the data.</param>
        /// <param name="a">First row</param>
        /// <param name="b">Second row</param>
        /// <param name="x">Column number (0-indexed)</param>
        /// <returns>-1 if the first string is lexographically less than the second. 1 otherwise.</returns>
        private int SortStrings(TreeModel model, TreeIter a, TreeIter b, int x)
        {            
            string s1 = (string)model.GetValue(a, x);
            string s2 = (string)model.GetValue(b, x);
            return String.Compare(s1, s2);
        }

        /// <summary>
        /// Gets the owner of the job with a given id.
        /// </summary>
        /// <param name="id">ID of the job.</param>
        /// <returns>Owner of the job</returns>
        private string GetJobOwner(string id)
        {
            foreach (var job in jobList)
            {
                if (job.Id == id) return job.Owner;
            }
            return "";
        }

        /// <summary>
        /// Redraws the TreeView if and only if the list of jobs passed in is different to the list of jobs already displayed.
        /// </summary>
        /// <param name="jobs"></param>
        public void AddJobsToTableIfNecessary(List<JobDetails> jobs)
        {
            if (jobList.SequenceEqual(jobs)) return;
            
            AddJobsToTable(jobs);
        }

        /// <summary>
        /// Redraws the TreeView.
        /// </summary>
        /// <param name="jobs">List of jobs to insert into the TreeView.</param>
        private void AddJobsToTable(List<JobDetails> jobs)
        {            
            jobList = jobs;            
            store = new ListStore(typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string));            
            foreach (JobDetails job in jobs)
            {
                string startTimeString = job.StartTime == null ? DateTime.UtcNow.ToLocalTime().ToString() : ((DateTime)job.StartTime).ToLocalTime().ToString();
                string endTimeString = job.EndTime == null ? "" : ((DateTime)job.EndTime).ToLocalTime().ToString();                
                string dispName = chkFilterOwner.Active ? job.DisplayName : job.DisplayName + " (" + job.Owner + ")";
                string progressString = job.Progress < 0 ? "Work in progress" : Math.Round(job.Progress, 2).ToString() + "%";                
                store.AppendValues(dispName, job.Id, job.State, progressString, startTimeString, endTimeString);
            }

            filterOwner = new TreeModelFilter(store, null);
            filterOwner.VisibleFunc = FilterOwnerFunc;            
            filterOwner.Refilter();

            sort = new TreeModelSort(filterOwner);
            sort.SetSortFunc(0, SortName);
            sort.SetSortFunc(1, SortId);
            sort.SetSortFunc(2, SortState);
            sort.SetSortFunc(3, SortProgress);
            sort.SetSortFunc(4, SortStartDate);
            sort.SetSortFunc(5, SortStartDate);
            
            tree.Model = sort;
        }

        /// <summary>
        /// Displays an error message in a pop-up box.
        /// </summary>
        /// <param name="msg">Message to be displayed.</param>
        public void ShowError(string msg)
        {
            MessageDialog md = new MessageDialog(null, DialogFlags.DestroyWithParent, MessageType.Error, ButtonsType.Ok, msg);
            md.Title = "Sanity Check Failed - High-Grade Insanity Detected!!!";
            md.Run();
            md.Destroy();
        }
    }
}
