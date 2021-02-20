using System;


namespace SimplMQTT.Client.Events
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public string Topic { get ; private set;}
        public string Value { get; private set; }

        public MessageReceivedEventArgs()
        {
            //You must define the default constructor for simpl+ to see the properties.
        }

        public MessageReceivedEventArgs(string topic, string value)
        {
            this.Topic = topic;
            this.Value = value;
        }
    }
}