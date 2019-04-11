﻿// -----------------------------------------------------------------------
// <copyright file="CustomQueryView.cs"  company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using Gtk;
using UserInterface.Interfaces;
using UserInterface.Views;
using Utility;

namespace ApsimNG.Views.CLEM
{
    /// <summary>
    /// Displays the result of a Custom SQL Query on a DataTable
    /// </summary>
    class CustomQueryView : ViewBase
    {
        // Components of the interface that are interactable
        // Taken from CustomQueryView.glade
        private Notebook notebook1 = null;
        private VBox vbox4 = null;
        private Entry fileentry = null;
        private Entry tableentry = null;
        private Button loadbtn = null;
        private Button savebtn = null;
        private Button saveasbtn = null;
        private Button runbtn = null;
        private Button storebtn = null;
        private TextView textview1 = null;

        // Custom gridview added post-reading the glade file
        public GridView Grid { get; set; } = null;

        // Raw text containing the SQL query
        public string Sql
        {
            get
            {
                return textview1.Buffer.Text;
            }
            set
            {
                textview1.Buffer.Text = value;
            }
        }
        
        // Name of the file containing raw SQL
        public string Filename
        {
            get
            {
                return fileentry.Text;
            }
            set
            {
                fileentry.Text = value;
            }
        }

        // Name of the table generated by the query
        public string Tablename
        {
            get
            {
                return tableentry.Text;
            }
            set
            {
                tableentry.Text = value;
            }
        }

        /// <summary>
        /// Instantiate the View
        /// </summary>
        /// <param name="owner"></param>
        public CustomQueryView(ViewBase owner) : base(owner)
        {
            // Read in the glade file
            Builder builder = BuilderFromResource("ApsimNG.Resources.Glade.CustomQueryView.glade");

            // Assign the interactable objects from glade
            notebook1 = (Notebook)builder.GetObject("notebook1");
            vbox4 = (VBox)builder.GetObject("vbox4");
            fileentry = (Entry)builder.GetObject("fileentry");
            tableentry = (Entry)builder.GetObject("tableentry");
            loadbtn = (Button)builder.GetObject("loadbtn");
            savebtn = (Button)builder.GetObject("savebtn");
            saveasbtn = (Button)builder.GetObject("saveasbtn");
            runbtn = (Button)builder.GetObject("runbtn");
            storebtn = (Button)builder.GetObject("storebtn");
            textview1 = (TextView)builder.GetObject("textview1");

            // Add the custom gridview (external to glade)
            Grid = new GridView(owner);
            Grid.ReadOnly = true;
            
            vbox4.Add(Grid.MainWidget);
            //notebook1.AppendPage(gridview1.MainWidget, data);

            // Assign methods to button click events
            loadbtn.Clicked += OnLoadClicked;
            savebtn.Clicked += OnSaveClicked;
            saveasbtn.Clicked += OnSaveAsClicked;
            runbtn.Clicked += OnRunClicked;
            storebtn.Clicked += OnStoreClicked;

            // Let the viewbase know which widget is the main widget
            mainWidget = notebook1;
        }

        /// <summary>
        /// New query execution event
        /// </summary>
        public event EventHandler OnRunQuery;
       
        /// <summary>
        /// New file load event
        /// </summary>
        public event EventHandler OnLoadFile;

        public event EventHandler<WriteTableEventArgs> OnWriteTable;

        /// <summary>
        /// Arguments for the WriteTableEvent
        /// </summary>
        public class WriteTableEventArgs :  EventArgs
        {
            public string Tablename { get; set; }
        }

        /// <summary>
        /// Select an SQL query file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnLoadClicked(object sender, EventArgs e)
        {
            try
            {
                // Open a file
                IFileDialog fileDialog = new FileDialog()
                {
                    Prompt = "Select query file.",
                    Action = FileDialog.FileActionType.Open,
                    FileType = "SQL files (*.sql)|*.sql"
                };

                // Write filename to the entrybox
                string filename = fileDialog.GetFile();
                fileentry.Text = filename;

                // Default table name is the file name
                tableentry.Text = Path.GetFileNameWithoutExtension(filename);

                // Write file contents to the textview buffer
                string sql = File.ReadAllText(filename);
                textview1.Buffer.Text = sql;
                
            }
            catch (Exception error)
            {
                ShowError(error);
            }

            // Invoke the loadfile event if it has subscribers
            if (OnLoadFile != null)
            {
                OnLoadFile.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Open an SQL query file
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The argument parameters</param>
        private void OnSaveAsClicked(object sender, EventArgs e)
        {
            try
            {
                IFileDialog fileDialog = new FileDialog()
                {
                    Prompt = "Select query file.",
                    Action = FileDialog.FileActionType.Save,
                    FileType = "SQL files (*.sql)|*.sql|Text files (*.txt)|*.txt"
                };

                string filename = fileDialog.GetFile();
                fileentry.Text = filename;

                File.WriteAllText(filename, Sql);                
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        /// <summary>
        /// Overwrites the file stored in the entry box with the displayed SQL
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The argument parameters</param>
        private void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {    
                if (fileentry.Text != null)
                {
                    File.WriteAllText(fileentry.Text, textview1.Buffer.Text);
                }              
            }
            catch (Exception error)
            {
                ShowError(error);
            }
        }

        /// <summary>
        /// Invokes the RunQuery event if it has subscribers
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The argument parameters</param>
        private void OnRunClicked(object sender, EventArgs e)
        {
            if (OnRunQuery != null)
            {
                OnRunQuery.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Invokes the WriteTable event if it has subscribers
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The argument parameters</param>
        private void OnStoreClicked(object sender, EventArgs e)
        {            
            if (OnWriteTable != null)
            {
                Tablename = tableentry.Text;
                WriteTableEventArgs args = new WriteTableEventArgs();
                args.Tablename = tableentry.Text;

                OnWriteTable.Invoke(this, args);
            }
        }

        /// <summary>
        /// Detach the view
        /// </summary>
        public void Detach()
        {
            loadbtn.Clicked -= OnLoadClicked;
            savebtn.Clicked -= OnSaveClicked;
            saveasbtn.Clicked -= OnSaveAsClicked;
            runbtn.Clicked -= OnRunClicked;
            storebtn.Clicked -= OnStoreClicked;
        }
    }
}
