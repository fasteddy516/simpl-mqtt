using System;

using Crestron.SimplSharp;


namespace SimplMQTT.Client.Exceptions
{
    /// <summary>
    /// Exception due to error communicating with broker on socket
    /// </summary>
    public class MqttCommunicationException : Exception
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public MqttCommunicationException()
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="e">Inner Exception</param>
        public MqttCommunicationException(Exception e)
            : base(String.Empty, e)
        {
        }
    }
}
