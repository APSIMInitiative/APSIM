using System.IO;
using System.Text;
using System;
using System.Globalization;

namespace APSIM.Shared.Utilities
{
    /// <summary>
    /// A random-access stream reader.
    /// </summary>
    /// <remarks>
    /// I'm not sure that this class is actually necessary. The only caller is
    /// <see cref="ApsimTextFile"/>, and I suspect that it could be refactored
    /// to just use a StreamReader directly.
    /// </remarks>
    [Serializable]
    class StreamReaderRandomAccess
    {
        /// <summary>
        /// Maximum allowed buffer size.
        /// </summary>
        private const int maxBufferSize = 1024;

        /// <summary>
        /// The internal stream reader.
        /// </summary>
        private StreamReader file = null;

        /// <summary>
        /// The current buffer read from the file.
        /// </summary>
        private char[] buffer = new char[maxBufferSize + 1];

        /// <summary>
        /// Current position in the buffer.
        /// </summary>
        private int position = 0;

        /// <summary>
        /// Current position in the file.
        /// </summary>
        private int offset = 0;

        /// <summary>
        /// The offset of the buffer within the stream.
        /// </summary>
        private int bufferOffset = 0;

        /// <summary>
        /// The size of the buffer. Note that the actual buffer array can be
        /// larger than this, but will be padded with zeroes at the end.
        /// </summary>
        private int bufferSize = 0;

        /// <summary>
        /// Initialises a new instance of <see cref="StreamReaderRandomAccess"/>
        /// to read from a file.
        /// </summary>
        /// <param name="filename">Path to a file.</param>
        public StreamReaderRandomAccess(string filename)
        {
            Open(filename);
        }

        /// <summary>
        /// Initialises a new instance of <see cref="StreamReaderRandomAccess"/>
        /// to read from a stream.
        /// </summary>
        /// <param name="stream">A stream.</param>
        public StreamReaderRandomAccess(Stream stream)
        {
            Open(stream);
        }

        /// <summary>
        /// Current position in the stream.
        /// </summary>
        public int Position
        {
            get
            {
                return offset;
            }
            set
            {
                Seek(value, SeekOrigin.Begin);
                EndOfStream = false;
            }
        }

        /// <summary>
        /// True iff current position is at end of stream/file.
        /// </summary>
        public bool EndOfStream { get; private set; }

        /// <summary>
        /// Close the stream (and its underlying file if it has one).
        /// </summary>
        public void Close()
        {
            file.Close();
            file = null;
            position = 0;
            EndOfStream = true;
            bufferSize = 0;
        }

        /// <summary>
        /// Seek to a position in the stream.
        /// </summary>
        /// <param name="seekPosition">A byte offset relative to the origin position.</param>
        /// <param name="origin">Reference point used to obtain the new position.</param>
        public void Seek(int seekPosition, SeekOrigin origin)
        {
            EndOfStream = false;
            offset = (int)file.BaseStream.Seek(seekPosition, origin);

            file.DiscardBufferedData();

            LoadBuffer();
        }

        /// <summary>
        /// Return a string containing all characters between the current
        /// position and the next newline character, or the end of the stream,
        /// whichever comes first.
        /// </summary>
        public string ReadLine()
        {
            if (EndOfStream)
                return "";

            StringBuilder lineBuffer = new StringBuilder();

            char ch = '\0';
            bool endOfLine = false;

            while (!endOfLine)
            {
                ch = buffer[position];

                if (ch == '\r')
                {
                    // Ignore CR.
                }
                else if (ch == '\n')
                {
                    endOfLine = true;
                }
                else
                {
                    lineBuffer.Append(ch);
                }

                position++;

                // If we've reached the end of the buffer, we read a new buffer
                // from the underlying stream.
                if (position == bufferSize)
                {
                    LoadBuffer();
                    if (EndOfStream)
                        endOfLine = true;
                }
            }

            offset = bufferOffset + position;
            return lineBuffer.ToString();
        }

        /// <summary>
        /// Open a file.
        /// </summary>
        /// <param name="filename">Path to a file.</param>
        private void Open(string filename)
        {
            Open(File.OpenRead(filename));
        }

        /// <summary>
        /// Open a stream. Closes the old stream if it's not already closed.
        /// </summary>
        /// <param name="stream">A stream.</param>
        private void Open(Stream stream)
        {
            if (file != null)
                Close();

            file = new StreamReader(stream);
            position = 0;
            EndOfStream = false;
            bufferSize = 0;
            bufferOffset = 0;

            LoadBuffer();
        }

        /// <summary>
        /// Read up to <see cref="maxBufferSize"/> characters from the
        /// underyling stream and store them in <see cref="buffer"/>.
        /// </summary>
        private void LoadBuffer()
        {
            bufferOffset = Convert.ToInt32(file.BaseStream.Position, CultureInfo.InvariantCulture);
            position = 0;
            bufferSize = file.Read(buffer, 0, maxBufferSize);

            if (bufferSize == 0)
                EndOfStream = true;
        }
    }
}
