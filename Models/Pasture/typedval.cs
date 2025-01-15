using System;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.Runtime.InteropServices;
#pragma warning disable CS1591
namespace CMPServices
{
    //============================================================================
    /// <summary>
    /// The main class that is used as the base class for structured types such as SDML and DDML values.
    /// </summary>
    //============================================================================
    abstract public class TTypedValue
    {
        /// <summary>
        /// Count of scalar types available in a TTypedValue.
        /// </summary>
        public const int NUMSCALARTYPES = 9;

        //============================================================================
        /// <summary>
        /// The type of the TTypedValue expressed as a simple int.
        /// See <see cref="baseType">baseType()</see>
        /// See <see cref="sTYPECODES"/>
        /// </summary>
        public enum TBaseType
        {
            /// <summary>
            /// Not a type.
            /// </summary>
            ITYPE_EMPTY = 0,
            /// <summary>
            /// Single byte integer.
            /// </summary>
            ITYPE_INT1,
            /// <summary>
            /// Two byte integer.
            /// </summary>
            ITYPE_INT2,
            /// <summary>
            /// Four byte signed integer.
            /// </summary>
            ITYPE_INT4,
            /// <summary>
            /// Eight byte signed integer.
            /// </summary>
            ITYPE_INT8,
            /// <summary>
            /// Single precision float. 4 bytes.
            /// </summary>
            ITYPE_SINGLE,
            /// <summary>
            /// Double precision float. 8 bytes.
            /// </summary>
            ITYPE_DOUBLE,
            /// <summary>
            /// Character.
            /// </summary>
            ITYPE_CHAR,
            /// <summary>
            /// Boolean.
            /// </summary>
            ITYPE_BOOL,
            /// <summary>
            /// Two byte char.
            /// </summary>
            ITYPE_WCHAR,
            /// <summary>
            /// Character string.
            /// </summary>
            ITYPE_STR,
            /// <summary>
            /// Two byte char string.
            /// </summary>
            ITYPE_WSTR,
            /// <summary>
            /// Defined type such as a record.
            /// </summary>
            ITYPE_DEF
        };

        //============================================================================
        /// <summary>
        /// Byte size of a four byte integer.
        /// </summary>
        public const uint INTSIZE = 4;   //size of Int32
        /// <summary>
        /// Byte sizes for the field types TBaseType.ITYPE_INT1 To TBaseType.ITYPE_WCHAR
        /// </summary>
        public static uint[] typeSize = { 0, 1, 2, 4, 8, 4, 8, 1, 1, 2, 0 };

        /// <summary>
        /// Lookup table of type name strings.
        /// </summary>
        /// <example>
        /// integer1,
        /// integer2,
        /// integer4,
        /// single,
        /// double,
        /// char,
        /// boolean,
        /// wchar,
        /// string,
        /// wstring,
        /// defined
        /// <code>
        /// string dblString = sTYPECODES[TBaseType.ITYPE_DOUBLE];
        /// </code>
        /// </example>
        public static String[] sTYPECODES = {"",           // Zero entry is unused
                           "integer1", //1
                           "integer2", //2
                           "integer4", //3
                           "integer8", //4
                           "single",   //5
                           "double",   //6
                           "char",     //7
                           "boolean",  //8
                           "wchar",    //9
                           "string",   //10
                           "wstring",  //11
                           "defined"}; //12
        /// <summary>
        /// The text name of a string type. "string"
        /// </summary>
        public static String STYPE_STR = sTYPECODES[(int)TBaseType.ITYPE_STR];
        /// <summary>
        /// The text name of a boolean type. "boolean"
        /// </summary>
        public static String STYPE_BOOL = sTYPECODES[(int)TBaseType.ITYPE_BOOL];
        /// <summary>
        /// The text name of a double type. "double"
        /// </summary>
        public static String STYPE_DOUBLE = sTYPECODES[(int)TBaseType.ITYPE_DOUBLE];
        /// <summary>
        /// The text name of an integer 4 type. "integer4"
        /// </summary>
        public static String STYPE_INT4 = sTYPECODES[(int)TBaseType.ITYPE_INT4];
        /// <summary>
        /// The text name of a defined type. "defined"
        /// </summary>
        public static String STYPE_DEF = sTYPECODES[(int)TBaseType.ITYPE_DEF];
        /// <summary>
        /// Return value from TTypedValue.isSameType()
        /// </summary>
        /// <seealso cref="TTypedValue">TTypedValue Class</seealso>
        public const int ctSAME = 0;
        /// <summary>
        /// Return value from TTypedValue.isSameType()
        /// </summary>
        /// <seealso cref="TTypedValue">TTypedValue Class</seealso>
        public const int ctCOMP = 1;
        /// <summary>
        /// Return value from TTypedValue.isSameType()
        /// </summary>
        /// <seealso cref="TTypedValue">TTypedValue Class</seealso>
        public const int ctDODGY = 2;
        /// <summary>
        /// Return value from TTypedValue.isSameType().
        /// </summary>
        /// <seealso cref="TTypedValue">TTypedValue Class</seealso>
        public const int ctBAD = -1;

        /// <summary>
        /// Contains two unit fields. Used in the array of matching units.
        /// </summary>
        private struct Unit
        {
            public String unit1;
            public String unit2;
            public Unit(String u1, String u2)
            {
                unit1 = u1;
                unit2 = u2;
            }
        };
        //============================================================================
        /// <summary>
        /// "cc/cc","mm/mm" are matching units.
        /// </summary>
        //============================================================================
        private static Unit match1 = new Unit("g/cm^3", "Mg/m^3");
        //============================================================================
        /// <summary>
        /// "m^3/m^3", "mm/mm" are matching units.
        /// </summary>
        //============================================================================
        private static Unit match2 = new Unit("m^3/m^3", "mm/mm");
        //============================================================================
        /// <summary>
        /// "ppm" and "mg/kg" are allowed to match, although "ppm" is invalid
        /// This is a concession to APSIM
        /// </summary>
        //============================================================================
        private static Unit match3 = new Unit("ppm", "mg/kg");
        //============================================================================
        /// <summary>
        /// "g/cc", "Mg/m^3" are matching units, although "cc" is invalid
        /// This is a concession to APSIM
        /// </summary>
        //============================================================================
        private static Unit match4 = new Unit("g/cc", "Mg/m^3");
        //============================================================================
        /// <summary>
        /// "0-1" and "-" match,  as both are dimensionless
        /// </summary>
        //============================================================================
        private static Unit match5 = new Unit("0-1", "-");
        //============================================================================
        /// <summary>
        /// "0-1" and "mm/mm" match, as both are effectively dimensionless
        /// </summary>
        //============================================================================
        private static Unit match6 = new Unit("0-1", "mm/mm");
        //============================================================================
        /// <summary>
        /// "cm^3/cm^3" and "mm/mm" match, as both are effectively dimensionless
        /// </summary>
        //============================================================================
        private static Unit match7 = new Unit("cm^3/cm^3", "mm/mm");
        //============================================================================
        /// <summary>
        /// "0-1" and "m^3/m^3" match, as both are effectively dimensionless
        /// </summary>
        //============================================================================
        private static Unit match8 = new Unit("0-1", "m^3/m^3");
        //============================================================================
        /// <summary>
        /// "0-1" and "m^2/m^2" match, as both are effectively dimensionless
        /// </summary>
        //============================================================================
        private static Unit match9 = new Unit("0-1", "m^2/m^2");
        //============================================================================
        /// <summary>
        /// Array of the matching units- match1, match2,...
        /// </summary>
        //============================================================================
        private static Unit[] UNITMATCHES = { match1, match2, match3, match4, match5, match6, match7, match8, match9 };

        private TTypedValue childTemplate;      //!<used to keep a pointer to the last element after setElementCount(0)
        /// <summary>
        /// Name of the typed value.
        /// </summary>
        protected String FName;
        /// <summary>
        /// Unit of the typed value.
        /// </summary>
        protected String FUnit;
        /// <summary>
        /// Store the base type as an integer.
        /// </summary>
        protected TBaseType FBaseType;
        /// <summary>
        /// True if a scalar.
        /// </summary>
        protected Boolean FIsScalar;
        /// <summary>
        /// True if an array.
        /// </summary>
        protected Boolean FIsArray;
        /// <summary>
        /// True if a record.
        /// </summary>
        protected Boolean FIsRecord;
        /// <summary>
        /// Block of bytes containing field/element values
        /// </summary>
        protected Byte[] FData;
        /// <summary>
        /// Size in bytes of the memory block holding the value data
        /// </summary>
        protected UInt32 FDataSize;
        /// <summary>
        /// List of TTypedValues that are fld or elem children
        /// </summary>
        protected List<TTypedValue> FMembers;
        /// <summary>
        /// Each typed value uses a parser at creation
        /// </summary>
        protected TSDMLParser parser;

        System.Text.ASCIIEncoding ascii;

        //======================================================================
        /// <summary>
        /// Get the integer value of the dimension bytes. Used for arrays
        /// and strings
        /// </summary>
        /// <param name="data">The block of byte data.</param>
        /// <param name="startIndex">Start at this index. 0 -> x</param>
        /// <returns>Returns the dimension of the array/string</returns>
        //======================================================================
        static protected uint getDimension(Byte[] data, uint startIndex)
        {
            return (uint)(data[startIndex] + (data[startIndex + 1] << 8) + (data[startIndex + 2] << 16) + (data[startIndex + 3] << 24));
        }

        /// <summary>
        /// Finds children nodes in the xml doc.
        /// </summary>
        abstract protected void getFldElemList();
        /// <summary>
        /// Add a new member.
        /// </summary>
        /// <param name="bluePrintValue"></param>
        abstract public void newMember(TTypedValue bluePrintValue);
        /// <summary>
        /// Add a scalar value.
        /// </summary>
        /// <param name="sName">Name</param>
        /// <param name="aType">Type</param>
        /// <returns></returns>
        abstract public TTypedValue addScalar(String sName, TBaseType aType);
        /// <summary>
        /// Writes a field as a string
        /// </summary>
        /// <param name="attrInfo">The value</param>
        /// <param name="indent">Indentation 0-n</param>
        /// <param name="tab">Number of spaces in each tab</param>
        /// <returns>The XML for the typed value.</returns>
        abstract protected String writeFieldInfo(TTypedValue attrInfo, int indent, int tab);
        /// <summary>
        /// Text representation of a TTypedValue.
        /// </summary>
        /// <param name="value">The value</param>
        /// <param name="startIndent">Indent from here.</param>
        /// <param name="tab">Number of spaces in each tab</param>
        /// <returns>The XML.</returns>
        abstract public String getText(TTypedValue value, int startIndent, int tab);

        //======================================================================
        /// <summary>
        /// Constructs a typed value using an XML description.
        /// </summary>
        /// <param name="sXML">XML text description.</param>
        /// <param name="sBaseType">Set the base type of this object. See <see cref="sTYPECODES"/></param>
        // N.Herrmann Apr 2002
        //======================================================================
        public TTypedValue(String sXML, String sBaseType)
        {
            ascii = new System.Text.ASCIIEncoding();

            FMembers = new List<TTypedValue>();
            //set the kind of this typed value
            setBaseType(sBaseType);

            parser = null;
            FData = null;
            FDataSize = 0;
            childTemplate = null;
            FUnit = "";

            //Called in the derived classes because it calls virtual functions
            //buildType(szXML);
        }

        //============================================================================
        /// <summary>
        /// Construct this object using the parser already created in the parent. Also
        /// use the dom node, baseNode to be the root node of the document for this
        /// new typed value. Can also specify the base type using sBaseType.
        /// </summary>
        /// <param name="parentParser">Pointer to the parents parser.</param>
        /// <param name="baseNode">DOM node to use as the root node.</param>
        /// <param name="sBaseType">Used to set the base type.  See <see cref="sTYPECODES"/></param>
        // N.Herrmann Apr 2002
        //============================================================================
        public TTypedValue(TSDMLParser parentParser, XmlNode baseNode, String sBaseType)
        {
            ascii = new System.Text.ASCIIEncoding();

            FMembers = new List<TTypedValue>();
            //set the kind of this typed value
            setBaseType(sBaseType);

            parser = null;
            FData = null;
            FDataSize = 0;
            childTemplate = null;
            FUnit = "";

            //Called in the derived classes because it calls virtual functions
            //buildType(parentParser, baseNode);
        }

        //============================================================================
        /// <summary>
        /// Creates a scalar of this aBaseType with sName.
        /// </summary>
        /// <param name="sName">Name of the scalar.</param>
        /// <param name="aBaseType">Base type of this scalar.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public TTypedValue(String sName, TBaseType aBaseType)
        {
            ascii = new System.Text.ASCIIEncoding();

            FMembers = new List<TTypedValue>();
            //set the kind of this typed value
            FBaseType = aBaseType;
            //Called in the derived classes because it calls virtual functions
            //constructScalar(szName, iBaseType);  //create a scalar type of TTypedValue
            parser = null;
            childTemplate = null;
            FUnit = "";
        }

        //============================================================================
        /// <summary>
        /// Creates a one dimensional array of scalar items.
        /// </summary>
        /// <param name="sArrayName">Name of this array.</param>
        /// <param name="aBaseType">Set the base type of this array.</param>
        /// <param name="iNoElements">Create it with this number of elements.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public TTypedValue(String sArrayName, TBaseType aBaseType, int iNoElements)
        {
            ascii = new System.Text.ASCIIEncoding();

            FMembers = new List<TTypedValue>();
            //set the kind of this typed value
            FBaseType = aBaseType;

            parser = null;
            FData = null;
            FDataSize = 0;
            childTemplate = null;

            Name = sArrayName;
            FUnit = "";
            FIsScalar = false;
            FIsArray = true;
            FIsRecord = false;

            //Called in the derived classes because they call virtual functions
            //addScalar("", iBaseType);     //calls suitable virtual function
            //setElementCount(iNoElements);

        }
        //============================================================================
        /// <summary>
        /// Creates a 1-dimensional array of arbitrary type
        /// baseValue is used as a blue print only.
        /// </summary>
        /// <param name="arrayName">Name of the array.</param>
        /// <param name="baseValue">Blue print typed value.</param>
        /// <param name="noElements">Number of elements for the array.</param>
        //============================================================================
        public TTypedValue(String arrayName, TTypedValue baseValue, int noElements)
        {
            ascii = new System.Text.ASCIIEncoding();

            FMembers = new List<TTypedValue>();
            //set the kind of this typed value
            FBaseType = baseValue.FBaseType;

            parser = null;
            FData = null;
            FDataSize = 0;
            childTemplate = null;
            FUnit = "";
        }

        //============================================================================
        /// <summary>
        /// Copy constructor. This constructor makes a copy of the source's structure.
        /// For specialised child classes, this constructor should be overriden.
        /// </summary>
        /// <param name="typedValue">Use this typed value as the source.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public TTypedValue(TTypedValue typedValue)
        {
            ascii = new System.Text.ASCIIEncoding();

            FMembers = new List<TTypedValue>();
            //set the kind of this typed value
            FBaseType = typedValue.FBaseType;

            FData = null;
            FDataSize = 0;
            parser = null; //won't be using a parser here
            childTemplate = null;
            FUnit = "";

            //Called in the derived classes because it calls virtual functions
            //initTypeCopy(typedValue)
        }
        public TTypedValue this[uint i] => item(i);
        public TTypedValue this[string s] => member(s);

        //============================================================================
        /// <summary>
        /// Finds the array item or field corresponding to the given index.
        /// </summary>
        /// <param name="index">Index of the member of this typed value. 1 -> x</param>
        /// <returns>The typed value.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public TTypedValue member(uint index)
        {
            return item(index);
        }
        //============================================================================
        /// <summary>
        /// Finds the record field corresponding to the given name.
        /// </summary>
        /// <param name="sName">Name of the field to find.</param>
        /// <returns>The typed value found.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public TTypedValue member(String sName)
        {
            TTypedValue nMember = null;
            TTypedValue _item;

            if (!FIsRecord)
                throw (new TypeMisMatchException("Cannot access named members for scalar or array"));

            uint i = 1;
            while ((nMember == null) && (i <= FMembers.Count))
            {
                _item = item(i);
                if (_item.Name.Equals(sName, StringComparison.OrdinalIgnoreCase))
                    nMember = _item;
                else
                    i++;
            }
            return nMember;
        }

        //Common code for the constructors
        //Some of these functions call virtual functions, so they are called
        //in the derived classes.
        //======================================================================
        /// <summary>
        /// Sets the FBaseType class type.
        /// </summary>
        /// <param name="sBaseType">The base type string. See <see cref="sTYPECODES"/></param>
        // N.Herrmann Apr 2002
        //======================================================================
        protected void setBaseType(String sBaseType)
        {
            if (sBaseType != null && (sBaseType.Length > 0))
            {
                FBaseType = TBaseType.ITYPE_DEF;
                while ((FBaseType > TBaseType.ITYPE_EMPTY) && (sBaseType != sTYPECODES[(int)FBaseType]))
                    FBaseType--;
            }
            else
                FBaseType = TBaseType.ITYPE_EMPTY;
        }

        //======================================================================
        /// <summary>
        /// Do the parsing of this type. If it is a structured type, then it will
        /// attempt to find all the children. Called during the construction process.
        /// </summary>
        // N.Herrmann Apr 2002
        //======================================================================
        protected void parseType()
        {
            if (FIsScalar)       //decide here if this is a scalar and whether to get fields
                createScalar();
            else
                getFldElemList(); //get the fields/elements

            if ((FBaseType == TBaseType.ITYPE_EMPTY) && FIsArray)
            {
                FBaseType = findArrayType(this); //retrieve base type from a child
            }
            else if ((FBaseType == TBaseType.ITYPE_DEF) && !FIsArray)
            {
                FIsRecord = true;
            }
        }

        //======================================================================
        /// <summary>
        /// Loads the description of this typed value from the parsed xml text.
        /// Assume that parser.getDescription() has been called.
        /// </summary>
        // N.Herrmann Apr 2002
        //======================================================================
        protected virtual void getDescription()
        {
            FName = parser.Name;
            FUnit = parser.Units;
            if (FBaseType == TBaseType.ITYPE_EMPTY)
            {
                FBaseType = TBaseType.ITYPE_DEF;
                if (parser.Kind.Length > 0)
                {
                    while ((FBaseType > TBaseType.ITYPE_EMPTY) && (parser.Kind != sTYPECODES[(int)FBaseType]))
                        FBaseType--;
                    if (FBaseType == TBaseType.ITYPE_EMPTY)
                        throw new Exception("DDML parse error for \"" + parser.Name + "\"; kind \"" + parser.Kind + "\" is not supported.");
                }
            }

            FIsScalar = parser.IsScalar;
            FIsArray = parser.IsArray;
            FIsRecord = parser.IsRecord;

        }
        //======================================================================
        /// <summary>
        /// Contains common code used by the constructors to set the field values of this
        /// type when it is a scalar.
        /// </summary>
        /// <param name="sName">Name of the scalar.</param>
        /// <param name="aBaseType">The type for this scalar.</param>
        // N.Herrmann Apr 2002
        //======================================================================
        protected void constructScalar(String sName, TBaseType aBaseType)
        {
            FBaseType = aBaseType;
            FData = null;
            //   FDataSize = 0;

            Name = sName;
            FIsScalar = true;
            FIsArray = false;
            FIsRecord = false;
            setUnits("");

            createScalar();              //allocates memory and initialises
        }
        //======================================================================
        /// <summary>
        /// Allocates memory for this scalar and sets it's initial value.
        /// </summary>
        // N.Herrmann Apr 2002
        //======================================================================
        protected virtual void createScalar()
        {
            FDataSize = 0;

            //allocate memory for this type
            if ((FBaseType >= TBaseType.ITYPE_INT1) && (FBaseType <= TBaseType.ITYPE_WSTR))
            {
                if ((FBaseType == TBaseType.ITYPE_STR) || (FBaseType == TBaseType.ITYPE_WSTR))
                {
                    FDataSize = INTSIZE;         //strings have a header to specify the length
                    //create the header so it is always available
                    FData = new Byte[FDataSize];
                    FData[0] = 0;  //no characters yet
                    FData[1] = 0;
                    FData[2] = 0;
                    FData[3] = 0;
                }
                else
                {
                    FDataSize = typeSize[(int)FBaseType];
                    FData = new Byte[FDataSize];
                    setValue(0);                  //init this scalar to 0
                }
            }
            //strings will use their own memory allocation routines to add characters
        }
        //============================================================================
        /// <summary>
        /// The value returned by count() depends on the type of the value, as follows:
        /// <para>For a <b>record</b>, it is the number of members in the record</para>
        /// <para>For a <b>string</b>, it is the number of characters</para>
        /// <para>For a simple <b>scalar</b>, it is zero</para>
        /// </summary>
        /// <returns>The count of elements.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public uint count()
        {
            uint icount;

            if (FIsScalar && ((FBaseType == TBaseType.ITYPE_STR) || (FBaseType == TBaseType.ITYPE_WSTR)))   //String - return the string length
                icount = getDimension(FData, 0);
            else if (FIsRecord || FIsArray)              //Collection - return number of elements
                icount = (uint)FMembers.Count;
            else                                         //Simple scalar
                icount = 0;

            return icount;
        }

        //======================================================================
        /// <summary>
        /// Finds the type of this array object by recursing into the lower dimesions
        /// if needed.
        /// </summary>
        /// <param name="typedValue">The typed value to interogate.</param>
        /// <returns>The base type for this variable. <![CDATA[ <see cref="TypeSpec">TypeSpec class</see> ]]> </returns>
        // N.Herrmann Apr 2002
        //======================================================================
        protected TBaseType findArrayType(TTypedValue typedValue)
        {
            TTypedValue value;
            TBaseType baseType;

            baseType = TBaseType.ITYPE_EMPTY; //default

            value = typedValue.item(1);  //first element
            if (value != null)
            {
                baseType = value.baseType();
                if (baseType == TBaseType.ITYPE_EMPTY)
                    baseType = findArrayType(value);
            }
            return baseType;
        }
        //======================================================================
        /// <summary>
        /// Set the units of the array elements
        /// </summary>
        /// <param name="sUnits">The units string.</param>
        //======================================================================
        public void setUnits(String sUnits)
        {
            if ((FBaseType >= TBaseType.ITYPE_INT1) && (FBaseType <= TBaseType.ITYPE_DOUBLE))
            {   //if number type
                if (FIsScalar || FIsArray)
                    FUnit = sUnits;
                uint iCount = count();
                if (FIsArray && (iCount > 0))
                {            //if has array elements
                    for (uint i = 1; i < iCount; i++)
                        member(i).setUnits(sUnits);
                }
                else
                    if (FIsArray && (iCount == 0) && (member(0) != null))         //else set the 0 element
                        member(0).setUnits(sUnits);
            }
        }
        //======================================================================
        /// <summary>
        /// Get the base type of the typed value. See <see cref="TBaseType"/>
        /// </summary>
        /// <returns>The base type.</returns>
        //======================================================================
        public TBaseType baseType()
        {
            return FBaseType;
        }
        //======================================================================
        /// <summary>
        /// Name of the typed value.
        /// </summary>
        //======================================================================
        public String Name
        {
            get { return FName; }
            set { FName = value; }
        }
        //======================================================================
        /// <summary>
        /// Get the units string.
        /// </summary>
        public String units()
        {
            return FUnit;
        }
        //======================================================================
        /// <summary>
        /// True is this is a scalar.
        /// </summary>
        public Boolean isScalar()
        {
            return FIsScalar;
        }
        //======================================================================
        /// <summary>
        /// True if this is an array.
        /// </summary>
        public Boolean isArray()
        {
            return FIsArray;
        }
        //======================================================================
        /// <summary>
        /// True if this is a record.
        /// </summary>
        public Boolean isRecord()
        {
            return FIsRecord;
        }
        //======================================================================
        /// <summary>
        /// Tests if this is a character type of scalar.
        /// </summary>
        /// <returns>True if this is a scalar of a non number type (text).</returns>
        //======================================================================
        public bool isTextType()
        {
            bool isText = false;
            if (isScalar())
            {
                if ((baseType() == TBaseType.ITYPE_STR) ||        //if char types
                        (baseType() == TBaseType.ITYPE_WCHAR) ||
                        (baseType() == TBaseType.ITYPE_WSTR) ||
                        (baseType() == TBaseType.ITYPE_CHAR))
                    isText = true;
            }
            return isText;
        }
        //======================================================================
        /// <summary>
        /// Set the values in the array.
        /// </summary>
        /// <param name="values">Array of scalar values.</param>
        /// <returns>True if this is successful: This is an array of scalars and each
        /// item has been set.</returns>
        //======================================================================
        public Boolean setValue(Double[] values)
        {
            Boolean result = false;
            if (FIsArray && (FBaseType != TBaseType.ITYPE_DEF) && (values != null))
            {
                result = true;
                setElementCount((uint)values.Length);
                for (uint i = 0; i < values.Length; i++)
                    result = result && item(i + 1).setValue(values[i]);
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Set the values in the array.
        /// </summary>
        /// <param name="values">Array of scalar values.</param>
        /// <returns>True if this is successful: This is an array of scalars and each
        /// item has been set.</returns>
        //======================================================================
        public Boolean setValue(int[] values)
        {
            Boolean result = false;
            if (FIsArray && (FBaseType != TBaseType.ITYPE_DEF) && (values != null))
            {
                result = true;
                setElementCount((uint)values.Length);
                for (uint i = 0; i < values.Length; i++)
                    result = result && item(i + 1).setValue(values[i]);
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Set the values in the array.
        /// </summary>
        /// <param name="values">Array of scalar values.</param>
        /// <returns>True if this is successful: This is an array of scalars and each
        /// item has been set.</returns>
        //======================================================================
        public Boolean setValue(Single[] values)
        {
            Boolean result = false;
            if (FIsArray && (FBaseType != TBaseType.ITYPE_DEF) && (values != null))
            {
                result = true;
                setElementCount((uint)values.Length);
                for (uint i = 0; i < values.Length; i++)
                    result = result && item(i + 1).setValue(values[i]);
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Set the values in the array.
        /// </summary>
        /// <param name="values">Array of scalar values.</param>
        /// <returns>True if this is successful: This is an array of scalars and each
        /// item has been set.</returns>
        //======================================================================
        public Boolean setValue(Boolean[] values)
        {
            Boolean result = false;
            if (FIsArray && (FBaseType != TBaseType.ITYPE_DEF) && (values != null))
            {
                result = true;
                setElementCount((uint)values.Length);
                for (uint i = 0; i < values.Length; i++)
                    result = result && item(i + 1).setValue(values[i]);
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Set the values in the array.
        /// </summary>
        /// <param name="values">Array of scalar values.</param>
        /// <returns>True if this is successful: This is an array of scalars and each
        /// item has been set.</returns>
        //======================================================================
        public Boolean setValue(String[] values)
        {
            Boolean result = false;
            if (FIsArray && (FBaseType == TBaseType.ITYPE_STR) && (values != null))
            {
                result = true;
                setElementCount((uint)values.Length);
                for (uint i = 0; i < values.Length; i++)
                    result = result && item(i + 1).setValue(values[i]);
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(Double value)
        {
            Boolean result = false;

            if (FIsScalar)
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8:
                        {
                            Int64 iValue;
                            if (value < 0)
                                iValue = (Int64)Math.Ceiling(value);
                            else
                                iValue = (Int64)Math.Floor(value);
                            setValue(iValue);
                        } break;
                    case TBaseType.ITYPE_SINGLE:
                        { //4 byte
                            if (value <= Single.MaxValue)
                                FData = BitConverter.GetBytes(Convert.ToSingle(value));
                        } break;
                    case TBaseType.ITYPE_DOUBLE:
                        {
                            FData = BitConverter.GetBytes(value);
                        } break;
                }
                result = true;
            }

            return result;
        }

        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(Int64 value)
        {
            bool result = false;

            if (FIsScalar)
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_INT1:
                        {
                            if (value <= SByte.MaxValue)
                                FData[0] = (BitConverter.GetBytes(value))[0];
                            else
                                return false;
                        } break;
                    case TBaseType.ITYPE_INT2:
                        {
                            if (value <= Int16.MaxValue)
                            {
                                FData[0] = BitConverter.GetBytes(value)[0];
                                FData[1] = BitConverter.GetBytes(value)[1];
                            }
                            else
                                return false;
                        } break;
                    case TBaseType.ITYPE_INT4:
                        {
                            if (value <= Int32.MaxValue)
                            {
                                FData[0] = BitConverter.GetBytes(value)[0];
                                FData[1] = BitConverter.GetBytes(value)[1];
                                FData[2] = BitConverter.GetBytes(value)[2];
                                FData[3] = BitConverter.GetBytes(value)[3];
                            }
                            else
                                return false;
                        } break;
                    case TBaseType.ITYPE_INT8:
                        {
                            FData = BitConverter.GetBytes(value);
                        } break;
                    case TBaseType.ITYPE_SINGLE: setValue((Double)value); break;
                    case TBaseType.ITYPE_DOUBLE: setValue((Double)value); break;
                }
                result = true;
            }

            return result;
        }

        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(float value)
        {
            return setValue((Double)value);
        }

        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(int value)
        {
            return setValue((Int64)value);
        }

        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(bool value)
        {
            bool result = false;

            if (FIsScalar)
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_BOOL: FData = BitConverter.GetBytes(value); break;
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8:
                    case TBaseType.ITYPE_SINGLE:
                    case TBaseType.ITYPE_DOUBLE:
                        {
                            if (value) setValue(1); else setValue(0);
                        } break;
                    case TBaseType.ITYPE_STR:
                    case TBaseType.ITYPE_WSTR:
                        {
                            if (value)
                                setValue("true");
                            else
                                setValue("false");
                        } break;
                }
                result = true;
            }

            return result;
        }

        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(String value)
        {
            bool result = false;

            if (FIsScalar)
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_BOOL:
                        {
                            if ((value.Length > 0) && (Char.ToLower(value[0]) == 't'))
                                setValue(true);
                            else setValue(false);
                        } break;
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8:
                        if (value.Length > 0)
                            setValue(Convert.ToInt64(value)); break;
                    case TBaseType.ITYPE_SINGLE:
                    case TBaseType.ITYPE_DOUBLE:
                        {
                            if (value.Length > 0)
                                setValue(Convert.ToDouble(value));
                            else
                                setValue(0);
                        } break;
                    case TBaseType.ITYPE_CHAR:
                    case TBaseType.ITYPE_WCHAR: setValue(value[0]); break;
                    case TBaseType.ITYPE_STR:
                        {    //single byte characters
                            uint byteCount = (uint)value.Length;
                            FDataSize = INTSIZE + byteCount;
                            FData = new Byte[FDataSize];
                            FData[0] = (Byte)((uint)value.Length);
                            FData[1] = (Byte)(((uint)value.Length) >> 8);
                            FData[2] = (Byte)(((uint)value.Length) >> 16);
                            FData[3] = (Byte)(((uint)value.Length) >> 24);

                            ascii.GetBytes(value, 0, value.Length, FData, 4); //copy the unicode chars to single bytes
                        } break;
                    case TBaseType.ITYPE_WSTR:
                        {    //double byte unicode characters
                            System.Text.UnicodeEncoding uni = new System.Text.UnicodeEncoding();
                            uint byteCount = (uint)uni.GetByteCount(value);
                            FDataSize = INTSIZE + byteCount;
                            FData = new Byte[FDataSize];
                            FData[0] = (Byte)((uint)value.Length);
                            FData[1] = (Byte)(((uint)value.Length) >> 8);
                            FData[2] = (Byte)(((uint)value.Length) >> 16);
                            FData[3] = (Byte)(((uint)value.Length) >> 24);

                            uni.GetBytes(value, 0, value.Length, FData, 4); //copy the unicode chars to double bytes
                        } break;
                }
                result = true;
            }

            return result;

        }

        //======================================================================
        /// <summary>
        /// Sets the value for this scalar.
        /// </summary>
        /// <param name="value">The value to set this scalar to.</param>
        /// <returns>True if successful.</returns>
        //======================================================================
        public Boolean setValue(Char value)
        {
            bool result = false;

            if (FIsScalar)
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_BOOL: setValue(value.ToString()); break;
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8: setValue(Convert.ToInt64(value)); break;
                    case TBaseType.ITYPE_SINGLE:
                    case TBaseType.ITYPE_DOUBLE: setValue(Convert.ToDouble(value)); break;
                    case TBaseType.ITYPE_CHAR:
                        { //single byte char
                            FData = ascii.GetBytes(value.ToString());
                        } break;
                    case TBaseType.ITYPE_WCHAR:
                        { //double byte unicode char
                            FData = BitConverter.GetBytes(value);
                        } break;
                    case TBaseType.ITYPE_STR:
                    case TBaseType.ITYPE_WSTR: setValue(value.ToString()); break;
                }
                result = true;
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Assignment from a TTypedValue that need not be of identical type, but must
        /// be type-compatible.
        /// When converting from a scalar string to a numeric an exception will be thrown
        /// if the source string is not a valid numeric.
        /// </summary>
        /// <param name="srcValue">The source typed value.</param>
        /// <returns>True is the value can be set.</returns>
        //======================================================================
        public Boolean setValue(TTypedValue srcValue)
        {
            bool result = false;
            bool bCompatible;

            if (srcValue != null)
            {
                bCompatible = ((FIsScalar == srcValue.isScalar()) && (FIsArray == srcValue.isArray()));
                if (FBaseType == TBaseType.ITYPE_DEF)
                {
                    bCompatible = (bCompatible && (srcValue.baseType() == TBaseType.ITYPE_DEF));
                }
                if (!bCompatible)
                {
                    String error = String.Format("Incompatible assignment from {0} to {1}\nCannot convert {2} to {3}", Name, srcValue.Name, srcValue.baseType().ToString(), FBaseType.ToString());
                    throw (new TypeMisMatchException(error));
                }
                if (FIsScalar)
                {
                    try
                    {
                        switch (FBaseType)
                        {
                            case TBaseType.ITYPE_INT1:
                            case TBaseType.ITYPE_INT2:
                            case TBaseType.ITYPE_INT4:
                                {
                                    result = setValue(srcValue.asInt());
                                    break;
                                }
                            case TBaseType.ITYPE_INT8:
                                result = setValue(Convert.ToInt64(srcValue.asDouble()));
                                break;
                            case TBaseType.ITYPE_DOUBLE:
                                {
                                    result = setValue(srcValue.asDouble());
                                    break;
                                }
                            case TBaseType.ITYPE_SINGLE:
                                {
                                    result = setValue(srcValue.asSingle());
                                    break;
                                }
                            case TBaseType.ITYPE_BOOL:
                                {
                                    result = setValue(srcValue.asBool());
                                    break;
                                }
                            default:
                                {
                                    result = setValue(srcValue.asStr());
                                    break;
                                }
                        }
                    }
                    catch
                    {
                        throw (new Exception("setValue() cannot convert " + srcValue.asStr() + " to " + FBaseType.ToString()));
                    }
                }
                else
                {
                    if (FIsArray)
                    {
                        setElementCount(srcValue.count());
                    }
                    uint iCount = count();
                    for (uint Idx = 1; Idx <= iCount; Idx++)
                    {
                        result = item(Idx).setValue(srcValue.item(Idx));
                    }
                }
            }
            return result;
        }
        //======================================================================
        /// <summary>
        /// Uses the xml text to build the type. Called by the descendant constructor.
        /// </summary>
        /// <param name="sXML">XML text description.</param>
        // N.Herrmann Apr 2002
        //======================================================================
        protected void buildType(String sXML)
        {
            parser = new TSDMLParser(sXML);    //create a parser, also reads description fields
            getDescription();                  //set the description fields from the parser

            parseType();                       //do the parsing of this type
            parser = null;
        }
        //======================================================================
        /// <summary>
        /// Uses the parents parser and the base node for this this type to build the
        /// type. Called by the descendant constructor.
        /// </summary>
        /// <param name="parentParser">Pointer to the parents parser.</param>
        /// <param name="baseNode">DOM Node to use as the root node.</param>
        // N.Herrmann Apr 2002
        //======================================================================
        protected void buildType(TSDMLParser parentParser, XmlNode baseNode)
        {
            XmlNode parentsNode;

            parser = parentParser;              //use the parent's parser
            parentsNode = parentParser.rootNode(); //store

            parser.setTopNode(baseNode);
            parser.getDescription();            //gets the values from the dom into the parser fields
            getDescription();                   //set the description fields from the parser
            parseType();                        //do the parsing of this type

            parser = null;                      //has only been borrowed
            parentParser.setTopNode(parentsNode);   //restore the topElement of the parser
        }
        //======================================================================
        /// <summary>
        /// Finds the array item or field corresponding to the given index.
        /// </summary>
        /// <param name="index">Index of the member of this typed value. 1 -> x</param>
        /// <returns>The typed value. null if not found.</returns>
        // N.Herrmann Apr 2002
        //======================================================================
        public TTypedValue item(uint index)
        {
            TTypedValue result;

            result = null;
            if (FIsScalar && (index == 1))                       // Item(1) for a scalar is the scalar itself
                result = this;
            else if ((index <= FMembers.Count) && (index > 0))                 // records and arrays
                result = FMembers[(int)index - 1];       // N.B. 1-offset indexing
            else if (FIsArray && (index == 0))
                if (childTemplate != null)
                    result = childTemplate;
                else if (FMembers.Count > 0)
                    result = FMembers[0];

            return result;
        }
        //==============================================================================
        /// <summary>
        /// Delete an element from an array. Assumes that 'index' is the natural order
        /// of the items in the FMembers list.
        /// </summary>
        /// <param name="index">Array index 1->x</param>
        /// <returns></returns>
        // N.Herrmann Feb 2003
        //==============================================================================
        public void deleteElement(int index)
        {
            if (FIsArray)
            {
                if ((FMembers.Count > 0) && (FMembers.Count >= index))
                {
                    //delete some elements (newsize>=0)
                    if (FMembers.Count == 1)
                        childTemplate = FMembers[index - 1]; //store the last element locally for cloning later
                    FMembers.RemoveAt(index - 1);   //delete it from the list
                }
            }
        }
        //============================================================================
        /// <summary>
        /// For arrays, this will adjust the size of the FMembers list.
        /// </summary>
        /// <param name="dim">New dimension of this array.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public void setElementCount(uint dim)
        {
            if (FIsArray)
            {
                if (dim > FMembers.Count)
                {
                    //add some more elements
                    while (dim > FMembers.Count)
                    {
                        //add a copy of the first element (structure)
                        if (FMembers.Count > 0)
                        {  //if there is an element to clone
                            newMember(item(1));      //Clones (copy constructor) the element structure
                        }
                        else
                        {
                            if (childTemplate != null)
                            {    //if previously stored an item that was removed when setElementCount(0)
                                addMember(childTemplate);
                                childTemplate = null; //now belongs to the list
                            }
                            else
                                addScalar("", FBaseType); //else determine what type the first element should be (must be a scalar)
                        }
                    }
                }
                else if (dim < FMembers.Count)
                {
                    while ((dim < FMembers.Count) && (FMembers.Count > 0))
                    {
                        //delete some elements (newsize>=0)
                        deleteElement(FMembers.Count);  //1 based
                    }
                }
            }
            else
                throw (new TypeMisMatchException("Cannot add or remove an array member to a non-array type."));
        }
        //============================================================================
        /// <summary>
        /// Only allowed to add members to records and arrays.
        /// </summary>
        /// <param name="newMember">The new member to add to this structure.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public void addMember(TTypedValue newMember)
        {
            if (((FIsArray || FIsRecord)) && (newMember != null))
            {
                if (FIsArray && ((FBaseType >= TBaseType.ITYPE_INT1) && (FBaseType <= TBaseType.ITYPE_DOUBLE))) //if number type
                    newMember.setUnits(FUnit);
                FMembers.Add(newMember);
            }
        }

        //============================================================================
        /// <summary>
        /// The new member to add to this structure.
        /// </summary>
        /// <param name="typedValue">Typed value to copy.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        protected void initTypeCopy(TTypedValue typedValue)
        {
            uint i;

            Name = typedValue.Name;
            FBaseType = typedValue.baseType();
            FIsScalar = typedValue.isScalar();
            FIsArray = typedValue.isArray();
            FIsRecord = typedValue.isRecord();
            setUnits(typedValue.units());

            if (FIsScalar)
            {
                createScalar();
                switch (FBaseType)
                {                                           //For scalars, copy the value data.
                    case TBaseType.ITYPE_INT1:                                            //Data pertaining to arrays and records
                    case TBaseType.ITYPE_INT2:                                            //   is ultimately stored in their
                    case TBaseType.ITYPE_INT4:                                            //   constituent scalars
                    case TBaseType.ITYPE_INT8: setValue(typedValue.asInt()); break;
                    case TBaseType.ITYPE_SINGLE: setValue(typedValue.asSingle()); break;
                    case TBaseType.ITYPE_DOUBLE: setValue(typedValue.asDouble()); break;
                    case TBaseType.ITYPE_BOOL: setValue(typedValue.asBool()); break;
                    case TBaseType.ITYPE_CHAR:
                    case TBaseType.ITYPE_WCHAR: setValue(typedValue.asChar()); break;
                    case TBaseType.ITYPE_STR:
                    case TBaseType.ITYPE_WSTR: setValue(typedValue.asStr()); break;
                }
            }
            else if (FIsArray || FIsRecord)
            {
                uint iCount = typedValue.count();
                if (FIsArray && (iCount == 0))
                {
                    if (typedValue.item(0) != null)
                        newMember(typedValue.item(0));
                    setElementCount(0);
                }
                else
                    for (i = 1; i <= iCount; i++)
                        newMember(typedValue.item(i)); //clones and adds this typed value
            }
        }
        //============================================================================
        /// <summary>
        /// Retrieves the TTypedValue as an integer.
        /// Will also read shorter types of numbers and return them as integers.
        /// On error an exception is thrown.
        /// </summary>
        /// <returns>An integet value.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public int asInteger()
        {
            return asInt();
        }
        //============================================================================
        /// <summary>
        /// Return an array of integers.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public int[] asIntArray()
        {
            uint iCount = count();
            int[] data = new int[0];
            if (FIsArray && (iCount > 0) )
            {
                data = new int[iCount];
                if (item(1).isScalar())
                {
                    for (uint i = 1; i <= iCount; i++)
                    {
                        data[i-1] = item(i).asInt();
                    }
                }
            }
            return data;
        }
        //============================================================================
        /// <summary>
        /// Retrieves the TTypedValue as an integer.
        /// Will also read shorter types of numbers and return them as integers.
        /// On error an exception is thrown.
        /// </summary>
        /// <returns></returns>
        //============================================================================
        public int asInt32()
        {
            return asInt();
        }
        //============================================================================
        /// <summary>
        /// Return an array of integers.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public int[] asInt32Array()
        {
            return asIntArray();
        }
        //============================================================================
        /// <summary>
        /// Retrieves the TTypedValue as an integer.
        /// Will also read shorter types of numbers and return them as integers.
        /// On error an exception is thrown.
        /// </summary>
        /// <returns>An integet value.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public int asInt()
        {
            String errorMsg = "";
            int value = 0;

            if (FIsScalar && (FData != null))
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_INT4: value = BitConverter.ToInt32(FData, 0); break;
                    case TBaseType.ITYPE_INT1: value = FData[0]; break;
                    case TBaseType.ITYPE_INT2: value = BitConverter.ToInt16(FData, 0); break;
                    //??                 case TBaseType.ITYPE_INT8: value = BitConverter.ToInt32(FData); break;
                    case TBaseType.ITYPE_BOOL: { if (asBool()) value = 1; else value = 0; } break;
                    case TBaseType.ITYPE_DOUBLE:
                    case TBaseType.ITYPE_SINGLE:
                    case TBaseType.ITYPE_STR:
                    case TBaseType.ITYPE_WSTR:
                        value = Convert.ToInt32(Math.Floor(asDouble() + 0.5)); break;

                    default:
                        {
                            errorMsg = "Cannot convert " + sTYPECODES[(int)FBaseType] + " TTypedValue to an integer value.";
                            throw (new TypeMisMatchException(errorMsg));
                        }
                }
            }
            else
            {
                errorMsg = "Cannot retrieve " + sTYPECODES[(int)FBaseType] + " TTypedValue as an integer value.";
                throw (new TypeMisMatchException(errorMsg));
            }

            return value;
        }
        //============================================================================
        /// <summary>
        /// Return an array of Singles.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public float[] asSingleArray()
        {
            uint iCount = count();
            float[] data = new float[0];
            if (FIsArray && (iCount > 0))
            {
                data = new float[iCount];
                if (item(1).isScalar())
                {
                    for (uint i = 1; i <= iCount; i++)
                    {
                        data[i - 1] = item(i).asSingle();
                    }
                }
            }
            return data;
        }
        //============================================================================
        /// <summary>
        /// The value of this scalar as a float.
        /// </summary>
        /// <returns>Floating point value.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public float asSingle()
        {
            String errorMsg = "";
            float value = 0.0f;

            if (FIsScalar && (FData != null))
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_DOUBLE: value = Convert.ToSingle(asDouble());    break;
                    case TBaseType.ITYPE_SINGLE: value = BitConverter.ToSingle(FData, 0); break;
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8: value = asInt(); break;
                    case TBaseType.ITYPE_BOOL: { if (asBool()) value = 1; else value = 0; } break;
                    case TBaseType.ITYPE_STR:
                        {
                            String buf;
                            buf = asStr();
                            if (buf.Length < 1)
                                buf = "0";
                            value = Convert.ToSingle(buf);
                        }
                        break;
                    default:
                        {
                            errorMsg = "Cannot convert " + sTYPECODES[(int)FBaseType] + " TTypedValue to a float value.";
                            throw (new TypeMisMatchException(errorMsg));
                        }
                }
            }
            else
            {
                errorMsg = "Cannot retrieve " + sTYPECODES[(int)FBaseType] + " TTypedValue as a float value.";
                throw (new TypeMisMatchException(errorMsg));
            }

            return value;
        }
        //============================================================================
        /// <summary>
        /// Return an array of Doubles.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public double[] asDoubleArray()
        {
            uint iCount = count();
            double[] data = new double[0];
            if (FIsArray && (iCount > 0))
            {
                data = new double[iCount];
                if (item(1).isScalar())
                {
                    for (uint i = 1; i <= iCount; i++)
                    {
                        data[i - 1] = item(i).asDouble();
                    }
                }
            }
            return data;
        }
        //============================================================================
        /// <summary>
        /// The value of this scalar as a double.
        /// </summary>
        /// <returns>Double precision value.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public double asDouble()
        {
            String errorMsg = "";
            double value = 0.0;

            if (FIsScalar && (FData != null))
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_DOUBLE: value = BitConverter.ToDouble(FData, 0); break;
                    case TBaseType.ITYPE_SINGLE: value = asSingle(); break;
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8: value = asInt(); break;
                    case TBaseType.ITYPE_BOOL: { if (asBool()) value = 1; else value = 0; } break;
                    case TBaseType.ITYPE_STR:
                        {
                            string buf;
                            buf = asStr();
                            if (buf.Length < 1)
                                buf = "0";
                            value = Convert.ToDouble(buf);
                        }
                        break;
                    default:
                        {
                            errorMsg = "Cannot convert " + sTYPECODES[(int)FBaseType] + " TTypedValue to a double.";
                            throw (new TypeMisMatchException(errorMsg));
                        }
                }
            }
            else
            {
                errorMsg = "Cannot retrieve " + sTYPECODES[(int)FBaseType] + " TTypedValue as a double.";
                throw (new TypeMisMatchException(errorMsg));
            }
            return value;
        }
        //============================================================================
        /// <summary>
        /// Return an array of Booleans.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public Boolean[] asBoolArray()
        {
            uint iCount = count();
            Boolean[] data = new Boolean[0];
            if (FIsArray && (iCount > 0))
            {
                data = new Boolean[iCount];
                if (item(1).isScalar())
                {
                    for (uint i = 1; i <= iCount; i++)
                    {
                        data[i - 1] = item(i).asBool();
                    }
                }
            }
            return data;
        }
        //============================================================================
        /// <summary>
        /// Return an array of Booleans.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public Boolean[] asBooleanArray()
        {
            return asBoolArray();
        }
        //============================================================================
        /// <summary>
        /// Returns false if value is 0. Returns true if anything else.
        /// Reads other interger values and interprets them.
        /// On error an exception is thrown.
        /// </summary>
        /// <returns>Value as true or false.</returns>
        //============================================================================
        public Boolean asBoolean()
        {
            return asBool();
        }
        //============================================================================
        /// <summary>
        /// Returns false if value is 0. Returns true if anything else.
        /// Reads other interger values and interprets them.
        /// On error an exception is thrown.
        /// </summary>
        /// <returns>Value as true or false.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public Boolean asBool()
        {
            String errorMsg;
            Boolean value = false;

            if (FIsScalar && (FData != null))
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_BOOL: value = BitConverter.ToBoolean(FData, 0); break;
                    case TBaseType.ITYPE_INT1:
                    case TBaseType.ITYPE_INT2:
                    case TBaseType.ITYPE_INT4:
                    case TBaseType.ITYPE_INT8: { if (asInt() == 0) value = false; else value = true; } break;
                    case TBaseType.ITYPE_CHAR:
                    case TBaseType.ITYPE_WCHAR: { if (asStr().ToLower() == "t") value = true; } break;
                    case TBaseType.ITYPE_WSTR:
                    case TBaseType.ITYPE_STR: { if (Char.ToLower(asStr()[0]) == 't') value = true; } break;
                    default:
                        {
                            errorMsg = "Cannot convert " + sTYPECODES[(int)FBaseType] + " TTypedValue to a boolean value.";
                            throw (new TypeMisMatchException(errorMsg));
                        }
                }
            }
            else
            {
                errorMsg = "Cannot retrieve " + sTYPECODES[(int)FBaseType] + " TTypedValue as a boolean value.";
                throw (new TypeMisMatchException(errorMsg));
            }
            return value;
        }
        //============================================================================
        /// <summary>
        /// Returns the character. On error an exception is thrown.
        /// <para>Conversions: Bool -> 'true'/'false', String -> asStr()[0] .</para>
        /// </summary>
        /// <returns>Character value.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public Char asChar()
        {
            String errorMsg = "";
            Char value = '\0';

            if (FIsScalar && (FData != null))
            {
                switch (FBaseType)
                {
                    case TBaseType.ITYPE_BOOL: { if (asBool()) value = 'T'; else value = 'F'; } break;
                    case TBaseType.ITYPE_CHAR:
                    case TBaseType.ITYPE_WCHAR: value = BitConverter.ToChar(FData, 0); break;
                    case TBaseType.ITYPE_WSTR:
                    case TBaseType.ITYPE_STR: value = asStr()[0]; break;
                    default:
                        {
                            errorMsg = "Cannot convert " + sTYPECODES[(int)FBaseType] + " TTypedValue to a character value.";
                            throw (new TypeMisMatchException(errorMsg));
                        }
                }
            }
            else
            {
                errorMsg = "Cannot retrieve " + sTYPECODES[(int)FBaseType] + " TTypedValue as a boolean value.";
                throw (new TypeMisMatchException(errorMsg));
            }
            return value;
        }
        //============================================================================
        /// <summary>
        /// Return an array of Booleans.
        /// </summary>
        /// <returns>Returns and array of zero length if this is not array of scalars
        /// with at least one element.</returns>
        //============================================================================
        public String[] asStringArray()
        {
            uint iCount = count();
            String[] data = new String[0];
            if (FIsArray && (iCount > 0))
            {
                data = new String[iCount];
                if (item(1).isScalar())
                {
                    for (uint i = 1; i <= iCount; i++)
                    {
                        data[i - 1] = item(i).asStr();
                    }
                }
            }
            return data;
        }
        //============================================================================
        /// <summary>
        /// Gets the text value for this scalar typed value from the data block.
        /// </summary>
        /// <returns>The value as a string.</returns>
        //============================================================================
        public String asString()
        {
            return asStr();
        }
        //============================================================================
        /// <summary>
        /// Gets the text value for this scalar typed value from the data block.
        /// </summary>
        /// <returns>The value as a string.</returns>
        //============================================================================
        public String asStr()
        {
            uint varSize;    //number of characters (not bytes)
            String buf = "";

            if (FIsScalar && (FData != null))
            {      //char strings (str) are scalars
                if (FBaseType == TBaseType.ITYPE_STR)
                {
                    //should be able to get the data from the data block
                    varSize = getDimension(FData, 0);
                    buf = ascii.GetString(FData, 4, (int)varSize);
                }
                if (FBaseType == TBaseType.ITYPE_WSTR)
                {
                    //Wide strings have x * 2 bytes
                    varSize = getDimension(FData, 0);
                    System.Text.UnicodeEncoding uni = new System.Text.UnicodeEncoding();
                    buf = uni.GetString(FData, 4, (int)varSize * 2);
                }
                else if ((FBaseType == TBaseType.ITYPE_CHAR ||
                              FBaseType == TBaseType.ITYPE_WCHAR))
                    buf = asChar().ToString();
                else if ((FBaseType == TBaseType.ITYPE_DOUBLE) || //if the field is a double I can still return a string representation
                    (FBaseType == TBaseType.ITYPE_SINGLE))
                {
                    buf = asDouble().ToString("G8");
                }
                else if ((FBaseType == TBaseType.ITYPE_INT1) ||   //if the field is an int I can still return a string representation
                      (FBaseType == TBaseType.ITYPE_INT2) ||
                      (FBaseType == TBaseType.ITYPE_INT4) ||
                      (FBaseType == TBaseType.ITYPE_INT8))
                {
                    buf = asInt().ToString();
                }
                else if (FBaseType == TBaseType.ITYPE_BOOL)
                {
                    if (asBool()) buf = "true"; else buf = "false";
                }
            }
            return buf;
        }

        //============================================================================
        /// <summary>
        /// Gets the text value for this scalar typed value from the data block.
        /// This representation is intended primarily for use in writing log files.
        /// </summary>
        /// <returns>The formatted output as a string. <para>If this is a scalar then the result will
        /// be same as asStr().</para><para>An array will be [1,2,3,4,5].</para><para>Records will be
        /// [fieldname1: asStr(), fieldname2: asStr()]</para></returns>
        //============================================================================
        public String asText()
        {
            if (FIsScalar)
            {
                return asStr();
            }
            else
            {
                StringBuilder buf = new StringBuilder("[");
                uint i;
                uint iCount = count();
                for (i = 1; i <= iCount; i++)
                {
                    if (i > 1) buf.Append(", ");
                    if (isRecord())
                    {
                        buf.Append(member(i).Name);
                        buf.Append(": ");
                    }
                    buf.Append(member(i).asText());
                }
                buf.Append("]");
                return buf.ToString();
            }
        }

        //============================================================================
        /// <summary>
        /// Returns the string value of this typed value as an escaped text string.
        /// </summary>
        /// <returns>String value escaped.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public String asEscapedString()
        {
            return escapeText(asStr());
        }
        //============================================================================
        /// <summary>
        /// Escapes the special characters for storing as xml.
        /// </summary>
        /// <param name="text">The character string to escape.</param>
        /// <returns>The escaped string.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        static public String escapeText(String text)
        {
            int index;

            StringBuilder sbuf = new StringBuilder("");
            for (index = 0; index < text.Length; index++)
            {
                switch (text[index])
                {
                    case '&': sbuf.Append("&#38;"); break;
                    case '<': sbuf.Append("&#60;"); break;
                    case '>': sbuf.Append("&#62;"); break;
                    case '"': sbuf.Append("&#34;"); break;
                    case '\'': sbuf.Append("&#39;"); break;
                    default:
                        {
                            // If it is none of the special characters, just copy it
                            sbuf.Append(text[index]);
                        } break;
                }
            }

            return sbuf.ToString();
        }
        //============================================================================
        /// <summary>
        /// The type of this value as a character string.
        /// </summary>
        /// <returns>Type string. <![CDATA[ <see cref="TypeSpec">TypeSpec Class</see> ]]> </returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public String typeName()
        {
            return sTYPECODES[(int)FBaseType];
        }
        //============================================================================
        /// <summary>
        /// The size of this type in bytes. For an array it includes the
        /// 4 byte header of each dimension.
        /// </summary>
        /// <returns>Integer value of the size.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public uint sizeBytes()
        {
            int i;
            uint iSize = 0;

            if (FIsScalar)
            {
                iSize = FDataSize;
            }
            else
            {
                for (i = 0; i < FMembers.Count; i++)
                {
                    iSize = iSize + FMembers[i].sizeBytes();
                }
                if (FIsArray)
                    iSize = iSize + INTSIZE;
            }

            return iSize;
        }
        //============================================================================
        /// <summary>
        /// Recursive routine for checking whether two types are (a) identical,
        /// (b) different but compatible, (c) incompatible.
        /// <para>Note:</para>
        /// <para>1. Type compatibility is not a transitive relationship.</para>
        /// <para>2. Unit compatibility needs further implementation.</para>
        /// </summary>
        /// <param name="srcValue">The TTypedValue to compare with.</param>
        /// <returns>Returns: 0 - exact match, 1 - compatible, -1 - cannot match</returns>
        //============================================================================
        public int canAssignFrom(TTypedValue srcValue)
        {
            int result = ctBAD;
            uint Idx;

            if (srcValue.isScalar())
            {
                if (!FIsScalar)
                    result = ctBAD;
                else if (srcValue.baseType() == FBaseType)
                    result = ctSAME;
                else if ((srcValue.baseType() <= TBaseType.ITYPE_INT8) && (srcValue.baseType() >= TBaseType.ITYPE_INT1) &&
                         (FBaseType <= TBaseType.ITYPE_INT8) && (FBaseType >= TBaseType.ITYPE_INT1))
                    result = ctCOMP;  //both integers
                else if ((FBaseType >= TBaseType.ITYPE_SINGLE) && (FBaseType <= TBaseType.ITYPE_DOUBLE) &&           //These conditions are not transitive
                         (srcValue.baseType() >= TBaseType.ITYPE_INT1) && (srcValue.baseType() <= TBaseType.ITYPE_DOUBLE))
                    result = ctCOMP;  //can match an int/single source to single/double destination
                else if ((srcValue.baseType() == TBaseType.ITYPE_CHAR) &&
                    ((FBaseType == TBaseType.ITYPE_WCHAR) ||
                    (FBaseType == TBaseType.ITYPE_STR) ||
                    (FBaseType == TBaseType.ITYPE_WSTR)))
                    result = ctCOMP;
                else if ((srcValue.baseType() == TBaseType.ITYPE_WCHAR) && (FBaseType == TBaseType.ITYPE_WSTR))
                    result = ctCOMP;
                else if ((srcValue.baseType() == TBaseType.ITYPE_STR) && (FBaseType == TBaseType.ITYPE_WSTR))
                    result = ctCOMP;
                // A sop to the old APSIM manager, which sends out all request-set values as strings
                else if ((srcValue.baseType() == TBaseType.ITYPE_STR) || (FBaseType == TBaseType.ITYPE_WSTR))
                    result = ctDODGY;
                else
                    result = ctBAD;

                if ((FBaseType >= TBaseType.ITYPE_INT1) && (FBaseType <= TBaseType.ITYPE_DOUBLE) &&
                      (!unitsMatch(units(), srcValue.units())))
                    result = ctBAD;
            }
            else if (srcValue.isArray())
            {   //an array
                if (!FIsArray)
                    result = ctBAD;
                else
                {
                    if (count() == 0)
                        setElementCount(1);  //addElement();
                    if (srcValue.count() == 0)
                        srcValue.setElementCount(1); //addElement();
                    result = member(1).canAssignFrom(srcValue.member(1));
                }
            }
            else
            {   //a record
                if (!isRecord())
                    result = ctBAD;
                else
                {
                    uint iCount = count();
                    result = ctCOMP;                                                        // First, test for identity
                    if (iCount == srcValue.count())
                    {
                        result = ctSAME;
                        for (Idx = 1; Idx <= iCount; Idx++)
                        {
                            if ((member(Idx).Name.ToLower() != srcValue.member(Idx).Name.ToLower()) ||
                                  (member(Idx).canAssignFrom(srcValue.member(Idx)) != ctSAME))
                                result = ctCOMP;
                        }
                    }


                    if (result == ctCOMP)
                    {                                                //If not same, test for compatibility
                        String elemName;
                        for (Idx = 1; Idx <= srcValue.count(); Idx++)
                        {
                            elemName = srcValue.member(Idx).Name;                 //field name
                            if (!hasField(elemName) ||
                                  (member(elemName).canAssignFrom(srcValue.member(Idx)) == ctBAD))
                                result = ctBAD;
                        }
                    }
                }
            }

            return result;

        }

        //============================================================================
        /// <summary>
        /// Returns TRUE i.f.f. the two unit strings have the same dimension and
        /// identical scale.
        /// </summary>
        /// <param name="sUnitA">Unit name one.</param>
        /// <param name="sUnitB">Unit name two.</param>
        /// <returns>True if matched.</returns>
        // N.B. this is a temporary implementation.
        // A.Moore
        //============================================================================
        static protected Boolean unitsMatch(String sUnitA, String sUnitB)
        {
            int i;
            Boolean result = false;

            // APSIM has historically sometimes encased units in parentheses
            // Get rid of these before proceding
            String sUnit1 = stripOuterParens(ref sUnitA);
            String sUnit2 = stripOuterParens(ref sUnitB);

            if (sUnit1 == sUnit2)
                result = true;
            else if ((sUnit1.Length == 0) || (sUnit2.Length == 0))       //The null string matches any unit
                result = true;
            else
            {    //Search the lookup table of matching units
                i = 0;
                while (!result && (i < UNITMATCHES.Length))
                {
                    if ((sUnit1 == UNITMATCHES[i].unit1) && (sUnit2 == UNITMATCHES[i].unit2))
                        result = true;
                    else if ((sUnit1 == UNITMATCHES[i].unit2) && (sUnit2 == UNITMATCHES[i].unit1))
                        result = true;
                    else
                        i++;
                }
            }
            return result;
        }

        static private String stripOuterParens(ref String text)
        {
            if (text.Length > 2 && text[0] == '(' && text[text.Length - 1] == ')')
                return text.Substring(1, text.Length - 2);
            else
                return text;
        }


        //============================================================================
        /// <summary>
        /// Tests for identity of two TTypedValue objects.
        /// </summary>
        /// <param name="otherValue">Typed value to test against this one.</param>
        /// <returns>True if it matches in type, size, and structure.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public Boolean equals(TTypedValue otherValue)
        {
            uint i;
            Boolean bEqual = false;

            if ((otherValue != null) &&
               (FBaseType == otherValue.baseType()) &&
               (FIsArray == otherValue.isArray()) &&
               (FIsRecord == otherValue.isRecord()) &&
               (count() == otherValue.count()) &&
               (FDataSize == otherValue.sizeBytes()))
                bEqual = true;

            if (bEqual)
            {
                if (FIsScalar)
                    bEqual = bEqual && (asStr() == otherValue.asStr());    //str comparison of the scalar (needs refinement)
            }
            else
            {
                for (i = 1; i <= count(); i++)
                    bEqual = bEqual && item(i).equals(otherValue.item(i));
            }
            return bEqual;
        }
        //============================================================================
        /// <summary>
        ///
        /// </summary>
        /// <param name="sName">Name of the field to find.</param>
        /// <returns>Returns TRUE if the value is a record and it has the nominated field.</returns>
        // N.Herrmann Apr 2002
        //============================================================================
        public Boolean hasField(String sName)
        {
            Boolean result = false;
            try
            {
                if (member(sName) != null)
                    result = true;
            }
            catch
            {
                result = false;
            }
            return result;
        }

        //============================================================================
        /// <summary>
        /// Copies the data from a data block into this scalar.
        /// </summary>
        /// <param name="newData">The data block to copy into this scalar.</param>
        /// <param name="startIndex">Start at this index in the byte array.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public Boolean copyDataBlock(Byte[] newData, uint startIndex)
        {
            Boolean copyOK = true;

            if ((FBaseType >= TBaseType.ITYPE_EMPTY) && (FBaseType <= TBaseType.ITYPE_WCHAR))
                FDataSize = typeSize[(int)FBaseType];
            else if (FBaseType == TBaseType.ITYPE_STR)
            {
                uint noChars = getDimension(newData, startIndex);
                FDataSize = INTSIZE + noChars;
            }
            else if (FBaseType == TBaseType.ITYPE_WSTR)
            {
                uint noChars = getDimension(newData, startIndex);
                FDataSize = INTSIZE + 2 * noChars;//size in bytes
            }

            if ((startIndex + FDataSize) <= newData.Length) //if there are enough bytes to copy
            {
                FData = new Byte[FDataSize];
                Array.Copy(newData, startIndex, FData, 0, FDataSize);
            }
            else
            {
                copyOK = false;
            }
            return copyOK;
        }
        //============================================================================
        /// <summary>
        /// Gets the datablock containing all the values from this TTypedValue.
        /// Assumes that memory has already been allocated for the data to be copied
        /// into.
        /// </summary>
        /// <param name="data">Location to copy data to.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public void getData(ref Byte[] data)
        {
            Byte[] tmpPtr;
            uint iStart = 0;

            tmpPtr = data;    //store the pointer position because it gets shifted
            copyData(data, ref iStart);   //do the copy
            data = tmpPtr;    //restore the pointer to the start of the memory block
        }
        //============================================================================
        /// <summary>
        /// Copies the data from a block of memory owned by someone else.
        /// Overrides setData() and assumes startIndex = 0.
        /// </summary>
        /// <param name="newData">The new data to copy into this typed value.</param>
        /// <param name="iNewSize">The size of the source typed value.</param>
        //============================================================================
        public int setData(Byte[] newData, int iNewSize)
        {
            return setData(newData, iNewSize, 0);
        }
        //============================================================================
        /// <summary>
        /// Copies the data from a block of memory owned by someone else.
        /// </summary>
        /// <param name="newData">The new data to copy into this typed value.</param>
        /// <param name="iNewSize">The size of the source typed value.</param>
        /// <param name="startIndex">Start at this index in the byte array.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public int setData(Byte[] newData, int iNewSize, uint startIndex)
        {
            uint dim;
            int i;
            int bytesRemain;
            TTypedValue value;
            int childSize;
            int itemSize = 0;

            bytesRemain = 0;
            if ((iNewSize > 0) && (newData != null))
            {         //if the incoming block has data
                if (FIsScalar)
                {
                    copyDataBlock(newData, startIndex); //copies scalars (including strings)
                    itemSize = (int)FDataSize;
                }
                else
                {
                    bytesRemain = iNewSize;      //keep count of bytes
                    if (FIsArray)
                    {
                        //get the DIM=x value from the datablock
                        dim = getDimension(newData, startIndex);
                        if (dim != count())
                            setElementCount(dim);   //create or delete child elements
                        bytesRemain = iNewSize - (int)INTSIZE;
                        startIndex += INTSIZE;
                        itemSize = (int)INTSIZE;
                    }
                    //now copy the children. All children exist now
                    //go through each child and set the data block
                    i = 0;
                    while (i < FMembers.Count)
                    {             //for each field
                        value = FMembers[i];
                        childSize = value.setData(newData, bytesRemain, startIndex);             //store the datablock
                        itemSize += childSize;
                        bytesRemain = bytesRemain - (int)childSize;
                        startIndex += (uint)childSize;                      //inc ptr along this dataBlock
                        i++;
                    }
                    //store the size in bytes for this type
                    //FDataSize = (uint)(iNewSize - Math.Max(0, bytesRemain));   //bytesRemain should = 0
                }
            }
            else if (FIsArray)
            {
                setElementCount(0);
            }
            //   if bytesRemain <> 0 then
            //      raise Exception.Create( 'Input data inconsistent with type of value in setData()' + Name );

            return itemSize;
        }


        //============================================================================
        /// <summary>
        /// Copies the FData of this type and any children into the memory already
        /// allocated. Called recursively to fill with data from the children.
        /// </summary>
        /// <param name="dataPtr">The location to copy to.</param>
        /// <param name="startIndex">Start at this index in the byte array.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public void copyData(Byte[] dataPtr, ref uint startIndex)
        {
            uint idx;

            if (FIsScalar)
            {  //scalars (and strings) are one block of data at this point
                Array.Copy(FData, 0, dataPtr, startIndex, FDataSize);
                startIndex += FDataSize;                         //move along so other scalars can follow
            }
            else
            {
                uint dim = count(); //store the array dimension
                if (FIsArray)
                {   //arrays have a dimension header and then the data blocks following
                    dataPtr[startIndex] = (Byte)dim;
                    dataPtr[startIndex + 1] = (Byte)(dim >> 8);
                    dataPtr[startIndex + 2] = (Byte)(dim >> 16);
                    dataPtr[startIndex + 3] = (Byte)(dim >> 24);
                    startIndex += INTSIZE;       //move along so the array items follow
                }
                if (FIsRecord || FIsArray)      //if this is a value that has children
                {
                    for (idx = 1; idx <= dim; idx++)     //for each member/element
                        item(idx).copyData(dataPtr, ref startIndex);       //var dataPtr. Ptr get moved along with each copy.
                }
            }
        }

        //============================================================================
        /// <summary>
        /// Copies data from one type to this type using the getData() setData() pair.
        /// Assumes that the source and destination are exactly compatible. Arrays will be
        /// resized as required.
        /// </summary>
        /// <param name="srcValue">The source value.</param>
        // adapted from A. Moore 2002
        //============================================================================
        public void copyFrom(TTypedValue srcValue)
        {
            if ((srcValue != null))
            {
                uint iSize = srcValue.sizeBytes();
                if (iSize > 0)
                {
                    Byte[] data = new Byte[iSize];
                    srcValue.getData(ref data);
                    setData(data, (int)iSize, 0);
                }
            }
        }


        //============================================================================
        /// <summary>
        /// Copies the data from a data block into this scalar.
        /// Overloaded version, taking data from an IntPtr
        /// </summary>
        /// <param name="newData">The data block to copy into this scalar.</param>
        /// <param name="startIndex">Start at this index in the byte array.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public void copyDataBlock(IntPtr newData, uint startIndex)
        {
            if ((FBaseType >= TBaseType.ITYPE_EMPTY) && (FBaseType <= TBaseType.ITYPE_WCHAR))
                FDataSize = typeSize[(int)FBaseType];
            else if (FBaseType == TBaseType.ITYPE_STR)
            {
                uint noChars = (uint)Marshal.ReadInt32(newData, (int)startIndex);
                FDataSize = INTSIZE + noChars;
            }
            else if (FBaseType == TBaseType.ITYPE_WSTR)
            {
                uint noChars = (uint)Marshal.ReadInt32(newData, (int)startIndex);
                FDataSize = INTSIZE + 2 * noChars;//size in bytes
            }

            FData = new Byte[FDataSize];
            for (int i = 0; i < FDataSize; i++)
                FData[i] = Marshal.ReadByte(newData, (int)startIndex + i);
        }

        //============================================================================
        /// <summary>
        /// Copies the data from a block of memory owned by someone else.
        /// Overloaded version, taking data from an IntPtr
        /// </summary>
        /// <param name="newData">The new data to copy into this typed value.</param>
        /// <param name="startIndex">Start at this index in the byte array.</param>
        // N.Herrmann Apr 2002
        //============================================================================
        public int setData(IntPtr newData, uint startIndex)
        {
            uint dim;
            int i;
            TTypedValue value;
            int childSize;
            int itemSize = 0;

            if (!newData.Equals(IntPtr.Zero)) {                   //if the incoming block has data
                if (FIsScalar) {
                    copyDataBlock(newData, startIndex);    //copies scalars (including strings)
                    itemSize = (int)FDataSize;
                }
                else {
                    if (FIsArray) {
                        dim = (uint) Marshal.ReadInt32(newData, (int)startIndex);      //get the DIM=x value from the datablock
                        if (dim != count())
                            setElementCount(dim);   //create or delete child elements
                        startIndex += INTSIZE;
                        itemSize = (int)INTSIZE;
                    }
                    //now copy the children. All children exist now
                    //go through each child and set the data block
                    for (i = 0; i < FMembers.Count; i++) {      //for each field
                        value = FMembers[i];
                        childSize = value.setData(newData, startIndex);                      //store the datablock
                        itemSize+= childSize;
                        startIndex += (uint)childSize;                    //inc ptr along this dataBlock
                    }
                }
            }
            else if (FIsArray) {
                setElementCount(0);
            }
            return itemSize;
        }
    }

    //============================================================================
    /// <summary>
    /// Thrown when a type mismatch occurs.
    /// For example: attempting to access an array in the manner of a scalar.
    /// </summary>
    //============================================================================
    [Serializable]
    public class TypeMisMatchException : ApplicationException
    {
        //============================================================================
        /// <summary>
        /// Create an exception that specifies a type mis match
        /// </summary>
        /// <param name="message"></param>
        //============================================================================
        public TypeMisMatchException(string message)
            : base(message)
        {
        }
        //============================================================================
        /// <summary>
        /// Constructor that will show details of the two types causing the problem.
        /// </summary>
        /// <param name="first">First TTypedValue.</param>
        /// <param name="second">Second TTypedValue.</param>
        //============================================================================
        public TypeMisMatchException(TTypedValue first, TTypedValue second)
            : base("Type mismatch exception: " + first.typeName() + " does not match " + second.typeName())
        {

        }
    }
    //============================================================================
    /// <summary>
    /// Thrown when an array item is out of range.
    /// </summary>
    //============================================================================
    [Serializable]
    public class ArrayIndexException : ApplicationException
    {
        //============================================================================
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="message">Exception message.</param>
        //============================================================================
        public ArrayIndexException(string message)
            : base(message)
        {
        }
    }
}
#pragma warning restore CS1591