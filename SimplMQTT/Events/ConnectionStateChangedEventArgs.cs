using System;


namespace SimplMQTT.Client.Events
{
    public class ConnectionStateChangedEventArgs : EventArgs
    {
        public ushort State { get; private set; }

        public ConnectionStateChangedEventArgs()
        {
            //You must define the default constructor for simpl+ to see the properties.
        }

        public ConnectionStateChangedEventArgs(ushort state)
        {
            State = state;
        }
    }
}