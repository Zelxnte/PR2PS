﻿using PR2PS.DataAccess.Entities;
using PR2PS.LevelImporter.Core;
using PR2PS.LevelImporter.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using static PR2PS.LevelImporter.Core.Enums;

namespace PR2PS.LevelImporter
{
    public partial class MainForm : Form
    {
        #region Fields.

        private const String INFO =
            "Use this application to import existing PR2 levels into PR2PS database. Instructions to use:\n\n"
            + "1.) Attach PR2PS database using Connect button.\n\n"
            + "2.) Use the Assign User tab to search the databse for existing user:\n"
            + "- Levels added to the pipeline will have that user assigned to them\n"
            + "- User can be changed before adding another level to the pipeline\n\n"
            + "3.) Use relevant tabs to search and add levels to the pipeline:\n"
            + "- Import From File - Used to import downloaded levels\n"
            + "- Import By Id - Used to import live levels specified by level id\n"
            + "- Import By Search - Used to search and import live levels according to given criteria\n\n"
            + "4.) Click on Run Import Procedure to initiate the import process.";

        private DatabaseConnector database;
        private UserModel selectedUser;
        private LevelSearcher levelSearcher;
        private LevelConvertor levelConvertor;
        private Boolean preventClosing;

        #endregion

        #region Constructor.

        public MainForm()
        {
            InitializeComponent();
        }

        #endregion

        #region Form event handlers.

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            this.Log("Initializing...");

            this.database = new DatabaseConnector();
            this.levelSearcher = new LevelSearcher();
            this.levelConvertor = new LevelConvertor();

            this.comboBoxSearchUserMode.SelectedIndex = 0;
            this.comboBoxSearchBy.SelectedIndex = 0;
            this.comboBoxSortBy.SelectedIndex = 0;
            this.comboBoxSortOrder.SelectedIndex = 0;

            this.btnConnectMainDb.Tag = AttachType.Main;
            this.btnConnectLevelsDb.Tag = AttachType.Levels;

            this.listBoxLocalLevels.DataSource = new List<String>();
            this.pipelineListBox.DataSource = new List<LevelModel>();

            this.MainForm_Resize(null, null);

            this.Log("Ready.");
        }

        private void MainForm_Resize(Object sender, EventArgs e)
        {
            if (this.WindowState != FormWindowState.Minimized)
            {
                this.splitContainerMain.SplitterDistance = this.splitContainerMain.Width - 250;
                this.splitContainerSub.SplitterDistance = this.splitContainerSub.Height - 150;
            }
        }

        private void MainForm_FormClosing(Object sender, FormClosingEventArgs e)
        {
            if (this.preventClosing && e.CloseReason == CloseReason.UserClosing)
            {
                this.Log("Please wait until the import procedure completes.", Color.Orange);
                e.Cancel = true;
                return;
            }

            this.Log("Closing and cleaning up...");

            this.database.Dispose();
            this.levelSearcher.Dispose();
            this.levelConvertor.Dispose();
        }

        #endregion

        #region Menu buttons handlers.

        private void btnAttachDb_Click(Object sender, EventArgs e)
        {
            ToolStripButton btn = sender as ToolStripButton;
            if (btn == null)
            {
                return;
            }

            AttachType? attachType = btn.Tag as AttachType?;
            if (!attachType.HasValue)
            {
                return;
            }

            using (OpenFileDialog fileDialog = new OpenFileDialog())
            {
                fileDialog.Filter = "SQLite Database File |*.sqlite";

                if (fileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    Log(String.Format("Attempting to attach database file '{0}'.", fileDialog.FileName));

                    try
                    {
                        this.database.Attach(fileDialog.FileName, attachType.Value);
                        btn.Enabled = false;

                        Log("Database attached successfully!", Color.LimeGreen);
                    }
                    catch (DbValidationException ex)
                    {
                        Log(String.Concat("Attached database is invalid: ", ex.Message), Color.Red);
                    }
                    catch (Exception ex)
                    {
                        Log(String.Concat("Error occured while attaching database:\n", ex), Color.Red);
                    }
                }
            }
        }

        private void infoBtn_Click(Object sender, EventArgs e)
        {
            MessageBox.Show(this, INFO, "Information", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void exitBtn_Click(Object sender, EventArgs e)
        {
            Application.Exit();
        }

        #endregion

        #region Search user tab handlers.

        private void btnSearchUser_Click(Object sender, EventArgs e)
        {
            try
            {
                String term = this.textBoxSearchUserTerm.Text;
                if (String.IsNullOrEmpty(term))
                {
                    Log("Please enter search term into the text box.", Color.Orange);
                    return;
                }

                if (!this.database.IsMainDbAttached)
                {
                    Log("You need to attach the main database firstly.", Color.Orange);
                    return;
                }

                Log("Searching...");

                IEnumerable<UserModel> results = this.database.FindUsers(term, (UserSearchMode)this.comboBoxSearchUserMode.SelectedIndex);

                Log("Search completed.");

                this.dataGridViewUserResuts.DataSource = results;
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while searching database for users:\n", ex), Color.Red);
            }
        }

        private void btnAssignUser_Click(Object sender, EventArgs e)
        {
            try
            {
                UserModel selected = (UserModel)this.dataGridViewUserResuts.CurrentRow?.DataBoundItem;
                if (selected == null)
                {
                    Log("You have to select user from the grid below.", Color.Orange);
                    return;
                }

                this.selectedUser = selected;

                Log(String.Format("User {0} selected.", selected.ToString()));
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while selecting user:\n", ex), Color.Red);
            }
        }

        #endregion

        #region Import from file tab handlers.

        private void btnBrowse_Click(Object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog fileDialog = new OpenFileDialog { Multiselect = true })
                {
                    fileDialog.Filter = "All Files |*.*";

                    if (fileDialog.ShowDialog(this) == DialogResult.OK)
                    {
                        List<String> dataSource = (List<String>)this.listBoxLocalLevels.DataSource;
                        dataSource.AddRange(fileDialog.FileNames);

                        this.RebindListBoxDataSource(this.listBoxLocalLevels, dataSource, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while loading levels:\n", ex), Color.Red);
            }
        }

        private void btnAddLocalToPipeline_Click(Object sender, EventArgs e)
        {
            try
            {
                if (this.selectedUser == null)
                {
                    Log("You have to select the user who will become the owner of selected levels.", Color.Orange);
                    return;
                }

                String[] selected = this.listBoxLocalLevels.SelectedItems.Cast<String>().ToArray();
                if (!selected.Any())
                {
                    Log("You have to select level(s) from the list below.", Color.Orange);
                    return;
                }

                List<String> dataSource = (List<String>)this.listBoxLocalLevels.DataSource;
                dataSource = dataSource.Except(selected).ToList();
                this.RebindListBoxDataSource(this.listBoxLocalLevels, dataSource, null);

                this.AddToPipeline(selected.Select(f => new LevelModel(this.selectedUser, f)));

                Log(String.Format("Successfully added {0} item(s) to the pipeline.", selected.Length));
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while adding levels to the pipeline:\n", ex), Color.Red);
            }
        }

        #endregion

        #region Import by id and version tab handlers.

        private void btnAddExactToPipeLine_Click(Object sender, EventArgs e)
        {
            if (this.selectedUser == null)
            {
                Log("You have to select the user who will become the owner of given level.", Color.Orange);
                return;
            }

            if (!Int64.TryParse(this.textBoxLevelId.Text, out Int64 dummyLevelId))
            {
                Log("Level id needs to be a number.", Color.Orange);
                return;
            }

            if (!String.IsNullOrEmpty(this.textBoxLevelVersion.Text) && !Int64.TryParse(this.textBoxLevelVersion.Text, out Int64 dummyVersion))
            {
                Log("Version (if specified) needs to be a number.", Color.Orange);
                return;
            }

            this.AddToPipeline(new[] { new LevelModel(this.selectedUser, this.textBoxLevelId.Text, this.textBoxLevelVersion.Text) });

            Log("Successfully added 1 item to the pipeline.");
        }

        #endregion

        #region Import by search tab hanlders.

        private async void btnSearchLevels_Click(Object sender, EventArgs e)
        {
            try
            {
                if (this.textBoxLevelsSearchTerm.TextLength < 1)
                {
                    Log("Search term needs to be at least 1 character.", Color.Orange);
                    return;
                }

                if (this.levelSearcher.IsBusy)
                {
                    Log("Search is already in progress.", Color.Orange);
                    return;
                }

                Log("Searching, please wait...");

                List<LevelResult> results = await this.levelSearcher.DoSearch(
                    this.textBoxLevelsSearchTerm.Text,
                    this.comboBoxSearchBy.SelectedItem.ToString(),
                    this.comboBoxSortBy.SelectedItem.ToString(),
                    this.comboBoxSortOrder.SelectedItem.ToString(),
                    this.numericPage.Value.ToString());

                this.dataGridViewLevelResults.DataSource = results;

                Log("Search completed.");
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while performing search:\n", ex), Color.Red);
            }
        }

        private void btnAddSearchResultsToPipeline_Click(Object sender, EventArgs e)
        {
            try
            {
                if (this.selectedUser == null)
                {
                    Log("You have to select the user who will become the owner of given level.", Color.Orange);
                    return;
                }

                LevelResult selected = (LevelResult)this.dataGridViewLevelResults.CurrentRow?.DataBoundItem;
                if (selected == null)
                {
                    Log("You have to select level from the grid below.", Color.Orange);
                    return;
                }

                this.AddToPipeline(new[] { new LevelModel(this.selectedUser, selected.LevelId, selected.Version) });

                Log("Successfully added 1 item to the pipeline.");
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while adding item to pipeline:\n", ex), Color.Red);
            }
        }

        #endregion

        #region Pipeline handlers.

        private void delFromPipelineBtn_Click(Object sender, EventArgs e)
        {
            try
            {
                LevelModel[] selected = this.pipelineListBox.SelectedItems.Cast<LevelModel>().ToArray();
                if (!selected.Any())
                {
                    Log("You have to select level(s) from the pipeline.", Color.Orange);
                    return;
                }

                if (this.preventClosing)
                {
                    Log("It is not possible to delete level(s) from the pipeline at the moment.", Color.Orange);
                    return;
                }

                List<LevelModel> dataSource = (List<LevelModel>)this.pipelineListBox.DataSource;
                dataSource = dataSource.Except(selected).ToList();
                this.RebindListBoxDataSource(this.pipelineListBox, dataSource, "Render");

                Log(String.Format("Successfully removed {0} item(s) from the pipeline.", selected.Length));
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while removing levels from the pipeline:\n", ex), Color.Red);
            }
        }

        private async void runBtn_Click(Object sender, EventArgs e)
        {
            try
            {
                if (!this.database.IsLevelsDbAttached)
                {
                    Log("You need to attach the levels database firstly.", Color.Orange);
                    return;
                }

                List<LevelModel> dataSource = (List<LevelModel>)this.pipelineListBox.DataSource;
                if (!dataSource.Any())
                {
                    Log("There are no levels in the pipeline to import.", Color.Orange);
                    return;
                }

                Log("Initiating import procedure...");

                this.preventClosing = true;
                this.ToggleState(false);

                List<LevelModel> failed = new List<LevelModel>();

                Action<ImportProgress> progressHandler = (p =>
                {
                    if (p.ProgressType == ProgressType.Info)
                    {
                        Log(p.Message);
                    }
                    else
                    {
                        failed.Add(p.LevelModel);
                        Log(p.Message, p.ProgressType == ProgressType.Warning ? Color.Orange : Color.Red);
                    }
                });

                // Short story here. Initially I wanted to take advantage of the IProgress interface to nicely report progress
                // from the async method. The problem is that due to synchronization context, the reported progress was being
                // delayed. Therefore, I am passing the action.

                List<Level> levels = await this.levelConvertor.GetAndConvert(dataSource, progressHandler);

                if (levels.Count == dataSource.Count)
                {
                    Log(String.Format("Successfully materialized all {0} levels!", levels.Count));
                }
                else
                {
                    Log(String.Format("Materialized {0} levels out of {1}.", levels.Count, dataSource.Count), Color.Orange);
                }

                if (levels.Any())
                {
                    Log("Proceeding with importing levels to the database...");

                    await this.database.ImportLevels(levels);

                    this.RebindListBoxDataSource(this.pipelineListBox, failed, "Render");

                    Log(String.Format("Successfully imported {0} levels into database!", levels.Count), Color.LimeGreen);
                }
                else
                {
                    Log("Import procedure has finished. No levels were imported.", Color.Orange);
                }

                this.ToggleState(true);
            }
            catch (Exception ex)
            {
                Log(String.Concat("Error occured while importing levels:\n", ex), Color.Red);
            }
            finally
            {
                this.preventClosing = false;
            }
        }

        #endregion

        #region Helpers.

        private void AddToPipeline(IEnumerable<LevelModel> levels)
        {
            if (levels == null || !levels.Any())
            {
                return;
            }

            List<LevelModel> dataSource = (List<LevelModel>)this.pipelineListBox.DataSource;
            dataSource.AddRange(levels);

            this.RebindListBoxDataSource(this.pipelineListBox, dataSource, "Render");
        }

        private void RebindListBoxDataSource(ListBox listBox, Object dataSource, String displayMember)
        {
            listBox.DataSource = null; // We have to do this for some strange reason...
            listBox.DataSource = dataSource;
            listBox.DisplayMember = displayMember;
        }

        private void ToggleState(Boolean enabled)
        {
            this.exitBtn.Enabled = enabled;
            this.tabControl.Enabled = enabled;

            this.pipelineListBox.Enabled = enabled;
            this.delFromPipelineBtn.Enabled = enabled;
            this.runBtn.Enabled = enabled;
        }

        #endregion

        #region Logging methods.

        private void Log(String message)
        {
            this.logTextBox.Write(message);
        }

        private void Log(String message, Color color)
        {
            this.logTextBox.Write(message, color);
        }

        #endregion
    }
}
