// An APSIMInputFile is either a ".met" file or a ".out" file.
// They are both text files that share the same format.
// These classes are used to read/write these files and create an object instance of them.
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;

namespace APSIM.Shared.Utilities
{
    /// <summary>
    /// A simple type for encapsulating a constant
    /// </summary>
    [Serializable]
    public class ApsimConstant
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApsimConstant"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="val">The value.</param>
        /// <param name="units">The units.</param>
        /// <param name="comm">The comm.</param>
        public ApsimConstant(string name, string val, string units, string comm)
        {
            Name = name;
            Value = val;
            Units = units;
            Comment = comm;
        }

        /// <summary>The name</summary>
        public string Name;

        /// <summary>The value</summary>
        public string Value;

        /// <summary>The units</summary>
        public string Units;

        /// <summary>The comment</summary>
        public string Comment;
    }

    /// <summary>
    /// A simple type for encapsulating a text entry
    /// </summary>
    [Serializable]
    public class ApsimPositionCacheEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ApsimConstant"/> class.
        /// </summary>
        /// <param name="date">The datetime of this entry.</param>
        /// <param name="pos">The file position of this entry.</param>
        public ApsimPositionCacheEntry(DateTime date, long pos)
        {
            Date = date;
            Position = pos;
        }

        /// <summary>The datetime of the line</summary>
        public DateTime Date;

        /// <summary>The value of the line</summary>
        public string Value;

        /// <summary>The position of the line in the file</summary>
        public long Position;
    }

    /// <summary>
    /// This class encapsulates an APSIM input file providing methods for
    /// reading data.
    /// </summary>
    [Serializable]
    public class ApsimTextFile
    {
        /// <summary>The file name</summary>
        private string _FileName;

        /// <summary>The name of the excel worksheet (where applicable)</summary>
        private string _SheetName;

        /// <summary>The headings</summary>
        public StringCollection Headings;

        /// <summary>The units</summary>
        public StringCollection Units;

        /// <summary>The _ constants</summary>
        private List<ApsimConstant> _Constants = new List<ApsimConstant>();

        /// <summary>Hello</summary>
        private Dictionary<string, double> _ConstantValues = new();

        /// <summary>Is the file a CSV file</summary>
        public bool IsCSVFile { get; set; } = false;

        /// <summary>The inStreamReader - used for text and csv files</summary>
        private StreamReaderRandomAccess inStreamReader;

        /// <summary>This is used to hold the sheet data (in datatable format) when file opened and extracted</summary>
        private DataTable _excelData;

        /// <summary>Is the apsim file an excel spreadsheet</summary>
        public bool IsExcelFile = false;

        /// <summary>This is used to hold the index of the row in <see cref="_excelData"/> for today's date.</summary>
        private int excelIndex = 0;

        /// <summary>The _ first date</summary>
        private DateTime _FirstDate;

        /// <summary>The _ last date</summary>
        private DateTime _LastDate;

        /// <summary>The first line position</summary>
        private long FirstLinePosition;

        /// <summary>A cache of lines that have been read to. Stores the date, position and value of the line
        /// Stored in date order for faster searching.</summary>
        private LinkedList<ApsimPositionCacheEntry> positionCache = new LinkedList<ApsimPositionCacheEntry>();

        /// <summary>The words</summary>
        private StringCollection Words = new StringCollection();

        /// <summary>The column types</summary>
        private Type[] ColumnTypes;

        /// <summary>
        /// A helper to cleanly get a DataTable from the contents of a file.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>The data table.</returns>
        public static DataTable ToTable(string fileName)
        {
            ApsimTextFile file = new ApsimTextFile();
            try
            {
                file.Open(fileName);
                DataTable data = file.ToTable();
                data.TableName = Path.GetFileNameWithoutExtension(fileName);
                return data;
            }
            finally
            {
                file.Close();
            }
        }

        /// <summary>
        /// Open the text file for reading
        /// </summary>
        /// <param name="fileName">The Name of the file to open</param>
        public void Open(string fileName)
        {
            Open(fileName, "");
        }

        /// <summary>
        /// Open the file ready for reading.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="sheetName">Name of excel worksheet, if applicable</param>
        /// <exception cref="System.Exception">Cannot find file:  + FileName</exception>
        public void Open(string fileName, string sheetName = "")
        {
            if (fileName == "")
                return;

            if (!File.Exists(fileName))
                throw new Exception("Cannot find file: " + fileName);

            _FileName = fileName;
            _SheetName = sheetName;

            IsCSVFile = Path.GetExtension(fileName).ToLower() == ".csv";
            IsExcelFile = ExcelUtilities.IsExcelFile(fileName);

            if (IsExcelFile)
            {
                OpenExcelReader();
            }
            else
            {
                inStreamReader = new StreamReaderRandomAccess(_FileName);
                Open();
            }
        }


        /// <summary>
        /// Opens the specified stream.
        /// </summary>
        /// <param name="stream">The stream.</param>
        public void Open(Stream stream)
        {
            _FileName = "Memory stream";
            inStreamReader = new StreamReaderRandomAccess(stream);
            Open();
        }

        /// <summary>
        /// Open the file ready for reading.
        /// </summary>
        /// <exception cref="System.Exception">
        /// Cannot find headings and units line in  + _FileName
        /// or
        /// Cannot find last row of file:  + _FileName
        /// </exception>
        private void Open()
        {
            _Constants.Clear();
            _ConstantValues.Clear();
            ReadApsimHeader(inStreamReader);
            if (Headings != null)
            {
                FirstLinePosition = inStreamReader.Position;

                // Read in first line.
                StringCollection Words = new StringCollection();
                GetNextLine(inStreamReader, ref Words);
                ColumnTypes = DetermineColumnTypes(inStreamReader, Words);

                // Get first date.
                object[] values = ConvertWordsToObjects(Words, ColumnTypes);
                try
                {
                    _FirstDate = GetDateFromValues(values);
                }
                catch (Exception err)
                {
                    throw new Exception("Unable to parse first date in file " + _FileName + ": " + err.Message);
                }

                // Now we need to seek to the end of file and find the last full line in the file.
                inStreamReader.Seek(0, SeekOrigin.End);
                if (inStreamReader.Position >= 1000 && inStreamReader.Position - 1000 > FirstLinePosition)
                {
                    inStreamReader.Seek(-1000, SeekOrigin.End);
                    inStreamReader.ReadLine(); // throw away partial line.
                }
                else
                    inStreamReader.Seek(FirstLinePosition, SeekOrigin.Begin);

                while (GetNextLine(inStreamReader, ref Words))
                { }

                // Get the date from the last line.
                if (Words.Count == 0)
                    throw new Exception("Cannot find last row of file: " + _FileName);

                values = ConvertWordsToObjects(Words, ColumnTypes);
                _LastDate = GetDateFromValues(values);

                inStreamReader.Seek(FirstLinePosition, SeekOrigin.Begin);
            }
        }


        /// <summary>
        /// Close this file.
        /// </summary>
        public void Close()
        {
            if (inStreamReader != null)
               inStreamReader.Close();
        }

        /// <summary>Gets the first date.</summary>
        public DateTime FirstDate { get { return _FirstDate; } }

        /// <summary>Gets the last date.</summary>
        public DateTime LastDate { get { return _LastDate; } }

        /// <summary>Gets the constants.</summary>
        public List<ApsimConstant> Constants { get { return _Constants; } }

        /// <summary>
        /// Constants the specified constant name.
        /// </summary>
        /// <param name="constantName">Name of the constant.</param>
        /// <returns>Return a given constant to caller</returns>
        public ApsimConstant Constant(string constantName)
        {
            foreach (ApsimConstant c in _Constants)
            {
                if (StringUtilities.StringsAreEqual(c.Name, constantName))
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns a constant as double.
        /// </summary>
        /// <param name="constantName">Name of the constant.</param>
        /// <returns>Returns a constant as double.</returns>
        public double ConstantAsDouble(string constantName)
        {
            if (!_ConstantValues.ContainsKey(constantName))
            {
                ApsimConstant constant = Constant(constantName);
                if (constant == null)
                    throw new Exception($"Constant {constantName} does not exist");
                _ConstantValues[constantName] = Convert.ToDouble(constant.Value, CultureInfo.InvariantCulture);
            }
            return _ConstantValues[constantName];
        }

        /// <summary>
        /// Set a given constant's value.
        /// </summary>
        /// <param name="constantName">Name of the constant.</param>
        /// <param name="constantValue">The constant value.</param>
        public void SetConstant(string constantName, string constantValue)
        {
            foreach (ApsimConstant c in _Constants)
            {
                if (StringUtilities.StringsAreEqual(c.Name, constantName))
                    c.Value = constantValue;
            }
            // This may be changing from a number to something else, hold off on
            // conversion until requested via ConstantAsDouble.
            _ConstantValues.Remove(constantName);
        }

        /// <summary>
        /// Add and set a given constant's value.
        /// </summary>
        /// <param name="constantName">Name of the constant.</param>
        /// <param name="constantValue">The constant value.</param>
        /// <param name="units">The units.</param>
        /// <param name="comment">The comment.</param>
        public void AddConstant(string constantName, string constantValue, string units, string comment)
        {
            _Constants.Add(new ApsimConstant(constantName, constantValue, units, comment));
        }

        /// <summary>
        /// Convert this file to a DataTable.
        /// </summary>
        /// <returns></returns>
        public DataTable ToTable(List<string> addConsts = null)
        {
            System.Data.DataTable data = new System.Data.DataTable();
            data.TableName = "Data";

            if (IsExcelFile == true)
            {
                data = ToTableFromExcel();
            }
            else
            {
                List<ApsimConstant> addedConstants = new List<ApsimConstant>();

                StringCollection words = new StringCollection();
                bool checkHeadingsExist = true;
                while (GetNextLine(inStreamReader, ref words))
                {
                    if (checkHeadingsExist)
                    {
                        for (int w = 0; w != ColumnTypes.Length; w++)
                            data.Columns.Add(new DataColumn(Headings[w], ColumnTypes[w]));

                        if (addConsts != null)
                        {
                            foreach (ApsimConstant constant in Constants)
                            {
                                if (addConsts.Contains(constant.Name, StringComparer.OrdinalIgnoreCase) && data.Columns.IndexOf(constant.Name) == -1)
                                {
                                    Type ColumnType = StringUtilities.DetermineType(constant.Value, constant.Units);
                                    data.Columns.Add(new DataColumn(constant.Name, ColumnType));
                                    addedConstants.Add(new ApsimConstant(constant.Name, constant.Value, constant.Units, ColumnType.ToString()));
                                }
                            }
                        }
                    }

                    DataRow newMetRow = data.NewRow();
                    object[] values = null;
                    try
                    {
                        values = ConvertWordsToObjects(words, ColumnTypes);
                    }
                    catch (Exception err)
                    {
                        throw new Exception("Error while parsing file " + _FileName + ": " + err.Message);
                    }
                    for (int w = 0; w != words.Count; w++)
                    {
                        int TableColumnNumber = newMetRow.Table.Columns.IndexOf(Headings[w]);
                        if (!Convert.IsDBNull(values[TableColumnNumber]))
                            newMetRow[TableColumnNumber] = values[TableColumnNumber];
                    }

                    foreach (ApsimConstant constant in addedConstants)
                    {
                        if (constant.Comment == typeof(Single).ToString() || constant.Comment == typeof(Double).ToString())
                            newMetRow[constant.Name] = Double.Parse(constant.Value, CultureInfo.InvariantCulture);
                        else
                            newMetRow[constant.Name] = constant.Value;
                    }
                    data.Rows.Add(newMetRow);
                    checkHeadingsExist = false;
                }
            }
            return data;
        }


        /// <summary>
        /// Convert this file to a DataTable.
        /// </summary>
        /// <returns></returns>
        public DataTable ToTableFromExcel()
        {
            System.Data.DataTable data = new System.Data.DataTable();

            if (_excelData.Rows.Count != 0)
            {
                data = _excelData.Copy();
            }
            //will I ever hit this without having any data???

            if (data != null)
            {
                //the excel to table function will not set the column caption
                //so we do it here manually, otherwise it's just called 'column1' etc
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    data.Columns[i].Caption = data.Columns[i].ColumnName;
                }
                return data;
            } else
            {
                throw new Exception("Error converting excel data to table");
            }
        }


        /// <summary>
        /// Reads the apsim header lines.
        /// </summary>
        /// <param name="inData">The in.</param>
        /// <param name="constantLines">The constant lines.</param>
        /// <param name="headingLines">The heading lines.</param>
        private void ReadApsimHeaderLines(StreamReaderRandomAccess inData,
                                          ref StringCollection constantLines,
                                          ref StringCollection headingLines)
        {
            string PreviousLine = "";

            while (!inData.EndOfStream)
            {
                string Line = inData.ReadLine();
                int PosEquals = Line.IndexOf('=');
                if (PosEquals != -1)
                {
                    // constant found.
                    constantLines.Add(Line);
                }
                else
                {
                    if (IsCSVFile)
                    {
                        headingLines.Add(Line);
                        break;
                    }

                    char[] whitespace = { ' ', '\t' };
                    int PosFirstNonBlankChar = StringUtilities.IndexNotOfAny(Line, whitespace);
                    if (PosFirstNonBlankChar != -1 && Line[PosFirstNonBlankChar] == '(')
                    {
                        headingLines.Add(PreviousLine);
                        headingLines.Add(Line);
                        break;
                    }
                }
                PreviousLine = Line;
            }

        }

        /// <summary>
        /// Read in the APSIM header - headings/units and constants.
        /// </summary>
        /// <param name="inData">The in.</param>
        /// <exception cref="System.Exception">The number of headings and units doesn't match in file:  + _FileName</exception>
        private void ReadApsimHeader(StreamReaderRandomAccess inData)
        {
            StringCollection ConstantLines = new StringCollection();
            StringCollection HeadingLines = new StringCollection();
            ReadApsimHeaderLines(inData, ref ConstantLines, ref HeadingLines);

            bool TitleFound = false;
            foreach (string ConstantLine in ConstantLines)
            {
                string Line = ConstantLine;
                string Comment = StringUtilities.SplitOffAfterDelimiter(ref Line, "!");
                Comment.Trim();
                int PosEquals = Line.IndexOf('=');
                if (PosEquals != -1)
                {
                    string Name = Line.Substring(0, PosEquals).Trim();
                    if (Name.ToLower() == "title")
                    {
                        TitleFound = true;
                        Name = "Title";
                    }
                    string Value = Line.Substring(PosEquals + 1).Trim();
                    string Unit = string.Empty;
                    if (Name != "Title")
                        Unit = StringUtilities.SplitOffBracketedValue(ref Value, '(', ')');
                    _Constants.Add(new ApsimConstant(Name, Value, Unit, Comment));
                }
            }
            if (HeadingLines.Count >= 1)
            {
                if (IsCSVFile)
                {
                    HeadingLines[0] = HeadingLines[0].TrimEnd(',');
                    Headings = new StringCollection();
                    Units = new StringCollection();
                    Headings.AddRange(HeadingLines[0].Split(",".ToCharArray()));
                    for (int i = 0; i < Headings.Count; i++)
                    {
                        Headings[i] = Headings[i].Trim();
                        Headings[i] = Headings[i].Trim('"');
                        Units.Add("()");
                    }
                }
                else
                {
                    Headings = new StringCollection();
                    Headings.AddRange(StringUtilities.SplitStringHonouringQuotes(HeadingLines[0], " \t").ToArray());
                    Units = new StringCollection();
                    Units.AddRange(StringUtilities.SplitStringHonouringBrackets(HeadingLines[1], " \t", '(', ')'));
                }
                TitleFound = TitleFound || StringUtilities.IndexOfCaseInsensitive(Headings, "title") != -1;
                if (Headings.Count != Units.Count)
                    throw new Exception("The number of headings and units doesn't match in file: " + _FileName);
            }
            if (!TitleFound)
                _Constants.Add(new ApsimConstant("Title", System.IO.Path.GetFileNameWithoutExtension(_FileName), "", ""));
        }

        /// <summary>
        /// Determine and return the data types of the specfied words.
        /// </summary>
        /// <param name="inData">The in.</param>
        /// <param name="words">The words.</param>
        /// <returns></returns>
        private Type[] DetermineColumnTypes(StreamReaderRandomAccess inData, StringCollection words)
        {
            Type[] Types = new Type[words.Count];
            for (int w = 0; w != words.Count; w++)
            {
                if (words[w] == "?" || words[w] == "*" || words[w] == "")
                    Types[w] = StringUtilities.DetermineType(LookAheadForNonMissingValue(inData, w), Units[w]);
                else
                    Types[w] = StringUtilities.DetermineType(words[w], Units[w]);

                // If we can parse as a DateTime, but don't yet have an explicit format, try to determine
                // the correct format and make it explicit.
                if (Types[w] == typeof(DateTime) && (Units[w] == "" || Units[w] == "()"))
                {
                    words[w] = DateUtilities.ValidateDateStringWithYear(words[w]);
                    Units[w] = "(yyyy-MM-dd)";
                }

            }
            return Types;
        }

        /// <summary>
        /// Convert the specified words to the specified column types and return their values.
        /// </summary>
        /// <param name="words">The words.</param>
        /// <param name="columnTypes">The column types.</param>
        /// <returns></returns>
        private object[] ConvertWordsToObjects(StringCollection words, Type[] columnTypes)
        {
            object[] values = new object[words.Count];
            for (int w = 0; w != words.Count; w++)
            {
                try
                {
                    words[w] = words[w].Trim();
                    if (words[w] == "?" || words[w] == "*" || words[w] == "")
                        values[w] = DBNull.Value;

                    else if (columnTypes[w] == typeof(DateTime))
                    {
                        values[w] = DateUtilities.GetDate(words[w]);
                    }
                    else if (columnTypes[w] == typeof(float))
                    {
                        double Value;
                        if (double.TryParse(words[w], NumberStyles.Float, CultureInfo.InvariantCulture, out Value))
                            values[w] = Value;
                        else
                            values[w] = DBNull.Value;
                    }
                    else
                        values[w] = words[w];
                }
                catch (Exception)
                {
                    values[w] = DBNull.Value;
                }
            }
            return values;
        }

        /// <summary>
        /// Return the next line in the file as a collection of words.
        /// </summary>
        /// <param name="inData">The in.</param>
        /// <param name="words">The words.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Invalid number of values on line:  + Line + \r\nin file:  + _FileName</exception>
        private bool GetNextLine(StreamReaderRandomAccess inData, ref StringCollection words)
        {
            if (inData.EndOfStream)
                return false;

            string Line = inData.ReadLine();

            if (string.IsNullOrWhiteSpace(Line))
                return false;

            if (Line.IndexOf("!") > 0) //used to ignore "!" in a row
                Line = Line.Substring(0, Line.IndexOf("!") - 1);

            if (IsCSVFile)
            {
                words.Clear();
                //Line = Line.TrimEnd(',');
                words.AddRange(Line.Split(",".ToCharArray()));
            }
            else {
                words = new StringCollection();
                words.AddRange(StringUtilities.SplitStringHonouringQuotes(Line, " \t").ToArray());
            }
                

            if (words.Count != Headings.Count)
                throw new Exception("Invalid number of values on line: " + Line + "\r\nin file: " + _FileName);

            // Remove leading / trailing double quote chars.
            for (int i = 0; i < words.Count; i++)
                words[i] = words[i].Trim("\"".ToCharArray());

            return true;
        }

        /// <summary>
        /// Looks the ahead for non missing value.
        /// </summary>
        /// <param name="inData">The in.</param>
        /// <param name="w">The w.</param>
        /// <returns></returns>
        private string LookAheadForNonMissingValue(StreamReaderRandomAccess inData, int w)
        {
            if (inData.EndOfStream)
                return "?";

            long Pos = inData.Position;

            StringCollection Words = new StringCollection();
            while (GetNextLine(inData, ref Words) && (Words[w] == "?" || Words[w] == "*")) ;
            inData.Position = Pos;

            if (Words.Count > w)
                return Words[w];
            else
                return "?";
        }

        /// <summary>
        /// Return the first date from the specified objects. Will return empty DateTime if not found.
        /// </summary>
        /// <param name="values">The values.</param>
        /// <returns></returns>
        public DateTime GetDateFromValues(object[] values)
        {
            int Year = 0;
            int Month = 0;
            int Day = 0;
            for (int Col = 0; Col != values.Length; Col++)
            {
                string ColumnName = Headings[Col];
                try
                {
                    if (ColumnName.Equals("date", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (ColumnTypes[Col] == typeof(DateTime))
                            return DateUtilities.GetDate(DateUtilities.GetDateAsString((DateTime)values[Col]));
                        else if (ColumnTypes[Col] == typeof(string))
                            return DateUtilities.GetDate(values[Col].ToString());
                    }
                    else if (ColumnName.Equals("year", StringComparison.CurrentCultureIgnoreCase))
                        Year = Convert.ToInt32(values[Col], CultureInfo.InvariantCulture);
                    else if (ColumnName.Equals("month", StringComparison.CurrentCultureIgnoreCase))
                        Month = Convert.ToInt32(values[Col], CultureInfo.InvariantCulture);
                    else if (ColumnName.Equals("day", StringComparison.CurrentCultureIgnoreCase))
                        Day = Convert.ToInt32(values[Col], CultureInfo.InvariantCulture);
                }
                catch (Exception err)
                {
                    throw new Exception("Unable to parse " + ColumnName + " from '" + values[Col] + "': " + err.Message);
                }
            }

            if (Year > 0)
            {
                if (Day > 0)
                    return new DateTime(Year, 1, 1).AddDays(Day - 1);
                else
                    Day = 1;

                if (Month == 0)
                    Month = 1;

                return new DateTime(Year, Month, Day);
            }
            return new DateTime();
        }


        /// <summary>
        /// Returns a date from data in a Datarow
        /// </summary>
        /// <param name="table"></param>
        /// <param name="rowIndex"></param>
        /// <returns></returns>
        public DateTime GetDateFromValues(DataTable table, int rowIndex)
        {
            int Year = 0;
            int Month = 0;
            int Day = 0;
            for (int col = 0; col < table.Columns.Count; col++)
            {
                string ColumnName = table.Columns[col].ColumnName;
                if (ColumnName.Equals("date", StringComparison.CurrentCultureIgnoreCase))
                    DateUtilities.GetDate(table.Rows[rowIndex][col].ToString());
                else if (ColumnName.Equals("year", StringComparison.CurrentCultureIgnoreCase))
                    Year = Convert.ToInt32(table.Rows[rowIndex][col], CultureInfo.InvariantCulture);
                else if (ColumnName.Equals("month", StringComparison.CurrentCultureIgnoreCase))
                    Month = Convert.ToInt32(table.Rows[rowIndex][col], CultureInfo.InvariantCulture);
                else if (ColumnName.Equals("day", StringComparison.CurrentCultureIgnoreCase))
                    Day = Convert.ToInt32(table.Rows[rowIndex][col], CultureInfo.InvariantCulture);
            }

            if (Year > 0)
            {
                if (Day > 0)
                    return new DateTime(Year, 1, 1).AddDays(Day - 1);
                else
                    Day = 1;

                if (Month == 0)
                    Month = 1;

                return new DateTime(Year, Month, Day);
            }
            return new DateTime();
        }

        /// <summary>
        /// Seek to the specified date. Will throw if date not found.
        /// </summary>
        /// <param name="date">The date.</param>
        /// <exception cref="System.Exception">Date  + Date.ToString() +  doesn't exist in file:  + _FileName</exception>
        public void SeekToDate(DateTime date)
        {
            if (date < _FirstDate)
                throw new Exception("Date " + DateUtilities.GetDateAsString(date) + " doesn't exist in file: " + _FileName);

            //check if we've looked at this date before
            ApsimPositionCacheEntry previousEntry = null; //used as a starting point to save searching from the start of the file if entries exist
            foreach (ApsimPositionCacheEntry entry in this.positionCache)
            {
                if (entry.Date.Equals(date))
                {
                    if (IsExcelFile)
                        excelIndex = (int)entry.Position;
                    else
                        SeekToPosition(entry.Position);
                    return;
                }
                else if (entry.Date < date)
                {
                    //cache is stored in reverse order, so stop looking after the date is past
                    previousEntry = entry;
                    break;
                }
            }

            long newPosition = 0;
            //did not find date in cache, start looking from closest entry found
            if (IsExcelFile)
            {
                int startingIndex = 0;
                if (previousEntry != null)
                    startingIndex = (int)previousEntry.Position;

                // Iterate through the DataTable, using excelIndex as the counter.
                // If we reach a row whose date is greater than or equal to the
                // desired date, break out of the loop.
                for (excelIndex = startingIndex; excelIndex < _excelData.Rows.Count; excelIndex++)
                {
                    DateTime rowDate = GetDateFromValues(_excelData.Rows[excelIndex].ItemArray);
                    if (rowDate >= date)
                        break;
                }
                newPosition = excelIndex;
            }
            else
            {
                //calculate how many rows we need to read through to each the given date
                //one day per row is written in the file
                int NumRowsToSkip = 0;
                if (previousEntry != null)
                {
                    NumRowsToSkip = (date - previousEntry.Date).Days;
                    SeekToPosition(previousEntry.Position);
                }
                else
                {
                    NumRowsToSkip = (date - _FirstDate).Days;
                    SeekToPosition(FirstLinePosition);
                }

                //read the file until we reach the correct line
                while (!inStreamReader.EndOfStream && NumRowsToSkip > 0)
                {
                    inStreamReader.ReadLine();
                    NumRowsToSkip--;
                }
                newPosition = GetCurrentPosition();
            }

            //now that we are in the position, insert our entry at this point in the list
            ApsimPositionCacheEntry newEntry = new ApsimPositionCacheEntry(date, newPosition);
            if (previousEntry != null)
                this.positionCache.AddBefore(this.positionCache.Find(previousEntry), newEntry);
            else
                this.positionCache.AddFirst(newEntry);
        }

        /// <summary>
        /// Return the next line of data from the file as an array of objects.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="System.Exception">End of file reached while reading file:  + _FileName</exception>
        public object[] GetNextLineOfData()
        {
            Words.Clear();
            if (IsExcelFile)
            {
                object[] values = _excelData.Rows[excelIndex].ItemArray;
                excelIndex++;
                return values;
            }
            else
            {
                if (GetNextLine(inStreamReader, ref Words))
                    return ConvertWordsToObjects(Words, ColumnTypes);
                else
                    throw new Exception("End of file reached while reading file: " + _FileName);
            }
        }

        /// <summary>Return the current file position</summary>
        public long GetCurrentPosition()
        {
            if (IsExcelFile)
                return excelIndex;
            else
                return inStreamReader.Position;
        }

        /// <summary>Seek to the specified file position</summary>
        public void SeekToPosition(long position)
        {
            if (IsExcelFile)
                excelIndex = Convert.ToInt32(position);
            else
                inStreamReader.Seek(position, SeekOrigin.Begin);
        }


        #region Excel File Reader Function

        /// <summary>
        /// This is used to read an excel file, extracting header, unit and constant information
        /// </summary>
        public void OpenExcelReader()
        {
            _excelData = new DataTable();

            if ((_FileName.Length <= 0) || (_SheetName.Length <= 0))
                return;

            try
            {
                Units = new StringCollection();
                Headings = new StringCollection();

                DataTable resultDt = ExcelUtilities.ReadExcelFileData(_FileName, _SheetName);

                if (resultDt == null)
                    throw new Exception("There does not appear to be any data.");

                int posEquals, rowCount = -1;
                string coltext1, coltext2, coltext3, coltext4;
                string unit, name, value, comment;
                bool titleFound = false;
                bool dataFound = false;
                bool unitsFound = false;

                while (dataFound == false)
                {
                    rowCount++;
                    coltext1 = resultDt.Rows[rowCount][0].ToString().Trim();
                    coltext2 = resultDt.Rows[rowCount][1].ToString().Trim();
                    coltext3 = resultDt.Rows[rowCount][2].ToString().Trim();
                    coltext4 = resultDt.Rows[rowCount][3].ToString().Trim();

                    //Assumptions are made here about the data, based on the values in the first two columns:
                    // If they are both blank, then this is a blank line.
                    // If there is data in first column, but not second, then this is the header/constant details line
                    // If there is data in both columns, then this is the first of our data for the datatable, with
                    //   the first line being the column titles, and if the second line starts with a '(' then this is
                    //   a measurement line, otherwise we are looking at the actual data values.

                    //if no data only in columns 1 and 2, then it is a blank line
                    //if data only in columns 1, then it could be a comments line, and is ignored
                    //if data only in columns 1 & 2, then it is a constants row
                    //if data on 3 or more columns then is actual data
                    //all measurements (in both constants and data headings) are in brackets after name (title)

                    unit = string.Empty;
                    name = string.Empty;
                    value = string.Empty;
                    comment = string.Empty;

                    posEquals = coltext1.IndexOf('!');
                    if (posEquals == 0)
                    {
                        //this is a comment line, and can be ignored
                        resultDt.Rows[rowCount].Delete();
                    }
                    else if (coltext1.Length == 0)
                    {
                        //if no data in column 1, then this is a blank row, need to make sure we remove these
                        resultDt.Rows[rowCount].Delete();
                    }
                    // Check for and handle "old style" constants
                    else if ((coltext1.Length > 0) && coltext2.Length == 0)
                    {
                        comment = StringUtilities.SplitOffAfterDelimiter(ref coltext1, "!").Trim();
                        posEquals = coltext1.IndexOf('=');
                        if (posEquals != -1)
                        {
                            name = coltext1.Substring(0, posEquals).Trim();
                            if (name.ToLower() == "title")
                            {
                                titleFound = true;
                                name = "Title";
                            }
                            value = coltext1.Substring(posEquals + 1).Trim();
                            if (name != "Title")
                                unit = StringUtilities.SplitOffBracketedValue(ref value, '(', ')');

                            //Replace , notation with . in case they are inputting data from another region
                            value = value.Replace(",", ".");

                            _Constants.Add(new ApsimConstant(name, value, unit, comment));
                        }
                        resultDt.Rows[rowCount].Delete();
                    }
                    else if ((coltext1.Length > 0) && (coltext2.Length > 0) && (coltext4.Length == 0))
                    {
                        //the unit, if it exists, is after the title (name) of the constant
                        unit = StringUtilities.SplitOffBracketedValue(ref coltext1, '(', ')');

                        name = coltext1.Trim();
                        if (name.ToLower() == "title")
                        {
                            titleFound = true;
                            name = "Title";
                        }

                        //now look at what is left in the first row
                        value = coltext2.Trim();

                        //comments are in column three - need to strip out any '!' at the start
                        if (coltext3.Length > 0)
                        {
                            comment = StringUtilities.SplitOffAfterDelimiter(ref coltext3, "!");
                            comment.Trim();
                        }

                        //Replace , notation with . in case they are inputting data from another region
                        value = value.Replace(",", ".");

                        _Constants.Add(new ApsimConstant(name, value, unit, comment));
                        resultDt.Rows[rowCount].Delete();
                    }

                    //the first line that has data in the first 4 columns
                    else if ((coltext1.Length > 0) && (coltext2.Length > 0) && (coltext3.Length > 0) && (coltext4.Length > 0))
                    {
                        for (int i = 0; i < resultDt.Columns.Count; i++)
                        {
                            value = resultDt.Rows[rowCount][i].ToString();

                            unit = StringUtilities.SplitOffBracketedValue(ref value, '(', ')');
                            if (unit.Length <= 0)
                                unit = "()";
                            else
                                unitsFound = true;
                            Units.Add(unit.Trim());

                            //add the title(name) to Headings collection
                            Headings.Add(value.Trim());
                        }

                        resultDt.Rows[rowCount].Delete();
                        //we have got both headings and measurements, so we can exit the while loop
                        dataFound = true;
                    }
                    //to ensure that we never get stuck on infinite loop;
                    if (rowCount >= resultDt.Rows.Count - 1) { dataFound = true; }
                }

                //make sure that the next row doesn't have '()' measurements in it
                coltext1 = resultDt.Rows[rowCount+1][0].ToString().Trim();
                posEquals = coltext1.IndexOf('(');
                if (posEquals == 0)
                {
                    //this line contains brackets, SHOULD be DATA
                    if (unitsFound)
                        throw new Exception();
                    // but if we haven't already seen units,
                    // read units from this line
                    // (to support "old style" layouts)
                    else
                    {
                        for (int i = 0; i < resultDt.Columns.Count; i++)
                        {
                            Units[i] = resultDt.Rows[rowCount+1][i].ToString();
                        }
                        resultDt.Rows[rowCount+1].Delete();
                    }
                }

                //this will actually delete all of the rows that we flagged for delete (above)
                resultDt.AcceptChanges();

                //this is where we clone the current datatable, so that we can set the datatypes to what they should be,
                //based on the first row of actual data (Need to do this as cannot change datatype once a column as data).
                _excelData = resultDt.Clone();
                for (int i = 0; i < resultDt.Columns.Count; i++)
                {
                    _excelData.Columns[i].DataType = StringUtilities.DetermineType(resultDt.Rows[0][i].ToString(), Units[i]);
                }
                _excelData.Load(resultDt.CreateDataReader());

                //now do the column names, need to have data loaded before we rename columns, else the above won't work.
                for (int i = 0; i < resultDt.Columns.Count; i++)
                {
                    // set to identifyable value if blank
                    _excelData.Columns[i].ColumnName = (Headings[i]==""?$"_BLANKCOLUMN_{i}":Headings[i]);
                }

                // delete all columns identified as blank (no column name previously provided)
                int w = 0;
                while (_excelData.Columns.Count > w)
                {
                    if (_excelData.Columns[w].ColumnName.StartsWith("_BLANKCOLUMN_"))
                    {
                        _excelData.Columns.RemoveAt(w);
                    }
                    else
                    {
                        w++;
                    }
                }

                _FirstDate = GetDateFromValues(_excelData, 0);
                _LastDate = GetDateFromValues(_excelData, _excelData.Rows.Count - 1);

                if (!titleFound)
                    _Constants.Add(new ApsimConstant("Title", System.IO.Path.GetFileNameWithoutExtension(_FileName), "", ""));

            }
            catch (Exception e)
            {
                throw new Exception($"Problem reading Excel Sheet {_SheetName} in {_FileName}. Error: {e.Message.ToString()}");
            }
        }

        #endregion
    }
}



