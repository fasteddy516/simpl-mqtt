using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;
using Crestron.SimplSharp.CrestronSockets;
#if USE_LOGGER
    using Crestron.SimplSharp.CrestronLogger;
#endif
#if USE_SSL
    using Crestron.SimplSharp.Cryptography.X509Certificates;
#endif

using SimplMQTT.Client.Events;
using SimplMQTT.Client.Exceptions;
using SimplMQTT.Client.Managers;
using SimplMQTT.Client.Messages;
using SimplMQTT.Client.Utility;


namespace SimplMQTT.Client
{
    public class MqttClient
    {
        #if USE_SSL
            private SecureTCPClient tcpClient;
        #else
            private TCPClient tcpClient;
        #endif
        private const int FIXED_HEADER_OFFSET = 2;
        private Random rand = new Random();
        private List<ushort> packetIdentifiers = new List<ushort>();
        private MqttPublisherManager publisherManager;
        private MqttSessionManager sessionManager;
        public PacketDecoder PacketDecoder { get; private set; }
        private CTimer disconnectTimer = null;
        private bool connectionRequested = false;

        private delegate void RouteControlPacketDelegate(MqttMsgBase packet);
        
        #region client properties

        public ushort KeepAlivePeriod { get; private set; }
        public Dictionary<string, byte> Subscriptions { get; set; }
        public string ClientID { get; private set; }
        public bool CleanSession { get; private set; }
        public bool WillFlag { get; internal set; }
        public byte WillQosLevel { get; internal set; }
        public string WillTopic { get; internal set; }
        public string WillMessage { get; internal set; }
        public bool WillRetain { get; internal set; }
        public static byte ProtocolVersion { get { return MqttSettings.PROTOCOL_VERSION; } }

        #endregion

        public event EventHandler<MessageReceivedEventArgs> MessageArrived;
        public event EventHandler<ErrorOccuredEventArgs> ErrorOccured;
        public event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

        #region initialize

        public MqttClient()
        {
            #if USE_LOGGER
                CrestronLogger.Mode = LoggerModeEnum.DEFAULT;
                CrestronLogger.PrintTheLog(false);
                CrestronLogger.Initialize(10);
                CrestronLogger.LogOnlyCurrentDebugLevel = false;
            #endif
        }

        public void Initialize(
            string clientID,
            string brokerAddress,
            ushort brokerPort,
            string username,
            string password,
            ushort willFlag,
            ushort willRetain,
            uint willQoS,
            string willTopic,
            string willMessage,
            uint cleanSession,
            ushort bufferSize
            #if USE_SSL
                ,string certificateFileName,
                string privateKeyFileName
            #endif
        )
        {
            MqttSettings.Instance.Username = username;
            MqttSettings.Instance.Password = password;
            MqttSettings.Instance.BufferSize = Convert.ToInt32(bufferSize);
            MqttSettings.Instance.Port = Convert.ToInt32(brokerPort);
            MqttSettings.Instance.IPAddressOfTheServer = IPAddress.Parse(brokerAddress);

            #if USE_LOGGER
                CrestronLogger.WriteToLog("Instance Settings initialized", 1);
            #endif
            
            KeepAlivePeriod = 0; // currently set to 0, as the keepalive mechanism has not been implemented
            ClientID = clientID;
            WillFlag = willFlag == 0 ? false : true;
            WillRetain = willRetain == 0 ? false : true;
            WillQosLevel = (byte)willQoS;
            WillTopic = willTopic;
            WillMessage = willMessage;
            Subscriptions = new Dictionary<string, byte>();
            CleanSession = cleanSession == 0 ? false : true;

            #if USE_LOGGER
                CrestronLogger.WriteToLog("Client settings initialized", 1);
            #endif

            try
            {
                #if USE_SSL
                    tcpClient = new SecureTCPClient(ipAddressOfTheServer.ToString(), port, bufferSize);
                    if (certificateFileName != "//" && privateKeyFileName != "//")
                    {
                        var certificate = ReadFromResource(@"NVRAM\\" + certificateFileName);
                        X509Certificate2 x509Cert = new X509Certificate2(certificate);
                        tcpClient.SetClientCertificate(x509Cert);
                        tcpClient.SetClientPrivateKey(ReadFromResource(@"NVRAM\\" + privateKeyFileName));
                    }
                #else
                    tcpClient = new TCPClient(brokerAddress.ToString(), brokerPort, bufferSize);
                #endif
                tcpClient.SocketStatusChange += this.OnSocketStatusChange;
                PacketDecoder = new PacketDecoder();
                sessionManager = new MqttSessionManager(clientID);
                publisherManager = new MqttPublisherManager(sessionManager);
                publisherManager.PacketToSend += this.OnPacketToSend;
            }
            catch (Exception e)
            {
                OnErrorOccured("ERROR DURING INITIALIZATION: " + e.Message);
            }

            #if USE_LOGGER
                CrestronLogger.WriteToLog("MQTTCLIENT - Initialize - completed : " + clientID, 1);
            #endif
        }


        #if USE_SSL
            private byte[] ReadFromResource(string path)
            {
                FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                stream.Close();
                return bytes;
            }
        #endif


        public void AddSubscription(string topic, uint qos)
        {
            try
            {
                if (qos > 2)
                    throw new ArgumentOutOfRangeException("QoS value must be in the range 0-2.");
                else
                    Subscriptions.Add(topic, (byte)qos);
            }
            catch (Exception e)
            {
                OnErrorOccured("AddTopic - Error occured : " + e.Message);
            }
        }

        #endregion

        #region FROM_TO_SIMPL_PLUS_MODULE

        public void Start()
        {
            if (tcpClient.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                Stop();
            }
            connectionRequested = true;
            Connect();
        }


        public void Stop()
        {
            connectionRequested = false;
            if (disconnectTimer != null)
            {
                disconnectTimer.Stop();
                disconnectTimer.Dispose();
                disconnectTimer = null;
            }
            if ((tcpClient != null) && (tcpClient.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED))
            {
                Send(MsgBuilder.BuildDisconnect());
                tcpClient.DisconnectFromServer();
            }
        }


        public void Log(ushort val)
        {
            #if USE_LOGGER
                bool printTheLog = val == 0 ? false : true;
                CrestronLogger.PrintTheLog(printTheLog);
                if (!printTheLog)
                    CrestronLogger.ShutdownLogger();
                else if (!CrestronLogger.LoggerInitialized)
                {
                    CrestronLogger.Initialize(10);
                    CrestronLogger.LogOnlyCurrentDebugLevel = false;
                }
            #else
                ErrorLog.Warn("Module not compiled with CrestronLogger support.");
            #endif
        }

        public void SetLogLevel(uint logLevel)
        {
            #if USE_LOGGER
                if (logLevel == 0)
                {
                    CrestronLogger.DebugLevel = 10;
                    CrestronLogger.LogOnlyCurrentDebugLevel = false;
                }
                else
                {
                    logLevel = (logLevel > 10) ? 10 : logLevel;
                    if (logLevel < 0)
                    {
                        SetLogLevel(0);
                    }
                    else
                    {
                        CrestronLogger.LogOnlyCurrentDebugLevel = true;
                        CrestronLogger.DebugLevel = logLevel;
                    }
                }
            #else
                ErrorLog.Warn("Module not compiled with CrestronLogger support.");
            #endif
        }

        public void OnMessageArrived(string topic, string value)
        {
            if (MessageArrived != null)
                MessageArrived(this, new MessageReceivedEventArgs(topic, value));
        }

        public void OnErrorOccured(string errorMessage)
        {
            if (ErrorOccured != null)
                ErrorOccured(this, new ErrorOccuredEventArgs(errorMessage));
        }

        #if USE_SSL
            private void OnSocketStatusChange(SecureTCPClient myTCPClient, SocketStatus serverSocketStatus)
        #else
            private void OnSocketStatusChange(TCPClient myTCPClient, SocketStatus serverSocketStatus)
        #endif
        {
            #if USE_LOGGER
                CrestronLogger.WriteToLog("MQTTCLIENT - OnSocketStatusChange - socket status : " + serverSocketStatus, 1);
            #endif
            if (serverSocketStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
            {
                OnConnectionStateChanged(1);
            }
            else
            {
                OnConnectionStateChanged(0);
                if (connectionRequested && (disconnectTimer == null))
                    disconnectTimer = new CTimer(DisconnectTimerCallback, 5000);
            }
        }

        public void Publish(string topic, string value, uint retain)
        {
            byte[] payload = Encoding.ASCII.GetBytes(value);
            MqttMsgPublish msg = MsgBuilder.BuildPublish(topic, false, (retain > 0), payload, GetNewPacketIdentifier());
            publisherManager.Publish(msg);
            FreePacketIdentifier(msg.MessageId); // this can be done automatically as long as we only publish at QoS 0
        }

        #endregion

        #region CONNECTION_TO_BROKER

        public void Connect()
        {
            #if USE_LOGGER
                CrestronLogger.WriteToLog("MQTTCLIENT - Connect , attempting connection to " + MqttSettings.Instance.IPAddressOfTheServer.ToString(), 1);
            #endif
            tcpClient.ConnectToServerAsync(ConnectToServerCallback);
        }


        #if USE_SSL
            private void ConnectToServerCallback(SecureTCPClient myTCPClient)
        #else
            private void ConnectToServerCallback(TCPClient myTCPClient)
        #endif
        {
            try
            {
                if (myTCPClient.ClientStatus == SocketStatus.SOCKET_STATUS_CONNECTED)
                {
                    MqttMsgConnect connect = MsgBuilder.BuildConnect(this.ClientID, MqttSettings.Instance.Username, MqttSettings.Instance.Password, this.WillRetain,
                         this.WillQosLevel, this.WillFlag, this.WillTopic, this.WillMessage, this.CleanSession, this.KeepAlivePeriod, ProtocolVersion);
                    Send(connect);
                    //TODO: timer for connack
                    tcpClient.ReceiveData();
                    MqttMsgBase packet = PacketDecoder.DecodeControlPacket(tcpClient.IncomingDataBuffer);
                    if (packet.Type == MqttMsgBase.MQTT_MSG_CONNACK_TYPE)
                    {
                        RouteControlPacketToMethodHandler(packet);
                    }
                    else
                    {
                        throw new MqttConnectionException("MQTTCLIENT - ConnectToServerCallback, Expected CONNACK , received " + packet, new ArgumentException());
                    }
                }
            }
            catch (MqttClientException e)
            {
                #if USE_LOGGER
                    CrestronLogger.WriteToLog("MQTTCLIENT - ConnectToServerCallback - Error occured : " + e.ErrorCode, 7);
                    CrestronLogger.WriteToLog("MQTTCLIENT - ConnectToServerCallback - Error occured : " + e.StackTrace, 7);
                #endif
            }
            catch (Exception e)
            {
                #if USE_LOGGER
                    CrestronLogger.WriteToLog("MQTTCLIENT - ConnectToServerCallback - Error occured : " + e.Message, 7);
                    CrestronLogger.WriteToLog("MQTTCLIENT - ConnectToServerCallback - Error occured : " + e.StackTrace, 7);
                #endif
                //Disconnect from server , signal error at module lvl;
            }
        }

        
        private void HandleCONNACKType(MqttMsgConnack mqttMsgConnack)
        {
            SubscribeToTopics();
            tcpClient.ReceiveDataAsync(ReceiveCallback);
        }

        
        private void OnConnectionStateChanged(ushort connectionStatus)
        {
            if (ConnectionStateChanged != null)
                ConnectionStateChanged(this, new ConnectionStateChangedEventArgs(connectionStatus));
        }

        #endregion

        #region SEND_CONTROL_PACKETS

        public void OnPacketToSend(object sender, PacketToSendEventArgs args)
        {
            Send(args.Packet);
        }

        public void Send(MqttMsgBase packet)
        {
            #if USE_LOGGER
                CrestronLogger.WriteToLog("MQTTCLIENT - SEND - Sending packet type " + packet, 2);
                CrestronLogger.WriteToLog("MQTTCLIENT - SEND - " + BitConverter.ToString(packet.GetBytes(ProtocolVersion)), 2);
            #endif
            byte[] pBufferToSend = packet.GetBytes(ProtocolVersion);
            tcpClient.SendDataAsync(pBufferToSend, pBufferToSend.Length, SendCallback);
        }

        #if USE_SSL
            private void SendCallback(SecureTCPClient myTCPClient, int numberOfBytesSent)
        #else
            private void SendCallback(TCPClient myTCPClient, int numberOfBytesSent)
        #endif
        {
            ;
        }

        #endregion

        #region RECEIVE_CONTROL_PACKETS       

        #if USE_SSL
            private void ReceiveCallback(SecureTCPClient myClient, int numberOfBytesReceived)
        #else
            private void ReceiveCallback(TCPClient myClient, int numberOfBytesReceived)
        #endif
        {
            try
            {
                if (numberOfBytesReceived != 0)
                {
                    byte[] incomingDataBuffer = new byte[numberOfBytesReceived];
                    Array.Copy(myClient.IncomingDataBuffer, 0, incomingDataBuffer, 0, numberOfBytesReceived);
                    tcpClient.ReceiveDataAsync(ReceiveCallback);
                    DecodeMultiplePacketsByteArray(incomingDataBuffer);
                }
            }
            catch (Exception e)
            {
                #if USE_LOGGER
                    CrestronLogger.WriteToLog("MQTTCLIENT - ReceiveCallback - Error occured : " + e.InnerException + " " + e.Message, 7);
                    CrestronLogger.WriteToLog("MQTTCLIENT - ReceiveCallback - Error occured : " + e.StackTrace, 7);
                #endif
                OnErrorOccured(e.Message);
                Disconnect(false);
            }

        }

        public void DecodeMultiplePacketsByteArray(byte[] data)
        {
            List<MqttMsgBase> packetsInTheByteArray = new List<MqttMsgBase>();
            int numberOfBytesProcessed = 0;
            int numberOfBytesToProcess = 0;
            int numberOfBytesReceived = data.Length;
            byte[] packetByteArray;
            MqttMsgBase tmpPacket = new MqttMsgSubscribe();
            while (numberOfBytesProcessed != numberOfBytesReceived)
            {
                int remainingLength = MqttMsgBase.decodeRemainingLength(data);
                int remainingLenghtIndex = tmpPacket.encodeRemainingLength(remainingLength, data, 1);
                numberOfBytesToProcess = remainingLength + remainingLenghtIndex;
                packetByteArray = new byte[numberOfBytesToProcess];
                Array.Copy(data, 0, packetByteArray, 0, numberOfBytesToProcess);
                {
                    byte[] tmp = new byte[data.Length - numberOfBytesToProcess];
                    Array.Copy(data, numberOfBytesToProcess, tmp, 0, tmp.Length);
                    data = tmp;
                }
                numberOfBytesProcessed += numberOfBytesToProcess;
                MqttMsgBase packet = PacketDecoder.DecodeControlPacket(packetByteArray);
                //RouteControlPacketDelegate r = new RouteControlPacketDelegate(RouteControlPacketToMethodHandler);
                //r.Invoke(packet);
                CrestronInvoke.BeginInvoke(RouteControlPacketToMethodHandler,packet);
            }            
        }

        
        private void RouteControlPacketToMethodHandler(object p)
        {
            MqttMsgBase packet = (MqttMsgBase)p;
            switch (packet.Type)
            {
                case MqttMsgBase.MQTT_MSG_CONNACK_TYPE:
                    {
                        HandleCONNACKType((MqttMsgConnack)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_PUBLISH_TYPE:
                    {
                        HandlePUBLISHType((MqttMsgPublish)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_PUBACK_TYPE:
                    {
                        HandlePUBACKType((MqttMsgPuback)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_PUBREC_TYPE:
                    {
                        HandlePUBRECType((MqttMsgPubrec)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_PUBREL_TYPE:
                    {
                        HandlePUBRELType((MqttMsgPubrel)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_PUBCOMP_TYPE:
                    {
                        HandlePUBCOMPType((MqttMsgPubcomp)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_SUBACK_TYPE:
                    {
                        HandleSUBACKype((MqttMsgSuback)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_UNSUBACK_TYPE:
                    {
                        HandleUNSUBACKype((MqttMsgUnsuback)packet);
                        break;
                    }
                case MqttMsgBase.MQTT_MSG_PINGRESP_TYPE:
                    {
                        HandlePINGRESPType((MqttMsgPingResp)packet);
                        break;
                    }
                default:
                    {
                        throw new MqttCommunicationException(new FormatException("MQTTCLIENT -Pacchetto non valido" + packet));
                    }
            }
        }


        #endregion

        #region PING

        private void HandlePINGREQType(MqttMsgPingReq mqttMsgPingReq)
        {
            Disconnect(false);
        }


        private void HandlePINGRESPType(MqttMsgPingResp mqttMsgPingResp)
        {
        }

        #endregion

        #region PUBLISH

        private void HandlePUBCOMPType(MqttMsgPubcomp pubComp)
        {
            throw new NotImplementedException();
            //publisherManager.ManagePubComp(pubComp);
        }

        private void HandlePUBRELType(MqttMsgPubrel pubRel)
        {
            throw new NotImplementedException();
            //MqttMsgPublish publish = sessionManager.GetPublishMessage(pubRel.MessageId);
            //string publishPayload = System.Text.Encoding.ASCII.GetString(publish.Message, 0, publish.Message.Length);
            //OnMessageArrived(publish.Topic, publishPayload);
        }

        private void HandlePUBRECType(MqttMsgPubrec pubRec)
        {
            throw new NotImplementedException();
            //publisherManager.ManagePubRec(pubRec);
        }

        private void HandlePUBACKType(MqttMsgPuback pubAck)
        {
            publisherManager.ManagePubAck(pubAck);
        }

        private void HandlePUBLISHType(MqttMsgPublish publish)
        {
            try
            {
                switch (publish.QosLevel)
                {
                    case MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE:
                        {
                            #if USE_LOGGER
                                CrestronLogger.WriteToLog("MQTTCLIENT - HandlePUBLISHType - Routing qos0 message", 5);
                            #endif
                            string publishPayload = System.Text.Encoding.ASCII.GetString(publish.Message, 0, publish.Message.Length);
                            OnMessageArrived(publish.Topic, publishPayload);
                            break;
                        }
                    case MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE:
                        {
                            #if USE_LOGGER
                                CrestronLogger.WriteToLog("MQTTCLIENT - HandlePUBLISHType - Routing qos1 message", 5);
                            #endif
                            string publishPayload = System.Text.Encoding.ASCII.GetString(publish.Message, 0, publish.Message.Length);
                            Send(MsgBuilder.BuildPubAck(publish.MessageId));
                            OnMessageArrived(publish.Topic, publishPayload);
                            break;
                        }
                    case MqttMsgBase.QOS_LEVEL_EXACTLY_ONCE:
                        {
                            #if USE_LOGGER
                                CrestronLogger.WriteToLog("MQTTCLIENT - HandlePUBLISHType - Routing qos2 message", 5);
                            #endif
                            //ManageQoS2(publish);
                            break;
                        }
                    default:
                        break;
                }
                //TODO: Raise MessageArrived event , handle the necessary responses with the publisher manager.
            }
            catch (ArgumentException e)
            {
                OnErrorOccured(e.Message);
            }

        }


        #endregion

        #region SUBSCRIBE

        private void HandleUNSUBACKype(MqttMsgUnsuback mqttMsgUnsuback)
        {
            throw new NotImplementedException();
        }

        private void HandleSUBACKype(MqttMsgSuback mqttMsgSuback)
        {
            #if USE_LOGGER
                CrestronLogger.WriteToLog("MQTTCLIENT - HANDLESUBACK -", 6);
            #endif
        }

        private void SubscribeToTopics()
        {
            Send(MsgBuilder.BuildSubscribe(Subscriptions.Keys.ToArray(), Subscriptions.Values.ToArray(), GetNewPacketIdentifier()));
        }


        #endregion

        #region DISCONNECT

        private void Disconnect(bool withDisconnectPacket)
        {
            #if USE_LOGGER
                CrestronLogger.WriteToLog("MQTTCLIENT - DISCONNECT - Restarting client", 8);
            #endif
            Stop();
        }

        public void DisconnectTimerCallback(object userSpecific)
        {
            if (connectionRequested && (tcpClient.ClientStatus != SocketStatus.SOCKET_STATUS_CONNECTED))
            {
                disconnectTimer.Dispose();
                disconnectTimer = new CTimer(DisconnectTimerCallback, 5000);
                Connect();
            }
        }
        #endregion


        internal ushort GetNewPacketIdentifier()
        {
            lock (packetIdentifiers)
            {
                ushort identifier = (ushort)rand.Next(0, 65535);
                while (packetIdentifiers.Contains(identifier))
                {
                    identifier = identifier = (ushort)rand.Next(0, 65535);
                }
                packetIdentifiers.Add(identifier);
                return identifier;
            }
        }

        internal void FreePacketIdentifier(ushort identifier)
        {
            if (packetIdentifiers.Contains(identifier))
                packetIdentifiers.Remove(identifier);
        }

    }
}