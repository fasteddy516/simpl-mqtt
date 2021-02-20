using System;


namespace SimplMQTT.Client.Events
{
    public class ErrorOccuredEventArgs : EventArgs
    {
        public string ErrorMessage { get; private set; }

        public ErrorOccuredEventArgs()
        {
            //You must define the default constructor for simpl+ to see the properties.
        }

        public ErrorOccuredEventArgs(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
    }
}