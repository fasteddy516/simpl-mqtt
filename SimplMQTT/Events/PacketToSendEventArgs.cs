using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Crestron.SimplSharp;

using SimplMQTT.Client.Messages;


namespace SimplMQTT.Client.Events
{
    public class PacketToSendEventArgs : EventArgs
    {
        public MqttMsgBase Packet { get; private set; }

        public PacketToSendEventArgs(MqttMsgBase packet)
        {
            this.Packet = packet;
        }
    }
}