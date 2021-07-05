using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace APSIM.Server.Cli
{
    /// <summary>
    /// Command-line options for Models.exe.
    /// </summary>
    public class ServerOptions
    {
        /// <summary>.apsimx file to hold in memory.</summary>
        [Option('f', "file", HelpText = ".apsimx file to hold in memory.", Required = true)]
        public string File { get; set; }

        /// <summary>Display verbose debugging info.</summary>
        [Option('v', "verbose", HelpText = "Display verbose debugging info")]
        public bool Verbose { get; set; }

        /// <summary>Keep the server alive after client disconnects.</summary>
        [Option('k', "keep-alive", HelpText = "Keep the server alive after client disconnects")]
        public bool KeepAlive { get; set; }

        /// <summary>This determines how data is sent/received over the socket.</summary>
        /// <remarks>
        /// When in managed mode, sent objects will be serialised. When in native mode, sent
        /// objects will be converted to a binary array. Actually, this kind of needs more thought.
        /// </remarks>
        [Option('m', "communication-mode", HelpText = "This deteremines how data is sent/received over the socket.", Default = CommunicationMode.Managed)]
        public CommunicationMode Mode { get; set; }

        /// <summary>
        /// Concrete examples shown in help text.
        /// </summary>
        [Usage]
        public static IEnumerable<Example> Examples
        {
            get
            {
                yield return new Example("Normal usage", new ServerOptions() { File = "file.apsimx" });
                yield return new Example("Comms with a native client", new ServerOptions() { File = "file.apsimx", Mode = CommunicationMode.Native });
            }
        }
    }
}
