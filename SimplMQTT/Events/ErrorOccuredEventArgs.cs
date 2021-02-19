using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Crestron.SimplSharp;

namespace SimplMQTT.Client.Events
{
    public class ErrorOccuredEventArgs : EventArgs
    {
        public string ErrorMessage { get; private set; }

        public ErrorOccuredEventArgs()
        {
            ;
        }

        public ErrorOccuredEventArgs(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }
    }
}