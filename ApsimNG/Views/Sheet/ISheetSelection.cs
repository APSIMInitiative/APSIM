﻿namespace UserInterface.Views
{
    /// <summary>Describes the public interface of a class that supports sheet cell selection.</summary>
    public interface ISheetSelection
    {
        /// <summary>Gets whether a cell is selected.</summary>
        /// <param name="columnIndex">The index of the current selected column.</param>
        /// <param name="rowIndex">The index of the current selected row</param>
        /// <returns>True if selected, false otherwise.</returns>
        bool IsSelected(int columnIndex, int rowIndex);

        /// <summary>Gets the currently selected cell..</summary>
        /// <param name="columnIndex">The index of the current selected column.</param>
        /// <param name="rowIndex">The index of the current selected row</param>
        void GetSelection(out int columnIndex, out int rowIndex);
    }
}