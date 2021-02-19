using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Crestron.SimplSharp;


namespace SimplMQTT.Client.Events
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public string Topic { get ; private set;}
        public string Value { get; private set; }

        /// <summary>
        /// You must define the default constructor for simpl+ to see the properties.
        /// </summary>
        public MessageReceivedEventArgs()
        {
            ;
        }

        public MessageReceivedEventArgs(string topic, string value)
        {
            this.Topic = topic;
            this.Value = value;
        }
    }
}