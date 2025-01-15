using System;
using System.Runtime.Serialization;

namespace Models.Core
{
    /// <summary>
    /// Apsim's exception object
    /// </summary>
    [Serializable]
    public class ApsimXException : Exception, ISerializable
    {
        /// <summary>Gets or sets the model.</summary>
        /// <value>The model.</value>
        public IModel model { get; set; }

        /// <summary>Initializes a new instance of the <see cref="ApsimXException"/> class.</summary>
        public ApsimXException()
        {

        }

        /// <summary>Initializes a new instance of the <see cref="ApsimXException"/> class.</summary>
        [Obsolete]
        protected ApsimXException(SerializationInfo info, StreamingContext context): base(info, context)
        { }

        /// <summary>Initializes a new instance of the <see cref="ApsimXException"/> class.</summary>
        /// <param name="model">The model.</param>
        /// <param name="message">The message.</param>
        public ApsimXException(IModel model, string message)
            : base(message)
        {
            this.model = model;
        }

        /// <summary>Initializes a new instance of the <see cref="ApsimXException"/> class.</summary>
        /// <param name="model">The model.</param>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The inner exception</param>
        public ApsimXException(IModel model, string message, Exception innerException)
            : base(message, innerException)
        {
            this.model = model;
        }

    }
}
