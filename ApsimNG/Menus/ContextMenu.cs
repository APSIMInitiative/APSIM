using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using APSIM.Documentation;
using APSIM.Server.Sensibility;
using APSIM.Shared.Utilities;
using Gtk;
using Models;
using Models.Climate;
using Models.Core;
using Models.Core.ApsimFile;
using Models.Core.Run;
using Models.Factorial;
using Models.Functions;
using Models.Soils;
using Models.Storage;
using UserInterface.Commands;
using Utility;

namespace UserInterface.Presenters
{

    /// <summary>
    /// This class contains methods for all context menu items that the ExplorerView exposes to the user.
    /// </summary>
    public class ContextMenu
    {
        [Link(IsOptional = true)]
        IDataStore storage = null;

        /// <summary>
        /// Reference to the ExplorerPresenter.
        /// </summary>
        private ExplorerPresenter explorerPresenter;

        /// <summary>
        /// The command that is currently being run.
        /// </summary>
        private RunCommand command = null;

        /// <summary>
        /// Maps a type to an array of fields/properties which are links.
        /// </summary>
        private static Dictionary<Type, MemberInfo[]> links = new Dictionary<Type, MemberInfo[]>();

        /// <summary>
        /// Maps a type to a list of public string properties.
        /// </summary>
        private static Dictionary<Type, PropertyInfo[]> stringProperties = new Dictionary<Type, PropertyInfo[]>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextMenu" /> class.
        /// </summary>
        /// <param name="explorerPresenter">The explorer presenter to work with</param>
        public ContextMenu(ExplorerPresenter explorerPresenter)
        {
            this.explorerPresenter = explorerPresenter;
        }

        /// <summary>
        /// Empty the data store
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Empty the data store",
                     AppliesTo = new Type[] { typeof(DataStore) })]
        public void EmptyDataStore(object sender, EventArgs e)
        {
            explorerPresenter.MainPresenter.ShowWaitCursor(true);
            explorerPresenter.MainPresenter.ShowMessage("Emptying datastore...", Simulation.MessageType.Information);
            try
            {
                storage.Writer.Empty();
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowRightHandPanel();
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
            explorerPresenter.MainPresenter.ShowMessage("Empty datastore complete", Simulation.MessageType.Information);
            explorerPresenter.MainPresenter.ShowWaitCursor(false);
        }

        /// <summary>
        /// Run post simulation models.
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Refresh",
                     AppliesTo = new Type[] { typeof(DataStore) })]
        public void RunPostSimulationModels(object sender, EventArgs e)
        {
            try
            {
                Runner runner = new Runner(explorerPresenter.ApsimXFile, runSimulations: false, wait: false);
                RunCommand command = new RunCommand("Post-simulation tools", runner, explorerPresenter);
                runner.AllSimulationsCompleted += RefreshRightHandPanel;
                command.Do();
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Refresh the right-hand panel of the UI.
        /// </summary>
        /// <param name="sender">Sender object.</param>
        /// <param name="args">Event data.</param>
        private void RefreshRightHandPanel(object sender, object args)
        {
            if (sender is Runner runner)
                runner.AllSimulationsCompleted -= RefreshRightHandPanel;

            // This can be called on the runner thread. The UI actions
            // need to occur on the main thread.
            Gtk.Application.Invoke(delegate
            {
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowRightHandPanel();
            });
        }

        /// <summary>
        /// Clear graph panel cache.
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Clear Cache",
                     AppliesTo = new Type[] { typeof(GraphPanel) })]
        public void ClearGraphPanelCache(object sender, EventArgs e)
        {
            try
            {
                GraphPanel panel = explorerPresenter.CurrentNode as GraphPanel;
                panel.Cache.Clear();
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowRightHandPanel();
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for a User interface "Run APSIM" action
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Run APSIM",
                     AppliesTo = new Type[] { typeof(Simulation),
                                              typeof(Simulations),
                                              typeof(Experiment),
                                              typeof(Folder),
                                              typeof(Morris),
                                              typeof(Sobol),
                                              typeof(Playlist),
                                              typeof(APSIM.Shared.JobRunning.IRunnable)},
                     ShortcutKey = "F5")]
        public void RunAPSIM(object sender, EventArgs e)
        {
            try
            {
                if (!Configuration.Settings.AutoSave || this.explorerPresenter.Save())
                {
                    IModel model = MainMenu.FindRunnable(explorerPresenter.CurrentNode);
                    var runner = new Runner(model, runType: Runner.RunTypeEnum.MultiThreaded, wait: false);
                    this.command = new RunCommand(model.Name, runner, this.explorerPresenter);
                    this.command.Do();
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for the run on cloud action
        /// </summary>        
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Run on cloud",
                     AppliesTo = new Type[] { typeof(Simulation),
                                              typeof(Simulations),
                                              typeof(Experiment),
                                              typeof(Folder)
                                            }
                    )
        ]
        public void RunOnCloud(object sender, EventArgs e)
        {
            try
            {
                object model = explorerPresenter.CurrentNode;
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowInRightHandPanel(model,
                                    "ApsimNG.Resources.Glade.RunOnCloudView.glade",
                                    new RunOnCloudPresenter());
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for generate .apsimx files option.
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Generate .apsimx files",
             AppliesTo = new Type[] {   typeof(Folder),
                                        typeof(Simulations),
                                        typeof(Simulation),
                                        typeof(Experiment),
                                    }
            )
        ]
        public async void OnGenerateApsimXFiles(object sender, EventArgs e)
        {
            try
            {
                await explorerPresenter.GenerateApsimXFiles(explorerPresenter.CurrentNode as IModel);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// User has clicked rename
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Rename", ShortcutKey = "F2", FollowsSeparator = true)]
        public void OnRename(object sender, EventArgs e)
        {
            try
            {
                this.explorerPresenter.Rename();
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// User has clicked Copy
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Copy", ShortcutKey = "Ctrl+C")]
        public void OnCopyClick(object sender, EventArgs e)
        {
            try
            {
                Model model = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as Model;
                if (model != null)
                {
                    // Set the clipboard text.
                    string st = FileFormat.WriteToString(model);
                    this.explorerPresenter.SetClipboardText(st, "_APSIM_MODEL");
                    this.explorerPresenter.SetClipboardText(st, "CLIPBOARD");
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// User has clicked Paste
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Paste", ShortcutKey = "Ctrl+V")]
        public void OnPasteClick(object sender, EventArgs e)
        {
            try
            {
                string internalCBText = this.explorerPresenter.GetClipboardText("_APSIM_MODEL");
                string externalCBText = this.explorerPresenter.GetClipboardText("CLIPBOARD");

                string text = string.IsNullOrEmpty(externalCBText) ? internalCBText : externalCBText;
                if (!string.IsNullOrEmpty(text))
                {

                    IModel currentNode = explorerPresenter.CurrentNode as IModel;
                    if (currentNode != null)
                    {
                        ICommand command = new AddModelCommand(currentNode, text, explorerPresenter.GetNodeDescription);
                        explorerPresenter.CommandHistory.Add(command, true);
                    }
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// User has clicked Duplicate
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Duplicate", ShortcutKey = "Ctrl+d")]
        public void OnDuplicateClick(object sender, EventArgs e)
        {
            try
            {
                Model model = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as Model;
                if (model != null)
                {
                    // Set the clipboard text.
                    string st = FileFormat.WriteToString(model);
                    this.explorerPresenter.SetClipboardText(st, "_APSIM_MODEL");
                    //this.explorerPresenter.SetClipboardText(st, "CLIPBOARD");
                }

                string internalCBText = this.explorerPresenter.GetClipboardText("_APSIM_MODEL");
                //string externalCBText = this.explorerPresenter.GetClipboardText("CLIPBOARD");

                //string text = string.IsNullOrEmpty(externalCBText) ? internalCBText : externalCBText;
                string text = internalCBText;



                if (!string.IsNullOrEmpty(text))
                {

                    IModel currentNode = explorerPresenter.CurrentNode as IModel;
                    IModel parentNode = explorerPresenter.CurrentNode.Parent as IModel;
                    if (parentNode != null)
                    {
                        ICommand command = new AddModelCommand(parentNode, text, explorerPresenter.GetNodeDescription);
                        explorerPresenter.CommandHistory.Add(command, true);
                    }
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
            finally
            {
                this.explorerPresenter.SetClipboardText("", "_APSIM_MODEL");
            }
        }

        /// <summary>
        /// User has clicked Delete
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Delete", ShortcutKey = "Del")]
        public void OnDeleteClick(object sender, EventArgs e)
        {
            try
            {
                IModel model = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as IModel;
                if (model != null && model.GetType().Name != "Simulations")
                    this.explorerPresenter.Delete(model);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Move up
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Move up", ShortcutKey = "Ctrl+Up", FollowsSeparator = true)]
        public void OnMoveUpClick(object sender, EventArgs e)
        {
            try
            {
                IModel model = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as IModel;
                if (model != null && model.GetType().Name != "Simulations")
                    this.explorerPresenter.MoveUp(model);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Move down
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Move down", ShortcutKey = "Ctrl+Down")]
        public void OnMoveDownClick(object sender, EventArgs e)
        {
            try
            {
                IModel model = explorerPresenter.CurrentNode;
                if (model != null && model.GetType().Name != "Simulations")
                    this.explorerPresenter.MoveDown(model);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Move up
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Collapse Children", ShortcutKey = "Ctrl+Left", FollowsSeparator = true)]
        public void OnCollapseChildren(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.CollapseChildren(explorerPresenter.CurrentNodePath);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Move down
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Expand Children", ShortcutKey = "Ctrl+Right")]
        public void OnExpandChildren(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.ExpandChildren(explorerPresenter.CurrentNodePath);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        [ContextMenu(MenuName = "Copy path to node",
                     ShortcutKey = "Ctrl+Shift+C",
                     FollowsSeparator = true)]
        public void CopyPathToNode(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.SetClipboardText(explorerPresenter.CurrentNodePath, "CLIPBOARD");
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        [ContextMenu(MenuName = "Copy manager snippet",
                     FollowsSeparator = true)]
        public void CopyManagerSnippet(object sender, EventArgs e)
        {
            try
            {
                string path = explorerPresenter.CurrentNodePath;
                string modelType = explorerPresenter.CurrentNode.GetType().Name;
                string namesp = explorerPresenter.CurrentNode.GetType().Namespace;

                string snippet = $"using {namesp};{Environment.NewLine}{Environment.NewLine}" +
                                 $"[Link(ByName=true)] private {modelType} {explorerPresenter.CurrentNode.Name};";

                explorerPresenter.SetClipboardText(snippet, "CLIPBOARD");
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        [ContextMenu(MenuName = "Copy manager snippet (full path)",
                     FollowsSeparator = true)]
        public void CopyManagerSnippetFullPath(object sender, EventArgs e)
        {
            try
            {
                string path = explorerPresenter.CurrentNodePath;
                string modelType = explorerPresenter.CurrentNode.GetType().Name;
                string namesp = explorerPresenter.CurrentNode.GetType().Namespace;

                string snippet = $"using {namesp};{Environment.NewLine}{Environment.NewLine}" +
                                 $"[Link(Type=LinkType.Path, Path=\"{path}\")] private {modelType} {explorerPresenter.CurrentNode.Name};";

                explorerPresenter.SetClipboardText(snippet, "CLIPBOARD");
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }


        /// <summary>
        /// A run has completed so re-enable the run button.
        /// </summary>
        /// <returns>True when APSIM is not running</returns>
        public bool RunAPSIMEnabled()
        {
            bool isRunning = this.command != null && this.command.IsRunning;
            if (!isRunning)
            {
                this.command = null;
            }

            return !isRunning;
        }

        /// <summary>
        /// Event handler for a User interface "Check Soil" action
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Check Soil", AppliesTo = new Type[] { typeof(Soil) })]
        public void CheckSoil(object sender, EventArgs e)
        {
            try
            {
                Soil currentSoil = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as Soil;
                if (currentSoil != null)
                {
                    ISummary summary = currentSoil.FindInScope<ISummary>(this.explorerPresenter.CurrentNodePath);
                    currentSoil.CheckWithStandardisation(summary);
                    explorerPresenter.MainPresenter.ShowMessage("Soil water parameters are valid.", Simulation.MessageType.Information);
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for a User interface "Download Soil" action
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Download Soil...", AppliesTo = new Type[] { typeof(Folder), typeof(Zone) })]
        public void DownloadSoil(object sender, EventArgs e)
        {
            try
            {
                object model = explorerPresenter.CurrentNode;
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowInRightHandPanel(model,
                                                       "ApsimNG.Resources.Glade.DownloadSoilView.glade",
                                                       new SoilDownloadPresenter());
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for a User interface "Download Weather" action
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Download Weather...", AppliesTo = new Type[] { typeof(Weather), typeof(Simulation) })]
        public void DownloadWeather(object sender, EventArgs e)
        {
            try
            {
                this.explorerPresenter.DownloadWeather();
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Accept the current test output as the official baseline for future comparison. 
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Accept Tests", AppliesTo = new Type[] { typeof(Tests) })]
        public void AcceptTests(object sender, EventArgs e)
        {
            try
            {
                int result = explorerPresenter.MainPresenter.ShowMsgDialog("You are about to change the officially accepted stats for this model. Are you sure?", "Replace official stats?", Gtk.MessageType.Question, Gtk.ButtonsType.YesNo);
                if ((Gtk.ResponseType)result != Gtk.ResponseType.Yes)
                {
                    return;
                }

                Tests test = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as Tests;
                try
                {
                    test.Test(true);
                }
                catch (ApsimXException ex)
                {
                    explorerPresenter.MainPresenter.ShowError(ex);
                }
                finally
                {
                    this.explorerPresenter.HideRightHandPanel();
                    this.explorerPresenter.ShowRightHandPanel();
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Export the data store to EXCEL format
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Export to EXCEL",
                     AppliesTo = new Type[] { typeof(DataStore) }, FollowsSeparator = true)]
        public async void ExportDataStoreToEXCEL(object sender, EventArgs e)
        {
            List<DataTable> tables = new List<DataTable>();
            try
            {
                string fileName = Path.ChangeExtension(storage.FileName, ".xlsx");

                // Show a message in the GUI.
                explorerPresenter.MainPresenter.ShowMessage("Exporting to excel...", Simulation.MessageType.Information);

                // Show a progress bar - this is currently the only way to get the stop/cancel button to appear.
                explorerPresenter.MainPresenter.ShowProgress(0, true);

                CancellationTokenSource cts = new CancellationTokenSource();

                // Read data from database (in the background).
                Task readTask = Task.Run(() =>
                {
                    ushort i = 0;
                    foreach (string tableName in storage.Reader.TableNames)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        DataTable table = storage.Reader.GetData(tableName);
                        table.TableName = tableName;
                        tables.Add(table);

                        double progress = 0.5 * (i + 1) / storage.Reader.TableNames.Count;
                        explorerPresenter.MainPresenter.ShowProgress(progress);

                        i++;
                    }
                }, cts.Token);

                // Add a handler to the stop button which cancels the excel export..
                EventHandler<EventArgs> stopHandler = (_, __) =>
                {
                    cts.Cancel();
                    explorerPresenter.MainPresenter.HideProgressBar();
                    explorerPresenter.MainPresenter.ShowMessage("Export to excel was cancelled.", Simulation.MessageType.Information, true);
                };
                explorerPresenter.MainPresenter.AddStopHandler(stopHandler);

                try
                {
                    // Wait for data to be read.
                    await readTask;

                    if (readTask.IsFaulted)
                        throw new Exception("Failed to read data from datastore", readTask.Exception);

                    if (readTask.IsCanceled || cts.Token.IsCancellationRequested)
                        return;

                    // Start the excel export as a task.
                    // todo: progress reporting and proper cancellation would be nice.
                    Task exportTask = Task.Run(() => Utility.Excel.WriteToEXCEL(tables.ToArray(), fileName), cts.Token);

                    // Wait for the excel file to be generated.
                    await exportTask;

                    if (exportTask.IsFaulted)
                        throw new Exception($"Failed to export to excel", exportTask.Exception);

                    if (exportTask.IsCanceled || cts.Token.IsCancellationRequested)
                        return;

                    // Show a success message.
                    explorerPresenter.MainPresenter.ShowMessage($"Excel successfully created: {fileName}", Simulation.MessageType.Information);

                    try
                    {
                        // Attempt to open the file - but don't display any errors if it doesn't work.
                        ProcessUtilities.ProcessStart(fileName);
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    // Remove callback from the stop button.
                    explorerPresenter.MainPresenter.RemoveStopHandler(stopHandler);

                    // Remove the progress bar and stop button.
                    explorerPresenter.MainPresenter.HideProgressBar();
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
            finally
            {
                // Disposing of datatables isn't strictly necessary, but if we don't,
                // it could be a while before the memory is reclaimed.
                tables.ForEach(t => t.Dispose());
            }
        }

        /// <summary>
        /// Export output in the data store to text files
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Export output to text files",
                     AppliesTo = new Type[] { typeof(DataStore) })]
        public void ExportOutputToTextFiles(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.MainPresenter.ShowWaitCursor(true);
                Report.WriteAllTables(storage, explorerPresenter.ApsimXFile.FileName);
                string folder = Path.GetDirectoryName(explorerPresenter.ApsimXFile.FileName);
                explorerPresenter.MainPresenter.ShowMessage("Text files have been written to " + folder, Simulation.MessageType.Information);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
            finally
            {
                explorerPresenter.MainPresenter.ShowWaitCursor(false);
            }
        }

        /// <summary>
        /// Export summary in the data store to text files
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Export summary to text files",
                     AppliesTo = new Type[] { typeof(DataStore) })]
        public void ExportSummaryToTextFiles(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.MainPresenter.ShowWaitCursor(true);
                string summaryFleName = Path.ChangeExtension(explorerPresenter.ApsimXFile.FileName, ".sum");
                Summary.WriteSummaryToTextFiles(storage, summaryFleName, Configuration.Settings.DarkTheme);
                explorerPresenter.MainPresenter.ShowMessage("Summary file written: " + summaryFleName, Simulation.MessageType.Information);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
            finally
            {
                explorerPresenter.MainPresenter.ShowWaitCursor(false);
            }
        }

        /// <summary>
        /// Event handler for a Add model action
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Add model...", FollowsSeparator = true, ShortcutKey = "Ctrl+N")]
        public void AddModel(object sender, EventArgs e)
        {
            try
            {
                object model = explorerPresenter.CurrentNode;
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowInRightHandPanel(model,
                                                       "ApsimNG.Resources.Glade.AddModelView.glade",
                                                       new AddModelPresenter());
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for checkbox for 'Include in documentation' menu item.
        /// </summary>
        public bool ShowPageOfGraphsChecked()
        {
            Folder folder = explorerPresenter.CurrentNode as Folder;
            return (folder != null) ? folder.ShowInDocs : false;
        }

        /// <summary>
        /// Event handler for 'Checkpoints' menu item
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Checkpoints", IsToggle = true,
                     AppliesTo = new Type[] { typeof(DataStore) })]
        public void ShowCheckpoints(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.HideRightHandPanel();
                explorerPresenter.ShowInRightHandPanel(explorerPresenter.ApsimXFile,
                                                       "ApsimNG.Resources.Glade.CheckpointView.glade",
                                                       new CheckpointsPresenter());
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for 'Show Model Structure' menu item.
        /// </summary>
        /// <param name="sender">Sender object.</param>
        /// <param name="e">Event arguments.</param>
        [ContextMenu(MenuName = "Show Model Structure",
                     IsToggle = true)]
        public void ShowModelStructure(object sender, EventArgs e)
        {
            try
            {
                IModel model = explorerPresenter.CurrentNode;
                if (model != null)
                {
                    //check if model is from a resource, if so, set all children to read only
                    var childrenFromResource = Resource.Instance.GetChildModelsThatAreFromResource(model);
                    if (childrenFromResource != null)
                    {
                        var hidden = !(sender as Gtk.CheckMenuItem).Active;
                        foreach (IModel child in childrenFromResource)
                        {
                            child.IsHidden = hidden;
                            child.ReadOnly = !hidden;

                            // Recursively set the hidden property.
                            foreach (IModel c in child.FindAllDescendants())
                                c.ReadOnly = !hidden;
                        }

                    }
                    else
                    {
                        foreach (IModel child in model.Children)
                        {
                            child.IsHidden = !child.IsHidden;
                        }
                    }

                    // Delete hidden models from tree control and refresh tree control.
                    foreach (IModel child in model.Children)
                        if (child.IsHidden)
                            explorerPresenter.Tree.Delete(child.FullPath);

                    explorerPresenter.PopulateContextMenu(model.FullPath);
                    explorerPresenter.RefreshNode(model);
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        public bool ShowModelStructureChecked()
        {
            IModel model = explorerPresenter.CurrentNode as IModel;
            if (model == null)
                return false;
            if (model.Children.Count < 1)
                return true;
            return !model.Children[0].IsHidden;
        }

        /// <summary>
        /// Event handler for 'Enabled' menu item.
        /// </summary>
        [ContextMenu(MenuName = "Enabled", IsToggle = true)]
        public void Enabled(object sender, EventArgs e)
        {
            try
            {
                IModel model = explorerPresenter.CurrentNode as IModel;
                if (model != null)
                {
                    // Toggle the enabled property on the model, and change the enabled property
                    // on all descendants to the new value of the model's enabled property.
                    List<ChangeProperty.Property> changes = new List<ChangeProperty.Property>();
                    changes.Add(new ChangeProperty.Property(model, nameof(model.Enabled), !model.Enabled));
                    foreach (IModel child in model.FindAllDescendants())
                        changes.Add(new ChangeProperty.Property(child, nameof(model.Enabled), !model.Enabled));

                    ChangeProperty command = new ChangeProperty(changes);
                    explorerPresenter.CommandHistory.Add(command);

                    explorerPresenter.PopulateContextMenu(explorerPresenter.CurrentNodePath);
                    explorerPresenter.RefreshNode(model);
                    explorerPresenter.HideRightHandPanel();
                    explorerPresenter.ShowRightHandPanel();

                    if (model.Enabled)
                    {
                        if (model is Manager manager)
                            manager.RebuildScriptModel();
                        foreach (Manager m in model.FindAllDescendants<Manager>())
                            m.RebuildScriptModel();
                    }
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        /// <summary>
        /// Event handler for checkbox for 'Enabled' menu item.
        /// </summary>
        public bool EnabledChecked()
        {
            IModel model = explorerPresenter.CurrentNode as IModel;
            return model.Enabled;
        }

        /// <summary>
        /// Ensure that the selected simulation will reset its state correctly
        /// when used by an apsim server.
        /// </summary>
        /// <param name="sender">Sender object.</param>
        /// <param name="args">Event arguments.</param>
        [ContextMenu(MenuName = "Verify Server Compatibility", FollowsSeparator = true)]
        public void CheckServerCompatibility(object sender, EventArgs args)
        {
            try
            {
                SimulationChecker checker = new SimulationChecker(explorerPresenter.CurrentNode, false);
                RunCommand command = new RunCommand("State validation", checker, explorerPresenter);
                command.Do();
            }
            catch (Exception error)
            {
                explorerPresenter.MainPresenter.ShowError(error);
            }
        }

        /// <summary>
        /// Event handler for a User interface "Create documentation from simulations" action
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Create documentation",
                     FollowsSeparator = true)]
        public void CreateFileDocumentation(object sender, EventArgs e)
        {
            try
            {
                explorerPresenter.MainPresenter.ShowMessage("Creating documentation...", Simulation.MessageType.Information);
                explorerPresenter.MainPresenter.ShowWaitCursor(true);

                IModel currentN = explorerPresenter.CurrentNode;
                IModel modelToDocument = currentN;
                explorerPresenter.ApsimXFile.Links.Resolve(modelToDocument, true, true, false);

                string modelTypeName = String.Empty;
                if (modelToDocument is Models.PMF.Plant)
                    modelTypeName = modelToDocument.Name;
                else if (modelToDocument is Simulations)
                {
                    var simpleFileName = Path.GetFileNameWithoutExtension((modelToDocument as Simulations).FileName);
                    modelTypeName = simpleFileName;
                }
                else modelTypeName = modelToDocument.GetType().Name;

                string fullDocFileName = Directory.GetParent(explorerPresenter.ApsimXFile.FileName).ToString()
                    + $"{Path.DirectorySeparatorChar}{modelTypeName}.html";

                string html = WebDocs.Generate(modelToDocument);

                File.WriteAllText(fullDocFileName, html);

                explorerPresenter.MainPresenter.ShowMessage($"Written {fullDocFileName}", Simulation.MessageType.Information);

                // Open the document.
                ProcessUtilities.ProcessStart(fullDocFileName);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
            finally
            {
                explorerPresenter.MainPresenter.ShowWaitCursor(false);
            }
        }

        /// <summary>
        /// Event handler for 'Include in documentation'
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Show page of graphs in documentation", IsToggle = true,
                     AppliesTo = new Type[] { typeof(Folder) },
                     FollowsSeparator = true)]
        public void ShowPageOfGraphs(object sender, EventArgs e)
        {
            try
            {
                Folder folder = explorerPresenter.CurrentNode as Folder;
                folder.ShowInDocs = !folder.ShowInDocs;
                explorerPresenter.PopulateContextMenu(explorerPresenter.CurrentNodePath);
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        [ContextMenu(MenuName = "Find All References",
                     ShortcutKey = "Shift + F12",
                     AppliesTo = new[] { typeof(IFunction) })]
        public void OnFindReferences(object sender, EventArgs e)
        {
            try
            {
                IModel model = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as IModel;
                if (model != null)
                {
                    string modelPath = model.FullPath;
                    StringBuilder message = new StringBuilder($"Searching for references to model {model.FullPath}...");
                    List<Reference> references = new List<Reference>();
                    message.AppendLine();
                    message.AppendLine();
                    Stopwatch timer = Stopwatch.StartNew();

                    foreach (VariableReference reference in model.FindAllInScope<VariableReference>())
                    {
                        try
                        {
                            if (reference.FindByPath(reference.VariableName.Replace(".Value()", ""))?.Value == model)
                                references.Add(new Reference() { Member = typeof(VariableReference).GetProperty("VariableName"), Model = reference, Target = model });
                        }
                        catch
                        {

                        }
                    }
                    timer.Stop();
                    message.AppendLine();
                    message.AppendLine($"Finished. Elapsed time: {timer.Elapsed.TotalSeconds.ToString("#.00")} seconds");
                    explorerPresenter.MainPresenter.ShowMessage(message.ToString(), Simulation.MessageType.Information);
                    var dialog = new Utility.FindAllReferencesDialog(model, references, explorerPresenter);
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        [ContextMenu(MenuName = "Compile Script",
                     ShortcutKey = "Ctrl+T",
                     FollowsSeparator = true,
                     AppliesTo = new[] { typeof(Manager) })]
        public void OnCompileScript(object sender, EventArgs e)
        {
            try
            {
                Manager model = this.explorerPresenter.ApsimXFile.FindByPath(this.explorerPresenter.CurrentNodePath, LocatorFlags.ModelsOnly)?.Value as Manager;
                if (model != null)
                {
                    model.RebuildScriptModel();
                    explorerPresenter.MainPresenter.ShowMessage("\"" + model.Name + "\" compiled successfully", Simulation.MessageType.Information);
                }
            }
            catch (Exception err)
            {
                explorerPresenter.MainPresenter.ShowError(err);
            }
        }

        //This menu item is dynamically added by ExplorerPresented based on how many 
        //Playlists exist within the file.
        [ContextMenu(MenuName = "Playlist",
                     ShortcutKey = "",
                     FollowsSeparator = true,
                     AppliesTo = new[] { typeof(Simulation),
                                         typeof(Simulations),
                                         typeof(Experiment),
                                         typeof(Folder) })]
        public void OnAddToPlaylist(object sender, EventArgs e)
        {
            List<string> namesToAdd = new List<string>();
            IModel model = explorerPresenter.CurrentNode;

            if (model is Simulations || model is Folder)
            {
                IEnumerable<Simulation> sims = (model as Model).FindAllDescendants<Simulation>();
                foreach (Simulation sim in sims)
                    namesToAdd.Add(sim.Name);

                IEnumerable<Experiment> exps = (model as Model).FindAllDescendants<Experiment>();
                foreach (Experiment exp in exps)
                    namesToAdd.Add(exp.Name);
            }
            else if (model is Simulation || model is Experiment)
            {
                namesToAdd.Add((model as Model).Name);
            }

            //This digs through the menu item that sends the event to see what the text was on the button
            //This is not good code and will break if the GUI changes
            MenuItem menuItem = sender as MenuItem;
            Box hBox = menuItem.Children[0] as Box;
            Label label = hBox.Children[1] as Label;
            string itemText = label.Text;
            string playlistName = itemText.Replace("Add to", "").Trim();

            Playlist playlist = explorerPresenter.ApsimXFile.FindDescendant(playlistName) as Playlist;
            if (playlist != null)
            {
                playlist.AddSimulationNamesToList(namesToAdd.ToArray());
            }
            else
            {
                string outputNames = "";
                foreach (string simName in namesToAdd)
                    outputNames += simName + ", ";
                explorerPresenter.MainPresenter.ShowMessage($"Could not add {outputNames} to Playlist called '{playlistName}'", Simulation.MessageType.Warning);
            }
        }

        /// <summary>
        /// Reset axes of a graph.
        /// </summary>
        /// <param name="sender">Sender of the event</param>
        /// <param name="e">Event arguments</param>
        [ContextMenu(MenuName = "Reset Graph Axes",
                     AppliesTo = new Type[] { typeof(Models.Graph) })]
        public void ResetGraphAxes(object sender, EventArgs e)
        {
            Models.Graph selectedGraph = this.explorerPresenter.CurrentNode as Models.Graph;
            if (selectedGraph.Axis.Count() > 0)
            {
                foreach (var axis in selectedGraph.Axis)
                {
                    axis.Maximum = null;
                    axis.Minimum = null;
                }
                // Refreshes the view with new resets.
                this.explorerPresenter.HideRightHandPanel();
                this.explorerPresenter.ShowRightHandPanel();
                this.explorerPresenter.MainPresenter.ShowMessage($"{selectedGraph.Name}: axis minimum and maximum reset.", Simulation.MessageType.Information);
            }


        }

    }
}