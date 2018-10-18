using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;

namespace SurpliseClient
{
    internal abstract class ClientProcess
    {
        internal virtual void StartFileRequest(string fileName, FileType fileType)
        {
        }

        internal virtual void StartIdRequest()
        {
        }

        internal virtual void StartFileSend(string fileName, FileType fileType, byte[] fileData)
        {

        }

        protected User UserToken;

        private readonly BlockingCollection<Packet> _processQueue =
            new BlockingCollection<Packet>(new ConcurrentQueue<Packet>());

        protected BlockingCollection<EventCapsule> EventQueue;

        protected List<ClientProcess> ProcessList;

        public byte Pid { get; protected set; }

        /*
         * Following methods controlls the Process Queue.
         * This methods are Thread-safe, and can be processed cocurrently
         */
        internal void EnqueueProcessQueue(Packet packet)
        {
            _processQueue.Add(packet);
        } //Cocurrently enqueues

        internal Packet DequeueProcessQueue()
        {
            return _processQueue.Take();
        } //Cocurrently dequeues, waits if no element is inside it

        internal Packet PeekProcessQueue()
        {
            return _processQueue.First();
        }

        internal int CountProcessQueue()
        {
            return _processQueue.Count;
        }

        internal bool AnyProcessQueue()
        {
            return _processQueue.Any();
        }

        /*
         * This method should be called before the termination of the process
         * This method removes the process from the process list
         */
        protected void TerminateProcess(ClientProcess process)
        {
            lock (ProcessList)
            {
                ProcessList.Remove(process);
            }
        }

        /*
 * This method will make packet header with specified packet Fieldss
 * Fields Except Data[] and Header[] must be set before calling this method
 */
        protected void MakePacketHeader(Packet packet)
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
                packet.MorePackets ? PacketConsts.MoreBytes : PacketConsts.LastByte;
        }
    }
    /*
     * FileRequestProcess Class
     * This class manages file Request process from client. This class Extends ClientProcess
     * This class will automatically remove itself from process list after completion
     * This class will Enqueue fileCapsule to the eventcapsule queue with NetworkEvent, ErrorType Set
     * -Receivable Packet types-
     * FileInfo, File, Suspend, Error
     * -Errors possible-
     * SocketClosed, InvalidPacket
     */
    internal class Analyzer: ClientProcess
    {
        internal Analyzer()
        {
            Pid = 0;
        }
    }
    internal class FileRequestProcess : ClientProcess
    {
        internal int
            ReceivedPacketCount
        {
            get;
            private set;
        } //Checks how many packets has been received. Resetted when process begins

        internal FileRequestProcess(User user, BlockingCollection<EventCapsule> eventQueue,
            List<ClientProcess> processList, byte processNum)
        {
            UserToken = user;
            EventQueue = eventQueue;
            Pid = processNum;
            ProcessList = processList;
        }

        /*
         * StartFileRequest
         * This method Creates FileRequest packet, and Enqueues it in Send Queue
         * fileName shouldn't be longer than 256 bytes ( throws ArgumentException if  filename is too long, or '0')
         * This method will call ReadFilePackets after execution
         *  Data0: userID LSB
         *  Data1: userID MSB
         *  Data2: userID
         *  Data3: userID
         *  Data4: fileNameArray Length
         *  Data5~Data(3+fileNameArray Length) : fileNameArray
         */
        internal override void StartFileRequest(string fileName, FileType fileType)
        {
            byte[] fileNameArray = Encoding.ASCII.GetBytes(fileName);
            uint userId = UserToken.UserId;
            if (fileNameArray.Length > 256 || fileNameArray.Length == 0)
            {
                throw new ArgumentException("Filename is too long");
            }

            Packet fileRequestpacket = new Packet
            {
                RequestType = (byte)PacketType.FileRequest,
                FileType = (byte)fileType,
                MorePackets = false,
                PacketNumber = 0,
                DataSize = (byte)(fileNameArray.Length + 5), // Additional 3 for userID and fileName length
                ProcessId = Pid
            };
            fileRequestpacket.Data = new byte[fileRequestpacket.DataSize];
            MakePacketHeader(fileRequestpacket); //Sets packet Header
            fileRequestpacket.Data[0] = (byte)(userId & 0xFF); // first and second bytes are for user ID
            fileRequestpacket.Data[1] = (byte)((userId >> 8) & 0xFF);
            fileRequestpacket.Data[2] = (byte) ((userId >> 16) & 0xFF);
            fileRequestpacket.Data[3] = (byte) ((userId >> 24) & 0xFF);
            fileRequestpacket.Data[4] = (byte)fileNameArray.Length; // File length
            Array.Copy(fileNameArray, 0, fileRequestpacket.Data, 5,
                fileNameArray.Length); // Copy file name to packet Data (from index 5)
            UserToken.EnqueueSend(fileRequestpacket); // Enqueue the Request packet to Sender
            ReadFilePackets(); // Start reading packets
        }

        /*
         * ReadFilePackets
         * This method will Read packet until MorePackets ==false, and Create fileCapsule storing file Data
         * This method assumes Analyzer works Correct. will be terminated if invalid packet arrives
         * -Receivable Packets-
         * FileInfo, File, Suspend, Error
         * FileInfo packet must arrive before File packet arrives
         */
        protected void ReadFilePackets()
        {
            FileCapsule fileCapsule = new FileCapsule(UserToken.UserId, Pid);
            List<byte> fileDataList = new List<byte>();
            Packet filePacket = DequeueProcessQueue();
            if (!IsValidPacket(filePacket))
            {
                fileCapsule.NetworkEvent = NetworkEvent.FileRequestFailed;
                fileCapsule.ErrorType = ErrorType.InvalidPacket;
                EventQueue.Add(fileCapsule);
                TerminateProcess(this);
                return;
            }

            if (filePacket.RequestType == (byte)PacketType.Suspend)
            {
                fileCapsule.NetworkEvent = NetworkEvent.FileRequestFailed;
                fileCapsule.ErrorType = ErrorType.SocketClosed;
                EventQueue.Add(fileCapsule);
                TerminateProcess(this);
                return;
            } //Handles suspend signal

            if (filePacket.RequestType == (byte)PacketType.Error)
            {
                fileCapsule.NetworkEvent = NetworkEvent.FileRequestFailed;
                fileCapsule.ErrorType = (ErrorType)filePacket.Data[1];
                EventQueue.Add(fileCapsule);
                TerminateProcess(this);
                return;
            } //Handles error signal

            if (filePacket.RequestType == (byte)PacketType.FileInfo) //First Packet must be FileInfo
            {
                fileCapsule.FileSize = BitConverter.ToUInt32(filePacket.Data, 0);// brings file size
                fileCapsule.UserId = BitConverter.ToUInt32(filePacket.Data, 4);
                fileCapsule.FileName = Encoding.ASCII.GetString(filePacket.Data, 9, filePacket.Data[8]);
                fileCapsule.FileType = (FileType)filePacket.FileType;
                ReceivedPacketCount = 0;
            }
            while (filePacket.MorePackets)
            {

                if (filePacket.RequestType == (byte)PacketType.File)
                {
                    fileDataList.AddRange(filePacket.Data.ToList());
                    ReceivedPacketCount++;
                }

                filePacket = DequeueProcessQueue();
                if (!IsValidPacket(filePacket))
                {
                    fileCapsule.NetworkEvent = NetworkEvent.FileRequestFailed;
                    fileCapsule.ErrorType = ErrorType.InvalidPacket;
                    EventQueue.Add(fileCapsule);
                    TerminateProcess(this);
                    return;
                }

                if (filePacket.RequestType == (byte)PacketType.Suspend)
                {
                    fileCapsule.NetworkEvent = NetworkEvent.FileRequestFailed;
                    fileCapsule.ErrorType = ErrorType.SocketClosed;
                    EventQueue.Add(fileCapsule);
                    TerminateProcess(this);
                    return;
                } //Handles suspend signal

                if (filePacket.RequestType == (byte)PacketType.Error)
                {
                    fileCapsule.NetworkEvent = NetworkEvent.FileRequestFailed;
                    fileCapsule.ErrorType = (ErrorType)filePacket.Data[1];
                    EventQueue.Add(fileCapsule);
                    TerminateProcess(this);
                    return;
                } //Handles error signal
            }

            if (filePacket.MorePackets == false && filePacket.RequestType == (byte)PacketType.File)
            {
                fileDataList.AddRange(filePacket.Data.ToList());
                ReceivedPacketCount++;
                fileCapsule.Data = fileDataList.ToArray();
                fileCapsule.NetworkEvent = NetworkEvent.FileRequestDone;
                fileCapsule.ErrorType = ErrorType.Success;
                EventQueue.Add(fileCapsule);
                TerminateProcess(this); // remove itself from processList
            }
        }
        /*
         * Checks if Valid packet has been arrived
         * Returns true if packet was valid
         */
        private bool IsValidPacket(Packet packet)
        {
            return packet.RequestType == (byte)PacketType.FileInfo || packet.RequestType == (byte)PacketType.File
                                                                    || packet.RequestType == (byte)PacketType.Error ||
                                                                    packet.RequestType == (byte)PacketType.Suspend;
        }
    }

    internal class FileSaveProcess : ClientProcess
    {
        internal FileSaveProcess(User user, BlockingCollection<EventCapsule> eventQueue,
            List<ClientProcess> processList, byte processNum)
        {
            UserToken = user;
            EventQueue = eventQueue;
            Pid = processNum;
            ProcessList = processList;
        }

        internal override void StartFileSend(string fileName, FileType fileType, byte[] fileData)
        {
            int fileSize = fileData.Length;
            int sourceOffset = 0;
            Packet fileInfoPacket = new Packet
            {
                PacketNumber = 0,
                RequestType = (byte) PacketType.FileInfo,
                FileType = (byte) fileType,
                MorePackets = true,
                ProcessId = Pid
            };
            fileInfoPacket.DataSize =
                (uint) MakeFileDataInfo(fileInfoPacket, (uint) fileSize, fileName, UserToken.UserId);
            MakePacketHeader(fileInfoPacket);
            UserToken.EnqueueSend(fileInfoPacket);
            ushort packetNum = 1;
            while (sourceOffset < fileSize)
            {
                int copySize = PacketConsts.ReceiverBufferSize;
                Packet packet = new Packet
                {
                    PacketNumber = packetNum,
                    RequestType = (byte) PacketType.File,
                    FileType = (byte) fileType,
                    MorePackets = true,
                    ProcessId = Pid
                };
                packetNum += 1;
                if (copySize > fileSize - sourceOffset)
                {
                    copySize = fileSize - sourceOffset;
                    packet.MorePackets = false;
                }

                packet.DataSize = (uint) copySize;
                MakePacketHeader(packet);
                packet.Data = new byte[copySize];
                Array.Copy(fileData, sourceOffset, packet.Data, 0, copySize);
                UserToken.EnqueueSend(packet);
                sourceOffset += copySize;
            }

            Packet replyPacket = DequeueProcessQueue();
            if (replyPacket.RequestType == (byte) PacketType.Suspend)
            {
                EventCapsule terminateCapsule = new FileSaveCapsule(UserToken.UserId, Pid, NetworkEvent.FileSaveFailed,
                    ErrorType.SocketClosed);
                EventQueue.Add(terminateCapsule);
            }

            if (replyPacket.RequestType == (byte)PacketType.FileSaveDone)
            {
                EventCapsule capsule =
                    new FileSaveCapsule(UserToken.UserId, Pid, NetworkEvent.FileSaveDone, ErrorType.Success);
                EventQueue.Add(capsule);
            }

            if (replyPacket.RequestType == (byte) PacketType.Error)
            {
                EventCapsule capsule = new FileSaveCapsule(UserToken.UserId, Pid, NetworkEvent.FileSaveFailed,
                    (ErrorType) replyPacket.Data[0]);
                EventQueue.Add(capsule);
            }

            TerminateProcess(this);
        }

        private int MakeFileDataInfo(Packet fileInfoPacket, uint fileSize, string fileName, uint userId)
        {
            byte[] fileNameArray = Encoding.ASCII.GetBytes(fileName);
            fileInfoPacket.Data = new byte[fileNameArray.Length + 9];
            fileInfoPacket.Data[0] = (byte)(fileSize & 0xFF);
            fileInfoPacket.Data[1] = (byte)((fileSize >> 8) & 0xFF);
            fileInfoPacket.Data[2] = (byte)((fileSize >> 16) & 0xFF);
            fileInfoPacket.Data[3] = (byte)((fileSize >> 24) & 0xFF);
            fileInfoPacket.Data[4] = (byte)(userId & 0xFF);
            fileInfoPacket.Data[5] = (byte)((userId >> 8) & 0xFF);
            fileInfoPacket.Data[6] = (byte)((userId >> 16) & 0xFF);
            fileInfoPacket.Data[7] = (byte)((userId >> 24) & 0xFF);
            fileInfoPacket.Data[8] = (byte)fileNameArray.Length;
            for (int i = 0; i < fileNameArray.Length; i++)
            {
                fileInfoPacket.Data[i + 9] = fileNameArray[i];
            }
            return fileNameArray.Length + 9; //Returns DataSize
        }
    }

    /*
     * IdRequestProcess
     * This Process will request new ID from the server
     * 
     */
    internal class IdRequestProcess : ClientProcess
    {

        internal IdRequestProcess(User user, BlockingCollection<EventCapsule> eventQueue,
            List<ClientProcess> processList, byte processId)
        {
            Pid = processId;
            EventQueue = eventQueue;
            ProcessList = processList;
            UserToken = user;
        }

        internal override void StartIdRequest()
        {
            Packet requestIdPacket = new Packet()
            {
                Data = new byte[1],
                DataSize = 1,
                FileType = (byte) FileType.NoFile,
                MorePackets = false,
                PacketNumber = 0,
                ProcessId = Pid,
                RequestType = (byte) PacketType.IdRequest
            };
            MakePacketHeader(requestIdPacket);
            requestIdPacket.Data[0] = (byte)PacketType.IdRequest;
            UserToken.EnqueueSend(requestIdPacket);
            ReceiveId();
        }

        private void ReceiveId()
        {
            Packet packet = DequeueProcessQueue();
            EventCapsule idCapsule;
            if (!IsValidPacket(packet))
            {
                idCapsule = new IdCapsule(UserToken.UserId, Pid);
                idCapsule.NetworkEvent = NetworkEvent.IdRequestFailed;
                idCapsule.ErrorType = ErrorType.InvalidPacket;
                EventQueue.Add(idCapsule);
                TerminateProcess(this);
                return;
            }

            if (packet.RequestType == (byte)PacketType.Id)
            {
                UserToken.UserId = BitConverter.ToUInt32(packet.Data, 0);//Sets the ID received
                idCapsule = new IdCapsule(UserToken.UserId, Pid)
                {
                    NetworkEvent = NetworkEvent.IdRequestDone,
                    ErrorType = ErrorType.Success
                };
                EventQueue.Add(idCapsule);
                TerminateProcess(this);
            }

            if (packet.RequestType == (byte)PacketType.Error)
            {
                idCapsule = new IdCapsule(UserToken.UserId, Pid)
                {
                    NetworkEvent = NetworkEvent.IdRequestFailed,
                    ErrorType = (ErrorType)packet.Data[1]
                };
                EventQueue.Add(idCapsule);
                TerminateProcess(this);
            }

            if (packet.RequestType == (byte)PacketType.Suspend)
            {
                idCapsule = new IdCapsule(UserToken.UserId, Pid)
                {
                    NetworkEvent = NetworkEvent.IdRequestFailed,
                    ErrorType = ErrorType.SocketClosed
                };
                EventQueue.Add(idCapsule);
                TerminateProcess(this);
            }
        }

        private bool IsValidPacket(Packet packet)
        {
            return packet.RequestType == (byte)PacketType.Id ||
                   packet.RequestType == (byte)PacketType.Suspend ||
                   packet.RequestType == (byte)PacketType.Error;
        }
    }

}
