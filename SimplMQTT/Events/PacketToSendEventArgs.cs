using System;

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