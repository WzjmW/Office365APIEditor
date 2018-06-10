﻿// Copyright (c) Microsoft. All rights reserved. 
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information. 

using Microsoft.Identity.Client;
using Microsoft.Office365.OutlookServices;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.OData.Client;

namespace Office365APIEditor
{
    public partial class MailboxViewerForm : Form
    {
        PublicClientApplication pca;
        AuthenticationResult ar;
        OutlookServicesClient client;
        ViewerHelper.ViewerHelper viewerHelper;

        // Current user's info.
        Microsoft.Identity.Client.IUser currentUser;

        bool doubleClicked = false;

        bool expandingNodeHasDummyNode = false;

        public bool requestFormOpened = false;

        public MailboxViewerForm()
        {
            InitializeComponent();
        }

        private void MailboxViewerForm_Load(object sender, System.EventArgs e)
        {
            closeSessionToolStripMenuItem.Enabled = false;

            // Change window title
            string windowTitle = "Office365APIEditor - " + Application.ProductVersion;
#if DEBUG
            windowTitle += " [DEBUG]";
#endif
            Text = windowTitle + " - Mailbox Viewer";
        }

        private bool Prepare()
        {
            // Use MSAL and acquire access token.

            AcquireViewerTokenForm acuireViewerTokenForm = new AcquireViewerTokenForm();
            if (acuireViewerTokenForm.ShowDialog(out pca, out ar) != DialogResult.OK)
            {
                return false;
            }

            string token = ar.AccessToken;
            currentUser = ar.User;

            try
            {
                client = new OutlookServicesClient(new Uri("https://outlook.office.com/api/v2.0"),
                    () =>
                    {
                        return Task.Run(() =>
                        {
                            return token;
                        });
                    });
                
                client.Context.SendingRequest2 += new EventHandler<SendingRequest2EventArgs>(
                    (eventSender, eventArgs) => Util.InsertHeaders(eventSender, eventArgs, currentUser.DisplayableId));

                viewerHelper = new ViewerHelper.ViewerHelper(pca, currentUser);

                // Get the root folder.
                PrepareMsgFolderRoot();
                
                // Get CalendarFolders (Calendars)
                PrepareCalendarFolders();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("ERROR retrieving folders: {0}", ex.Message, "Office365APIEditor"));

                return false;
            }
        }

        private async void PrepareMsgFolderRoot()
        {
            // Get MsgFolderRoot and add it to the tree.

            try
            {
                var msgFolderRoot = await viewerHelper.GetMsgFolderRootAsync();

                TreeNode node = new TreeNode("MsgFolderRoot")
                {
                    Tag = new FolderInfo() { ID = msgFolderRoot.Id, Type = FolderContentType.MsgFolderRoot, Expanded = false },
                    ContextMenuStrip = contextMenuStrip_FolderTreeNode
                };
                node.Nodes.Add(new TreeNode()); // Add a dummy node.

                if (treeView_Mailbox.InvokeRequired)
                {
                    treeView_Mailbox.Invoke(new MethodInvoker(delegate { treeView_Mailbox.Nodes.Add(node); }));
                }
                else
                {
                    treeView_Mailbox.Nodes.Add(node);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Office365APIEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void PrepareChildMailFolders(string FolderId, TreeNode FolderNode)
        {
            // Get all child MailFolder of specified folder, and add them to the tree.

            var childMailFolders = await viewerHelper.GetAllChildMailFolderAsync(FolderId);

            foreach (var folder in childMailFolders)
            {
                TreeNode node = new TreeNode(folder.DisplayName)
                {
                    Tag = new FolderInfo() { ID = folder.Id, Type = FolderContentType.Message, Expanded = false },
                    ContextMenuStrip = contextMenuStrip_FolderTreeNode
                };

                if (folder.ChildFolderCount >= 1)
                {
                    node.Nodes.Add(new TreeNode()); // Add a dummy node.
                }

                if (treeView_Mailbox.InvokeRequired)
                {
                    treeView_Mailbox.Invoke(new MethodInvoker(delegate {
                        FolderNode.Nodes.Add(node);
                        if (expandingNodeHasDummyNode)
                        {
                            // Remove a dummy node.
                            FolderNode.Nodes[0].Remove();
                            expandingNodeHasDummyNode = false;
                        }
                    }));
                }
                else
                {
                    FolderNode.Nodes.Add(node);
                    if (expandingNodeHasDummyNode)
                    {
                        // Remove a dummy node.
                        FolderNode.Nodes[0].Remove();
                        expandingNodeHasDummyNode = false;
                    }
                }
            }
        }
        
        private async void PrepareChildContactFolders(string FolderId, TreeNode FolderNode)
        {
            // Get all child contact folders of specified folder, and add them to the tree.

            var childContactFolders = await viewerHelper.GetAllChildContactFolderAsync(FolderId);

            if (childContactFolders.Count == 0)
            {
                if (expandingNodeHasDummyNode)
                {
                    // Remove a dummy node.
                    FolderNode.Nodes[0].Remove();
                    expandingNodeHasDummyNode = false;
                }

                return;
            }

            foreach (var folder in childContactFolders)
            {
                TreeNode node = new TreeNode(folder.DisplayName)
                {
                    Tag = new FolderInfo() { ID = folder.Id, Type = FolderContentType.Contact, Expanded = false },
                    ContextMenuStrip = contextMenuStrip_FolderTreeNode
                };
                node.Nodes.Add(new TreeNode()); // Add a dummy node.

                if (treeView_Mailbox.InvokeRequired)
                {
                    treeView_Mailbox.Invoke(new MethodInvoker(delegate {
                        FolderNode.Nodes.Add(node);
                        if (expandingNodeHasDummyNode)
                        {
                            // Remove a dummy node.
                            FolderNode.Nodes[0].Remove();
                            expandingNodeHasDummyNode = false;
                        }
                    }));
                }
                else
                {
                    FolderNode.Nodes.Add(node);
                    if (expandingNodeHasDummyNode)
                    {
                        // Remove a dummy node.
                        FolderNode.Nodes[0].Remove();
                        expandingNodeHasDummyNode = false;
                    }
                }
            }
        }

        private async void PrepareCalendarFolders()
        {
            // Calendar object has no ParentID or ChildFolders.
            // So we use DummyCalendarRoot node as a parent folder of calendar folders.
            // We can get all calendar folders in user's mailbox at once.

            // Make a dummy node.
            TreeNode dummyCalendarRootNode = new TreeNode("Calendar Folders (Dummy Folder)")
            {
                Tag = new FolderInfo() { ID = "", Type = FolderContentType.DummyCalendarRoot },
                ContextMenuStrip = null
            };

            if (treeView_Mailbox.InvokeRequired)
            {
                treeView_Mailbox.Invoke(new MethodInvoker(delegate { treeView_Mailbox.Nodes.Add(dummyCalendarRootNode); }));
            }
            else
            {
                treeView_Mailbox.Nodes.Add(dummyCalendarRootNode);
            }

            try
            {
                var calendars = await viewerHelper.GetCalendarFoldersAsync();

                foreach (var calendar in calendars)
                {
                    TreeNode node = new TreeNode(calendar.Name)
                    {
                        Tag = new FolderInfo() { ID = calendar.Id, Type = FolderContentType.Calendar },
                        ContextMenuStrip = contextMenuStrip_FolderTreeNode
                    };

                    if (treeView_Mailbox.InvokeRequired)
                    {
                        treeView_Mailbox.Invoke(new MethodInvoker(delegate { dummyCalendarRootNode.Nodes.Add(node); }));
                    }
                    else
                    {
                        dummyCalendarRootNode.Nodes.Add(node);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Office365APIEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void treeView_Mailbox_AfterSelect(object sender, TreeViewEventArgs e)
        {
            // Get new OutlookServiceClient.
            client = await Util.GetOutlookServicesClientAsync(pca, currentUser);
            if (client == null)
            {
                MessageBox.Show("Acquiring access token failed.", "Office365APIEditor", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Reset DataGrid.
            for (int i = dataGridView_FolderProps.Rows.Count - 1; i >= 0; i--)
            {
                dataGridView_FolderProps.Rows.RemoveAt(i);
            }

            for (int i = dataGridView_FolderProps.Columns.Count - 1; i >= 0; i--)
            {
                dataGridView_FolderProps.Columns.RemoveAt(i);
            }

            if (treeView_Mailbox.InvokeRequired)
            {
                // Another thread is working. We should do nothing.
                return;
            }

            // Get folder props.    

            FolderInfo info = (FolderInfo)treeView_Mailbox.SelectedNode.Tag;

            switch (info.Type)
            {
                case FolderContentType.Message:
                case FolderContentType.MsgFolderRoot:
                    GetMessageFolderProps(info.ID, treeView_Mailbox.SelectedNode.Text);
                    break;
                case FolderContentType.Contact:
                    GetContactFolderProps(info.ID, treeView_Mailbox.SelectedNode.Text);
                    break;
                case FolderContentType.Calendar:
                    GetCalendarFolderProps(info.ID, treeView_Mailbox.SelectedNode.Text);
                    break;
                default:
                    break;
            }
        }

        private async void GetMessageFolderProps(string FolderId, string FolderDisplayName)
        {
            // Get the folder.
            IMailFolder mailFolderResults = new MailFolder();

            try
            {
                mailFolderResults = await client.Me.MailFolders[FolderId].ExecuteAsync();
            }
            catch (Microsoft.OData.Core.ODataErrorException ex)
            {
                // We know that we can't get RSS Feeds folder.
                // But we can get the folder using DisplayName Filter.

                if (ex.Error.ErrorCode == "ErrorItemNotFound")
                {
                    var tempResults = await client.Me.MailFolders
                        .Where(m => m.DisplayName == FolderDisplayName)
                        .Take(2)
                        .ExecuteAsync();

                    if (tempResults.CurrentPage.Count != 1)
                    {
                        // We have to get a unique folder.
                        MessageBox.Show(ex.Message, "Office365APIEditor");
                        return;
                    }

                    mailFolderResults = tempResults.CurrentPage[0];
                }
                else
                {
                    MessageBox.Show(ex.Error.ErrorCode);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Office365APIEditor");
                return;
            }

            // Add columns.
            dataGridView_FolderProps.Columns.Add("Property", "Property");
            dataGridView_FolderProps.Columns.Add("Value", "Value");
            dataGridView_FolderProps.Columns.Add("Type", "Type");

            // Add rows.

            DataGridViewRow propChildFolderCount = new DataGridViewRow();
            propChildFolderCount.CreateCells(dataGridView_FolderProps, new object[] { "ChildFolderCount", mailFolderResults.ChildFolderCount.Value, mailFolderResults.ChildFolderCount.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propChildFolderCount);

            DataGridViewRow propDisplayName = new DataGridViewRow();
            propDisplayName.CreateCells(dataGridView_FolderProps, new object[] { "DisplayName", mailFolderResults.DisplayName, mailFolderResults.DisplayName.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propDisplayName);

            DataGridViewRow propId = new DataGridViewRow();
            propId.CreateCells(dataGridView_FolderProps, new object[] { "Id", mailFolderResults.Id, mailFolderResults.Id.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propId);

            DataGridViewRow propParentFolderId = new DataGridViewRow();
            propParentFolderId.CreateCells(dataGridView_FolderProps, new object[] { "ParentFolderId", mailFolderResults.ParentFolderId, mailFolderResults.ParentFolderId.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propParentFolderId);

            DataGridViewRow propTotalItemCount = new DataGridViewRow();
            propTotalItemCount.CreateCells(dataGridView_FolderProps, new object[] { "TotalItemCount", mailFolderResults.TotalItemCount, mailFolderResults.TotalItemCount.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propTotalItemCount);

            DataGridViewRow propUnreadItemCount = new DataGridViewRow();
            propUnreadItemCount.CreateCells(dataGridView_FolderProps, new object[] { "UnreadItemCount", mailFolderResults.UnreadItemCount, mailFolderResults.UnreadItemCount.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propUnreadItemCount);
        }

        private async void GetContactFolderProps(string FolderId, string FolderDisplayName)
        {
            // Get the folder.
            IContactFolder contactFolderResults = new ContactFolder();

            try
            {
                contactFolderResults = await client.Me.ContactFolders[FolderId].ExecuteAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Office365APIEditor");
                return;
            }


            // Add columns.
            dataGridView_FolderProps.Columns.Add("Property", "Property");
            dataGridView_FolderProps.Columns.Add("Value", "Value");
            dataGridView_FolderProps.Columns.Add("Type", "Type");

            // Add rows.

            DataGridViewRow propDisplayName = new DataGridViewRow();
            propDisplayName.CreateCells(dataGridView_FolderProps, new object[] { "DisplayName", contactFolderResults.DisplayName, contactFolderResults.DisplayName.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propDisplayName);

            DataGridViewRow propId = new DataGridViewRow();
            propId.CreateCells(dataGridView_FolderProps, new object[] { "Id", contactFolderResults.Id, contactFolderResults.Id.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propId);

            DataGridViewRow propParentFolderId = new DataGridViewRow();
            propParentFolderId.CreateCells(dataGridView_FolderProps, new object[] { "ParentFolderId", contactFolderResults.ParentFolderId, contactFolderResults.ParentFolderId.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propParentFolderId);
        }

        private async void GetCalendarFolderProps(string FolderId, string FolderDisplayName)
        {
            // Get the folder.
            ICalendar calendarFolderResults = new Calendar();

            try
            {
                calendarFolderResults = await client.Me.Calendars[FolderId].ExecuteAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Office365APIEditor");
                return;
            }

            // Add columns.
            dataGridView_FolderProps.Columns.Add("Property", "Property");
            dataGridView_FolderProps.Columns.Add("Value", "Value");
            dataGridView_FolderProps.Columns.Add("Type", "Type");

            // Add rows.

            DataGridViewRow propChangeKey = new DataGridViewRow();
            propChangeKey.CreateCells(dataGridView_FolderProps, new object[] { "ChangeKey", calendarFolderResults.ChangeKey, calendarFolderResults.ChangeKey.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propChangeKey);

            DataGridViewRow propColor = new DataGridViewRow();
            propColor.CreateCells(dataGridView_FolderProps, new object[] { "Color", calendarFolderResults.Color.ToString(), calendarFolderResults.Color.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propColor);

            DataGridViewRow propId = new DataGridViewRow();
            propId.CreateCells(dataGridView_FolderProps, new object[] { "Id", calendarFolderResults.Id, calendarFolderResults.Id.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propId);

            DataGridViewRow propName = new DataGridViewRow();
            propName.CreateCells(dataGridView_FolderProps, new object[] { "Name", calendarFolderResults.Name, calendarFolderResults.Name.GetType().ToString() });
            dataGridView_FolderProps.Rows.Add(propName);
        }

        private void treeView_Mailbox_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                treeView_Mailbox.SelectedNode = e.Node;
            }
        }

        private void treeView_Mailbox_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            FolderInfo info = (FolderInfo)treeView_Mailbox.SelectedNode.Tag;

            if (info.Type == FolderContentType.DummyCalendarRoot)
            {
                // This is a dummy node. We should do nothing.
                return;
            }

            // Open selected folder.
            OpenFolder(e.Node);
        }

        private void ToolStripMenuItem_OpenContentTable_Click(object sender, EventArgs e)
        {
            // Open selected folder.
            OpenFolder(treeView_Mailbox.SelectedNode);
        }

        private void treeView_Mailbox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Clicks == 2)
            {
                doubleClicked = true;
            }
            else
            {
                doubleClicked = false;
            }
        }

        private void treeView_Mailbox_BeforeCollapse(object sender, TreeViewCancelEventArgs e)
        {
            // Disable collapsing after double click.
            e.Cancel = doubleClicked;
            doubleClicked = false;
        }

        private void treeView_Mailbox_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            if (doubleClicked)
            {
                // Disable expanding after double clock.
                e.Cancel = true;
                doubleClicked = false;

                return;
            }
            
            // Get child folders.

            FolderInfo folderInfo = (FolderInfo)e.Node.Tag;            

            if (folderInfo.Type != FolderContentType.Calendar && folderInfo.Type != FolderContentType.DummyCalendarRoot && folderInfo.Expanded == false)
            {
                expandingNodeHasDummyNode = true;

                PrepareChildMailFolders(folderInfo.ID, e.Node);
                PrepareChildContactFolders(folderInfo.ID, e.Node);

                folderInfo.Expanded = true;
                e.Node.Tag = folderInfo;
            }
        }

        private void OpenFolder(TreeNode SelectedNode)
        {
            // Open selected folder.
            FolderViewerForm folderViewerForm = new FolderViewerForm(pca, currentUser, (FolderInfo)SelectedNode.Tag, SelectedNode.Text);
            folderViewerForm.Show();
        }

        private void newSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Prepare())
            {
                // New session stated
                newSessionToolStripMenuItem.Enabled = false;
                closeSessionToolStripMenuItem.Enabled = true;
            }
        }

        private void newEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (requestFormOpened)
            {
                MessageBox.Show("Editor window is already opened.", "Office365APIEditor");
            }
            else
            {
                requestFormOpened = true;
                RequestForm requestForm = new RequestForm
                {
                    Owner = this
                };
                requestForm.Show();
            }
        }

        private void closeSessionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CloseCurrentSession();

            newSessionToolStripMenuItem.Enabled = true;
            closeSessionToolStripMenuItem.Enabled = false;
        }

        private void CloseCurrentSession()
        {
            client = null;
            dataGridView_FolderProps.Rows.Clear();
            dataGridView_FolderProps.Columns.Clear();
            treeView_Mailbox.Nodes.Clear();
        }

        private void accessTokenViewerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TokenViewer tokenView = new TokenViewer
            {
                Owner = this
            };
            tokenView.Show();
        }
    }
}