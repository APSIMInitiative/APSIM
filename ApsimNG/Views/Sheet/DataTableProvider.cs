﻿using System;
using System.Collections.Generic;
using System.Data;

namespace UserInterface.Views
{
    /// <summary>
    /// Wraps a .NET DataTable as a data provider for a sheet widget.
    /// </summary>
    public class DataTableProvider : ISheetDataProvider
    {
        /// <summary>The wrapped data table.</summary>
        private readonly DataTable data;

        /// <summary>The optional units for each column in the data table. Can be null.</summary>
        private readonly IList<string> units;

        /// <summary>Number of heading rows.</summary>
        private int numHeadingRows;

        /// <summary>Constructor.</summary>
        /// <param name="dataSource">A data table.</param>
        /// <param name="columnUnits">Optional units for each column of data.</param>
        public DataTableProvider(DataTable dataSource, IList<string> columnUnits = null)
        {
            if (dataSource == null)
                data = new DataTable();
            else
                data = dataSource;
            units = columnUnits;
            if (units == null)
                numHeadingRows = 1;
            else
                numHeadingRows = 2;
        }

        /// <summary>Gets the number of columns of data.</summary>
        public int ColumnCount => data.Columns.Count;

        /// <summary>Gets the number of rows of data.</summary>
        public int RowCount => data.Rows.Count + numHeadingRows;

        /// <summary>Get the contents of a cell.</summary>
        /// <param name="colIndex">Column index of cell.</param>
        /// <param name="rowIndex">Row index of cell.</param>
        public string GetCellContents(int colIndex, int rowIndex)
        {
            if (rowIndex == 0)
                return data.Columns[colIndex].ColumnName;
            else if (numHeadingRows == 2 && rowIndex == 1)
                return units[colIndex];
            var value = data.Rows[rowIndex - numHeadingRows][colIndex];
            if (value is double)
                return ((double)value).ToString("F3");  // 3 decimal places.
            else if (value is DateTime)
                return ((DateTime)value).ToString("yyyy-MM-dd");
            return value.ToString();
        }

        /// <summary>Set the contents of a cell.</summary>
        /// <param name="colIndex">Column index of cell.</param>
        /// <param name="rowIndex">Row index of cell.</param>
        /// <param name="value">The value.</param>
        public void SetCellContents(int colIndex, int rowIndex, string value)
        {
            data.Rows[rowIndex - numHeadingRows][colIndex] = value;
        }
    }
}