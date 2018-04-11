﻿// -----------------------------------------------------------------------
// <copyright file="EditorView.cs" company="APSIM Initiative">
//     Copyright (c) APSIM Initiative
// </copyright>
// -----------------------------------------------------------------------

namespace UserInterface.Views
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using EventArguments;
    using Gtk;
    using Mono.TextEditor;
    using Utility;

    /// <summary>
    /// This is IEditorView interface
    /// </summary>
    public interface IEditorView
    {
        /// <summary>
        /// Invoked when the editor needs context items (after user presses '.')
        /// </summary>
        event EventHandler<NeedContextItemsArgs> ContextItemsNeeded;

        /// <summary>
        /// Invoked when the user changes the text in the editor.
        /// </summary>
        event EventHandler TextHasChangedByUser;

        /// <summary>
        /// Invoked when the user leaves the text editor.
        /// </summary>
        event EventHandler LeaveEditor;

        /// <summary>
        /// Gets or sets the text property to get and set the content of the editor.
        /// </summary>
        string Text { get; set; }

        /// <summary>
        /// Gets or sets the lines property to get and set the lines in the editor.
        /// </summary>
        string[] Lines { get; set; }

        /// <summary>
        /// Gets or sets the characters that bring up the intellisense context menu.
        /// </summary>
        string IntelliSenseChars { get; set; }

        /// <summary>
        /// Gets the current line number
        /// </summary>
        int CurrentLineNumber { get; }

        /// <summary>
        /// Gets or sets the current location of the caret (column and line)
        /// </summary>
        System.Drawing.Rectangle Location { get; set; }
        
        /// <summary>
        /// Add a separator line to the context menu
        /// </summary>
        void AddContextSeparator();

        /// <summary>
        /// Add an action (on context menu) on the series grid.
        /// </summary>
        /// <param name="menuItemText">The text of the menu item</param>
        /// <param name="onClick">The event handler to call when menu is selected</param>
        /// <param name="shortcut">Describes the key to use as the accelerator</param>
        void AddContextActionWithAccel(string menuItemText, System.EventHandler onClick, string shortcut);
    }

    /// <summary>
    /// This class provides an intellisense editor and has the option of syntax highlighting keywords.
    /// </summary>
    public class EditorView : ViewBase, IEditorView
    {
        /// <summary>
        /// The find-and-replace form
        /// </summary>
        private FindAndReplaceForm _findForm = new FindAndReplaceForm();
        
        /// <summary>
        /// The completion form.
        /// </summary>
        private IntellisenseView intellisense;

        /// <summary>
        /// Scrolled window
        /// </summary>
        private ScrolledWindow scroller;

        /// <summary>
        /// The main text editor
        /// </summary>
        private TextEditor textEditor;

        /// <summary>
        /// The popup menu options on the editor
        /// </summary>
        private Menu popupMenu = new Menu();

        /// <summary>
        /// Menu accelerator group
        /// </summary>
        private AccelGroup accel = new AccelGroup();

        /// <summary>
        /// Horizontal scroll position
        /// </summary>
        private int horizScrollPos = -1;

        /// <summary>
        /// Vertical scroll position
        /// </summary>
        private int vertScrollPos = -1;

        /// <summary>
        /// Invoked when the editor needs context items (after user presses '.')
        /// </summary>
        public event EventHandler<NeedContextItemsArgs> ContextItemsNeeded;

        /// <summary>
        /// Invoked when the user changes the text in the editor.
        /// </summary>
        public event EventHandler TextHasChangedByUser;

        /// <summary>
        /// Invoked when the user leaves the text editor.
        /// </summary>
        public event EventHandler LeaveEditor;

        /// <summary>
        /// Gets or sets the text property to get and set the content of the editor.
        /// </summary>
        public string Text
        {
            get
            {
                return textEditor.Text;
            }

            set
            {
                textEditor.Text = value;
                textEditor.Document.MimeType = "text/x-csharp";
                textEditor.Options.EnableSyntaxHighlighting = true;
                textEditor.Options.HighlightMatchingBracket = true;
            }
        }

        /// <summary>
        /// Gets or sets the lines in the editor.
        /// </summary>
        public string[] Lines
        {
            get
            {
                string text = textEditor.Text.TrimEnd("\r\n".ToCharArray());
                return text.Split(new string[] { textEditor.EolMarker, "\r\n", "\n" }, StringSplitOptions.None);
            }

            set
            {
                string st = string.Empty;
                if (value != null)
                {
                    foreach (string avalue in value)
                    {
                        if (st != string.Empty)
                            st += textEditor.EolMarker;
                        st += avalue;
                    }
                }
                Text = st;
            }
        }

        /// <summary>
        /// Gets or sets the characters that bring up the intellisense context menu.
        /// </summary>
        public string IntelliSenseChars { get; set; }

        /// <summary>
        /// Gets the current line number
        /// </summary>
        public int CurrentLineNumber
        {
            get
            {
                return textEditor.Caret.Line;
            }
        }

        /// <summary>
        /// Gets or sets the current location of the caret (column and line) and the current scrolling position
        /// This isn't really a Rectangle, but the Rectangle class gives us a convenient
        /// way to store these values.
        /// </summary>
        public System.Drawing.Rectangle Location
        {
            get
            {
                DocumentLocation loc = textEditor.Caret.Location;
                return new System.Drawing.Rectangle(loc.Column, loc.Line, Convert.ToInt32(scroller.Hadjustment.Value), Convert.ToInt32(scroller.Vadjustment.Value));
            }

            set
            {
                textEditor.Caret.Location = new DocumentLocation(value.Y, value.X);
                horizScrollPos = value.Width;
                vertScrollPos = value.Height;

                // Unfortunately, we often can't set the scroller adjustments immediately, as they may not have been set up yet
                // We make these calls to set the position if we can, but otherwise we'll just hold on to the values until the scrollers are ready
                Hadjustment_Changed(this, null);
                Vadjustment_Changed(this, null);
            }
        }

        /// <summary>
        /// Default constructor that configures the Completion form.
        /// </summary>
        /// <param name="owner">The owner view</param>
        public EditorView(ViewBase owner) : base(owner)
        {
            scroller = new ScrolledWindow();
            textEditor = new TextEditor();
            scroller.Add(textEditor);
            _mainWidget = scroller;
            Mono.TextEditor.TextEditorOptions options = new Mono.TextEditor.TextEditorOptions();
            options.EnableSyntaxHighlighting = true;
            options.ColorScheme = Utility.Configuration.Settings.EditorStyleName; 
            options.HighlightCaretLine = true;
            textEditor.Options = options;
            textEditor.TextArea.DoPopupMenu = DoPopup;
            textEditor.Document.LineChanged += OnTextHasChanged;
            textEditor.TextArea.FocusInEvent += OnTextBoxEnter;
            textEditor.TextArea.FocusOutEvent += OnTextBoxLeave;
            textEditor.TextArea.KeyPressEvent += OnKeyPress;
            scroller.Hadjustment.Changed += Hadjustment_Changed;
            scroller.Vadjustment.Changed += Vadjustment_Changed;
            _mainWidget.Destroyed += _mainWidget_Destroyed;

            AddContextActionWithAccel("Cut", OnCut, "Ctrl+X");
            AddContextActionWithAccel("Copy", OnCopy, "Ctrl+C");
            AddContextActionWithAccel("Paste", OnPaste, "Ctrl+V");
            AddContextActionWithAccel("Delete", OnDelete, "Delete");
            AddContextSeparator();
            AddContextActionWithAccel("Undo", OnUndo, "Ctrl+Z");
            AddContextActionWithAccel("Redo", OnRedo, "Ctrl+Y");
            AddContextActionWithAccel("Find", OnFind, "Ctrl+F");
            AddContextActionWithAccel("Replace", OnReplace, "Ctrl+H");
            AddContextSeparator();
            MenuItem styleMenu = AddMenuItem("Use style", null);
            Menu styles = new Menu();
            styleMenu.Submenu = styles;

            // find all the editor styles and add sub menu items to the popup
            string[] styleNames = Mono.TextEditor.Highlighting.SyntaxModeService.Styles;
            Array.Sort(styleNames, StringComparer.InvariantCulture);
            foreach (string name in styleNames)
            {
                MenuItem subItem = new MenuItem(name);
                subItem.Activated += OnChangeEditorStyle;
                styles.Append(subItem);
            }

            IntelliSenseChars = ".";

            intellisense = new IntellisenseView();
            intellisense.ContextItemsNeeded += ContextItemsNeeded;
            intellisense.LoseFocus += HideCompletionWindow;
        }

        /// <summary>
        /// Cleanup events
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void _mainWidget_Destroyed(object sender, EventArgs e)
        {
            textEditor.Document.LineChanged -= OnTextHasChanged;
            textEditor.TextArea.FocusInEvent -= OnTextBoxEnter;
            textEditor.TextArea.FocusOutEvent -= OnTextBoxLeave;
            textEditor.TextArea.KeyPressEvent -= OnKeyPress;
            scroller.Hadjustment.Changed -= Hadjustment_Changed;
            scroller.Vadjustment.Changed -= Vadjustment_Changed;
            _mainWidget.Destroyed -= _mainWidget_Destroyed;

            intellisense.ContextItemsNeeded -= ContextItemsNeeded;
            intellisense.ItemSelected -= InsertCompletionItemIntoTextBox;
            intellisense.LoseFocus -= HideCompletionWindow;
            intellisense.Cleanup();

            // It's good practice to disconnect all event handlers, as it makes memory leaks
            // less likely. However, we may not "own" the event handlers, so how do we 
            // know what to disconnect?
            // We can do this via reflection. Here's how it currently can be done in Gtk#.
            // Windows.Forms would do it differently.
            // This may break if Gtk# changes the way they implement event handlers.
            foreach (Widget w in popupMenu)
            {
                if (w is MenuItem)
                {
                    PropertyInfo pi = w.GetType().GetProperty("AfterSignals", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi != null)
                    {
                        System.Collections.Hashtable handlers = (System.Collections.Hashtable)pi.GetValue(w);
                        if (handlers != null && handlers.ContainsKey("activate"))
                        {
                            EventHandler handler = (EventHandler)handlers["activate"];
                            (w as MenuItem).Activated -= handler;
                        }
                    }
                }
            }

            popupMenu.Destroy();
            accel.Dispose();
            textEditor.Destroy();
            textEditor = null;
            _findForm.Destroy();
            _owner = null;
        }

        /// <summary>
        /// The vertical position has changed
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The event arguments</param>
        private void Vadjustment_Changed(object sender, EventArgs e)
        {
            if (vertScrollPos > 0 && vertScrollPos < scroller.Vadjustment.Upper)
            {
                scroller.Vadjustment.Value = vertScrollPos;
                scroller.Vadjustment.ChangeValue();
                vertScrollPos = -1;
            }
        }

        /// <summary>
        /// The horizontal position has changed
        /// </summary>
        /// <param name="sender">The sender object</param>
        /// <param name="e">The event arguments</param>
        private void Hadjustment_Changed(object sender, EventArgs e)
        {
            if (horizScrollPos > 0 && horizScrollPos < scroller.Hadjustment.Upper)
            {
                scroller.Hadjustment.Value = horizScrollPos;
                scroller.Hadjustment.ChangeValue();
                horizScrollPos = -1;
            }
        }

        /// <summary>
        /// Preprocesses key strokes so that the ContextList can be displayed when needed. 
        /// </summary>
        /// <param name="sender">Sending object</param>
        /// <param name="e">Key arguments</param>
        [GLib.ConnectBefore] // Otherwise this is handled internally, and we won't see it
        private void OnKeyPress(object sender, KeyPressEventArgs e)
        {
            char keyChar = (char)Gdk.Keyval.ToUnicode(e.Event.KeyValue);
            if (e.Event.Key == Gdk.Key.F3)
            {
                if (string.IsNullOrEmpty(_findForm.LookFor))
                    _findForm.ShowFor(textEditor, false);
                else
                    _findForm.FindNext(true, (e.Event.State & Gdk.ModifierType.ShiftMask) == 0, string.Format("Search text «{0}» not found.", _findForm.LookFor));
                e.RetVal = true;
            }
            else if (IntelliSenseChars.Contains(keyChar.ToString()) && ContextItemsNeeded != null)
            {
                // If user one of the IntelliSenseChars, then display contextlist.
                if (ShowCompletionWindow(keyChar))
                {
                    e.RetVal = false;
                }                
            }
            else
            {
                e.RetVal = false;
            }
        }

        /// <summary>
        /// Retrieve the word before the specified character position. 
        /// </summary>
        /// <param name="pos">Position in the editor</param>
        /// <returns>The position of the word</returns>
        private string GetWordBeforePosition(int pos)
        {
            if (pos == 0)
                return string.Empty;
            else
            {
                int PosDelimiter = textEditor.Text.LastIndexOfAny(" \r\n(+-/*".ToCharArray(), pos - 1);
                return textEditor.Text.Substring(PosDelimiter + 1, pos - PosDelimiter - 1).TrimEnd(".".ToCharArray());
            }
        }

        /// <summary>
        /// Show the context list. Return true if popup box shown
        /// </summary>
        /// <param name="characterPressed">Character pressed</param>
        /// <returns>Completion form showing</returns>        
        private bool ShowCompletionWindow(char characterPressed)
        {
            string textBeforePeriod = GetWordBeforePosition(textEditor.Caret.Offset);
            intellisense.ContextItemsNeeded += ContextItemsNeeded;
            if (!intellisense.GenerateAutoCompletionOptions(textBeforePeriod))
                return false;
            textEditor.TextArea.InsertAtCaret(characterPressed.ToString());

            // Turn readonly on so that the editing window doesn't process keystrokes.
            textEditor.Document.ReadOnly = true;

            // Work out where to put the completion window.            
            Cairo.Point p = textEditor.TextArea.LocationToPoint(textEditor.Caret.Location);
            p.Y += (int)textEditor.LineHeight;

            // Need to convert to screen coordinates....
            int x, y, frameX, frameY;
            mainWindow.GetOrigin(out frameX, out frameY);
            textEditor.TextArea.TranslateCoordinates(_mainWidget.Toplevel, p.X, p.Y, out x, out y);

            intellisense.ItemSelected += InsertCompletionItemIntoTextBox;
            intellisense.MainWindow = MainWidget.Toplevel as Window;
            return intellisense.SmartShowAtCoordinates(frameX + x, frameY + y);
        }

        /// <summary>
        /// Hide the completion window.
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void HideCompletionWindow(object sender, EventArgs e)
        {
            textEditor.Document.ReadOnly = false;
            textEditor.GrabFocus();
        }

        /// <summary>
        /// Insert the currently selected completion item into the text box.
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void InsertCompletionItemIntoTextBox(object sender, IntellisenseItemSelectedArgs e)
        {            
            if (!string.IsNullOrEmpty(e.ItemSelected))
            {
                textEditor.Document.ReadOnly = false;
                textEditor.InsertAtCaret(e.ItemSelected);
            }
            textEditor.Document.ReadOnly = false;
            textEditor.GrabFocus();
        }

        /// <summary>
        /// User has changed text. Invoke our OnTextChanged event.
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnTextHasChanged(object sender, EventArgs e)
        {
            if (TextHasChangedByUser != null)
                TextHasChangedByUser(sender, e);
        }

        /// <summary>
        /// Entering the textbox event
        /// </summary>
        /// <param name="o">The calling object</param>
        /// <param name="args">The arguments</param>
        private void OnTextBoxEnter(object o, FocusInEventArgs args)
        {
            ((o as Widget).Toplevel as Gtk.Window).AddAccelGroup(accel);
        }

        /// <summary>
        /// Leaving the textbox event
        /// </summary>
        /// <param name="o">The calling object</param>
        /// <param name="e">The event arguments</param>
        private void OnTextBoxLeave(object o, EventArgs e)
        {
            ((o as Widget).Toplevel as Gtk.Window).RemoveAccelGroup(accel);
            if (LeaveEditor != null)
                LeaveEditor.Invoke(this, e);
        }

        #region Code related to Edit menu

        /// <summary>
        /// Show the popup menu
        /// </summary>
        /// <param name="b">The button</param>
        private void DoPopup(Gdk.EventButton b)
        {
            popupMenu.Popup();
        }

        /// <summary>
        /// Add a menu item to the menu
        /// </summary>
        /// <param name="menuItemText">Menu item caption</param>
        /// <param name="onClick">Event handler</param>
        /// <returns>The menu item that was created</returns>
        public MenuItem AddMenuItem(string menuItemText, System.EventHandler onClick)
        {
            MenuItem item = new MenuItem(menuItemText);
            if (onClick != null)
                item.Activated += onClick;
            popupMenu.Append(item);
            popupMenu.ShowAll();

            return item;
        }

        /// <summary>
        /// Add an action (on context menu) on the series grid.
        /// </summary>
        public void AddContextSeparator()
        {
            popupMenu.Append(new SeparatorMenuItem());
        }

        /// <summary>
        /// Add an action (on context menu) on the text area.
        /// </summary>
        /// <param name="menuItemText">The text of the menu item</param>
        /// <param name="onClick">The event handler to call when menu is selected</param>
        /// <param name="shortcut">The shortcut string</param>
        public void AddContextActionWithAccel(string menuItemText, System.EventHandler onClick, string shortcut)
        {
            ImageMenuItem item = new ImageMenuItem(menuItemText);
            if (!string.IsNullOrEmpty(shortcut))
            {
                string keyName = string.Empty;
                Gdk.ModifierType modifier = Gdk.ModifierType.None;
                string[] keyNames = shortcut.Split(new char[] { '+' });
                foreach (string name in keyNames)
                {
                    if (name == "Ctrl")
                        modifier |= Gdk.ModifierType.ControlMask;
                    else if (name == "Shift")
                        modifier |= Gdk.ModifierType.ShiftMask;
                    else if (name == "Alt")
                        modifier |= Gdk.ModifierType.Mod1Mask;
                    else if (name == "Del")
                        keyName = "Delete";
                    else
                        keyName = name;
                }
                try
                {
                    Gdk.Key accelKey = (Gdk.Key)Enum.Parse(typeof(Gdk.Key), keyName, false);
                    item.AddAccelerator("activate", accel, (uint)accelKey, modifier, AccelFlags.Visible);
                }
                catch
                {
                }
            }
            if (onClick != null)
                item.Activated += onClick;
            popupMenu.Append(item);
            popupMenu.ShowAll();
        }

        /// <summary>
        /// The cut menu handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnCut(object sender, EventArgs e)
        {
            ClipboardActions.Cut(textEditor.TextArea.GetTextEditorData());
        }

        /// <summary>
        /// The Copy menu handler 
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnCopy(object sender, EventArgs e)
        {
            ClipboardActions.Copy(textEditor.TextArea.GetTextEditorData());
        }

        /// <summary>
        /// The Past menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnPaste(object sender, EventArgs e)
        {
            ClipboardActions.Paste(textEditor.TextArea.GetTextEditorData());
        }

        /// <summary>
        /// The Delete menu handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnDelete(object sender, EventArgs e)
        {
            DeleteActions.Delete(textEditor.TextArea.GetTextEditorData());
        }

        /// <summary>
        /// The Undo menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnUndo(object sender, EventArgs e)
        {
            MiscActions.Undo(textEditor.TextArea.GetTextEditorData());
        }

        /// <summary>
        /// The Redo menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnRedo(object sender, EventArgs e)
        {
            MiscActions.Redo(textEditor.TextArea.GetTextEditorData());
        }

        /// <summary>
        /// The Find menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnFind(object sender, EventArgs e)
        {
            _findForm.ShowFor(textEditor, false);
        }

        /// <summary>
        /// The Replace menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnReplace(object sender, EventArgs e)
        {
            _findForm.ShowFor(textEditor, true);
        }

        /// <summary>
        /// Changing the editor style menu item handler
        /// </summary>
        /// <param name="sender">The sending object</param>
        /// <param name="e">The event arguments</param>
        private void OnChangeEditorStyle(object sender, EventArgs e)
        {
            string caption = ((Gtk.Label)(((MenuItem)sender).Children[0])).LabelProp;
            
            Utility.Configuration.Settings.EditorStyleName = caption;
            textEditor.Options.ColorScheme = caption;
            textEditor.QueueDraw();
        }

        // The following block comes from the example code provided at 
        // http://www.codeproject.com/Articles/30936/Using-ICSharpCode-TextEditor
        // I leave it here because it provides the handlers needed for a popup menu
        // Currently find and replace functions are accessed via keystrokes (e.g, ctrl-F, F3)
        /*
        private void menuToggleBookmark_Click(object sender, EventArgs e)
        {
            DoEditAction(new ICSharpCode.TextEditor.Actions.ToggleBookmark());
            TextBox.IsIconBarVisible = TextBox.Document.BookmarkManager.Marks.Count > 0;
        }

        private void menuGoToNextBookmark_Click(object sender, EventArgs e)
        {
            DoEditAction(new ICSharpCode.TextEditor.Actions.GotoNextBookmark
                (bookmark => true));
        }

        private void menuGoToPrevBookmark_Click(object sender, EventArgs e)
        {
            DoEditAction(new ICSharpCode.TextEditor.Actions.GotoPrevBookmark
                (bookmark => true));
        }
        */

        #endregion
    }
}
