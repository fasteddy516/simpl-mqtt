using System;


namespace SimplMQTT.Client.Exceptions
{
    public class MqttConnectionException : Exception
    {
        public MqttConnectionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
