using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using System.Timers;
/*processManager*/
namespace SurpliseClient
{
    public class ProcessManager
    {
        private BlockingCollection<EventCapsule> _eventQueue;
        private const int CheckReceiveTimeInterval = 10000;
        private const int InitializeTimerinterval = 20000;

        private List<ClientProcess> _processList;
        private Communicator _clientCommunicator;

        private Socket _socket;
        private User _user;

        public bool Activated { get; private set; }
        public uint UserId
        {
            get => _user.UserId; 
        }
        public ProcessManager()
        {
            Activated = false;
        }
        public EventCapsule DequeueEventQueue()
        {
            return _eventQueue.Take();
        }
        public int CountEventQueue()
        {
            if (_eventQueue == null) return -1;
            return _eventQueue.Count;
        }
        public int CountProcessList()
        {
            if (_processList == null) return -1;
            lock (_processList)
            {
                return _processList.Count;
            }
        }
        public void ConnectServer(string ipAddress, int port, uint userId)
        {
            IPAddress address = IPAddress.Parse(ipAddress);
            IPEndPoint remoteEp = new IPEndPoint(address, port);
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _user = new User(userId);
            _processList = new List<ClientProcess>();
            lock (_processList)
                _processList = new List<ClientProcess>();
            _eventQueue = new BlockingCollection<EventCapsule>(new ConcurrentQueue<EventCapsule>());
            _socket.BeginConnect(remoteEp, ConnectCallback, _socket);
        }
        public byte RequestFile(string fileName, FileType fileType)
        {
            if (Activated == false || fileType == FileType.NoFile || _user.UserId==0) throw new ArgumentException("Wrong Filetype or API Not Activated"); //Returns if Id is not properly set
            byte processId = GetEmptyPid();
            if (processId == 0) throw new InvalidOperationException("Failed to get new process Id");
            ClientProcess requestFileProcess = new FileRequestProcess(_user, _eventQueue, _processList, processId);
            AddProcess(requestFileProcess);
            Thread fileRequest = new Thread(() => requestFileProcess.StartFileRequest(fileName, fileType));
            fileRequest.Start();
            return processId;
        }
        public byte SaveUserFile(string fileName, byte[] fileData)
        {
            FileType fileType = FileType.User;
            if (Activated == false || _user.UserId == 0) throw new ArgumentException("API not activated");
            byte processId = GetEmptyPid();
            if (processId == 0) throw new InvalidOperationException("Failed to get new process Id");
            ClientProcess saveFileProcess = new FileSaveProcess(_user, _eventQueue, _processList, processId);
            AddProcess(saveFileProcess);
            Thread fileRequest = new Thread(() => saveFileProcess.StartFileSend(fileName,fileType,fileData));
            fileRequest.Start();
            return processId;
        }


        /*
         * Internal methods
         */
        private byte RequestId()
        {
            byte processId = GetEmptyPid();
            if (processId == 0) return 0;
            ClientProcess requestIdProcess = new IdRequestProcess(_user, _eventQueue, _processList, processId);
            AddProcess(requestIdProcess);
            Thread idRequest = new Thread(() => requestIdProcess.StartIdRequest());
            idRequest.Start();
            return processId;
        }
        private void AddProcess(ClientProcess clientProcess)
        {
            lock (_processList)
            {
                _processList.Add(clientProcess);
                _processList.Sort((x, y) => x.Pid.CompareTo(y.Pid)); //sorts processList by Pid

            }
        }
        private byte GetEmptyPid()
        {
            byte processNum = 1;
            lock (_processList)
            {
                _processList.ForEach(delegate (ClientProcess process)
                {
                    if (process.Pid == processNum) processNum++;//Serches for processNum that is not used
                });
            }
            return processNum;
        }
        private ClientProcess GetProcess(byte processId)
        {
            ClientProcess process;
            lock (_processList)
            {
                process = _processList.Find(x => x.Pid == processId); //returns null if not found
            }
            return process;
        }
        private void Analyzer()
        {
            System.Timers.Timer checkTimer = new System.Timers.Timer(CheckReceiveTimeInterval);
            System.Timers.Timer initializeTimer = new System.Timers.Timer(InitializeTimerinterval);
            initializeTimer.Elapsed += CheckTimerCallback;
            initializeTimer.Start();
            checkTimer.Elapsed += CheckTimerCallback;
            Packet packet;
            while ((packet=GetPacket(checkTimer,initializeTimer))!=null)
            {
                byte processId = packet.ProcessId;
                ClientProcess process = GetProcess(processId);
                process?.EnqueueProcessQueue(packet); //ignores when according process is not found
            }
            checkTimer.Stop();
            lock (_clientCommunicator.CancelSendDequeue)
                _clientCommunicator.CancelSendDequeue.Cancel();
        }
        private Packet GetPacket(System.Timers.Timer checkTimer, System.Timers.Timer initializeTimer)
        {
            Packet packet = _user.DequeueReceive();
            if (packet?.RequestType == (byte) PacketType.Suspend)
            {
                lock (_processList)
                {
                    foreach (ClientProcess process in _processList)
                    {
                        process.EnqueueProcessQueue(packet);
                    }
                }

                _clientCommunicator.OperationState = false;
                SuspendCommunicator(_clientCommunicator);
                ShutDownCapsule capsule = new ShutDownCapsule((ErrorType) packet.Data[0], _user.UserId);
                _eventQueue.Add(capsule);
                packet = null;
                Activated = false;
            }

            if (packet?.RequestType == (byte) PacketType.ConnectionCheck)
            {
                _user.EnqueueSend(packet);
            }

            initializeTimer.Stop();
            checkTimer.Stop();
            checkTimer.Start();

            return packet;
        }
        private void CheckTimerCallback(object e, ElapsedEventArgs args)
        {
            Packet timeoutSuspendPacket = new Packet
            {
                Data = new byte[1],
                DataSize = 1,
                FileType = (byte)FileType.NoFile,
                MorePackets = false,
                PacketNumber = 0,
                ProcessId = 0,
                RequestType = (byte)PacketType.Suspend
            };
            MakePacketHeader(timeoutSuspendPacket);
            timeoutSuspendPacket.Data[0] = (byte)ErrorType.ConnectionCheckFailed;
            _user.EnqueueReceive(timeoutSuspendPacket);
            Activated = false;
            _clientCommunicator.OperationState = false;
            Console.WriteLine("Checktimer callback called..");
        }
        private void ConnectCallback(IAsyncResult ar)
        {
            SocketAsyncEventArgs sendArg = new SocketAsyncEventArgs();
            sendArg.SetBuffer(new byte[PacketConsts.ArgsBufferSize], 0, PacketConsts.SendSize);
            SocketAsyncEventArgs receiveArg = new SocketAsyncEventArgs();
            receiveArg.SetBuffer(new byte[PacketConsts.ArgsBufferSize], 0, PacketConsts.SendSize);
            Thread analyzerThread = new Thread(Analyzer);
            _user.SetUserSocket(_socket);
            _clientCommunicator = new Communicator(sendArg, receiveArg);
            _clientCommunicator.SetUser(_user);
            _clientCommunicator.CancelSendDequeue = new CancellationTokenSource();
            _clientCommunicator.StartSendThread();
            _clientCommunicator.StartReceiveThread();
            analyzerThread.Start();
            if (_user.UserId == 0)
            {
                RequestId(); //Call RequestId Automatically if userID was set to default
            }

            Activated = true;
            _eventQueue.Add(new ConnectedEvent(_user.UserId));
        }
        private static void MakePacketHeader(Packet packet)
        {
            packet.Header[HeaderMemberStartIndex.HeaderSign] = PacketConsts.HeaderSign;
            packet.Header[HeaderMemberStartIndex.PacketNum] = (byte)(packet.PacketNumber & 0xFF);
            packet.Header[HeaderMemberStartIndex.PacketNum + 1] = (byte)(packet.PacketNumber >> 8);
            packet.Header[HeaderMemberStartIndex.RequestType] = packet.RequestType;
            packet.Header[HeaderMemberStartIndex.DataSize] = (byte)(packet.DataSize & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 1] = (byte)((packet.DataSize >> 8) & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 2] = (byte)((packet.DataSize >> 16) & 0xFF);
            packet.Header[HeaderMemberStartIndex.DataSize + 3] = (byte)((packet.DataSize >> 24) & 0xFF);
            packet.Header[HeaderMemberStartIndex.FileType] = packet.FileType;
            packet.Header[HeaderMemberStartIndex.ProcessNum] = packet.ProcessId;
            packet.Header[HeaderMemberStartIndex.EndPoint] =
                (packet.MorePackets ? PacketConsts.MoreBytes : PacketConsts.LastByte);
        }

        public void Shutdown()
        {
            Packet shutdownPacket = new Packet
            {
                Data = new byte[1],
                DataSize = 1,
                FileType = (byte)FileType.NoFile,
                MorePackets = false,
                PacketNumber = 0,
                ProcessId = 0,
                RequestType = (byte)PacketType.Suspend
            };
            shutdownPacket.Data[0] = (byte)ErrorType.ManualShutdown;
            MakePacketHeader(shutdownPacket);
            Activated = false;
            _user.EnqueueReceive(shutdownPacket);
            _clientCommunicator.OperationState = false;
        }

        private static void SuspendCommunicator(Communicator communicator)
        {

            lock (communicator.CancelSendDequeue)
            {
                communicator.CancelSendDequeue.Cancel(); // cancel the dequeue process from communicator
            }
            try
            {
                communicator.UserToken.ClientSocket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
            }

            while (communicator.UserToken.ClientSocket.Connected)
            {
                communicator.UserToken.ClientSocket.Disconnect(false);
            }

            communicator.UserToken.ClientSocket.Close();

        }
    }


}
