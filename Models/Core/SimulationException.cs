using System;
using System.Runtime.Serialization;

namespace Models.Core
{
    /// <summary>
    /// An exception thrown during a simulation run.
    /// </summary>
    [Serializable]
    public class SimulationException : Exception, ISerializable
    {
        /// <summary>
        /// Name of the simulation in which the error was thrown.
        /// </summary>
        public string SimulationName { get; private set; }

        /// <summary>
        /// Name of the file containing the simulation.
        /// </summary>
        public string FileName { get; private set; }

        /// <summary>
        /// Constructor provided for binary deserialization.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        [Obsolete]
        public SimulationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            SimulationName = info.GetString(nameof(SimulationName));
            FileName = info.GetString(nameof(FileName));
        }

        /// <summary>
        /// /// Create a <see cref="SimulationException" /> instance.
        /// </summary>
        /// <param name="message">Error message.</param>
        /// <param name="simulationName">Name of the simulation in which the error was thrown.</param>
        /// <param name="fileName">Name of the file containing the simulation.</param>
        public SimulationException(string message, string simulationName, string fileName) : base(message)
        {
            SimulationName = simulationName;
            FileName = fileName;
        }

        /// <summary>
        /// /// Create a <see cref="SimulationException" /> instance.
        /// </summary>
        /// <param name="message">Error message.</param>
        /// <param name="innerException">Inner exception data.</param>
        /// <param name="simulationName">Name of the simulation in which the error was thrown.</param>
        /// <param name="fileName">Name of the file containing the simulation.</param>
        public SimulationException(string message, Exception innerException, string simulationName, string fileName) : base(message, innerException)
        {
            SimulationName = simulationName;
            FileName = fileName;
        }

        /// <summary>
        /// Convert to string.
        /// </summary>
        public override string ToString()
        {
            return $"ERROR in file: {FileName}{Environment.NewLine}Simulation name: {SimulationName}{Environment.NewLine}{base.ToString()}";
        }

        /// <summary>
        /// Get object data for serialization.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Streaming context.</param>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(nameof(SimulationName), SimulationName);
            info.AddValue(nameof(FileName), FileName);
        }
    }
}