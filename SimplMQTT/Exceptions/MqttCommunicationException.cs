using System;


namespace SimplMQTT.Client.Exceptions
{
    public class MqttCommunicationException : Exception
    {
        public MqttCommunicationException()
        {
        }

        public MqttCommunicationException(Exception e) : base(String.Empty, e)
        {
        }
    }
}
