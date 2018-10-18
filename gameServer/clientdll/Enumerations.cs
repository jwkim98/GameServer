using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SurpliseClient
{
    public enum CommunicatorError
    {
        ObjectDisposed,
        ReceiveCheckTimeout,
        SendCheckError,
        NoBytesReceived,
        SocketError,
        WrongHeader,
        DataSizeOverflow,
        EmptyData,
        InvalidRequest,
        WrongCloser,
        NoAdditionalReceived,
        ReceiverSideError,
        HeaderError
    }

    public enum PacketType : byte
    {
        FileInfo,
        IdRequest,
        Id,
        HealthRecord,
        Story,
        Manager,
        Update,
        NewUser,
        File,
        FileRequest,
        Error,
        Suspend,
        ConnectionCheck,
        FileSaveDone
    }

    public enum FileType : byte
    {
        Dlc,
        User,
        NoFile
    }

    internal static class PacketConsts//Header design definitions
    {
        public const byte HeaderSize = 11;
        public const byte HeaderSign = 0x01;
        public const int ReceiverBufferSize = ArgsBufferSize * 4;//should be largest packet size for 1 time
        public const int SenderBufferSize = ArgsBufferSize * 4;
        public const byte MoreBytes = 0xF0;
        public const byte LastByte = 0x0F;
        public const int ArgsBufferSize = 1024 * 128;
        public const int SendSize = 1024 * 128;
        public const int RequestTypeNum = 6;// Number of possible requests-1
    }

    internal static class HeaderMemberStartIndex
    {
        public const byte HeaderSign = 0;
        public const byte PacketNum = 1;
        public const byte RequestType = 3;
        public const byte DataSize = 4;
        public const byte FileType = 8;
        public const byte ProcessNum = 9;
        public const byte EndPoint = 10;
    }

    public class Packet
    {
        internal byte[] Header = new byte[PacketConsts.HeaderSize];
        internal byte[] Data;
        internal byte RequestType;
        internal ushort PacketNumber;
        internal bool MorePackets;
        internal uint DataSize;
        internal byte FileType;
        internal byte ProcessId;
    }

    public enum NetworkEvent
    {
        FileRequestDone,
        FileRequestFailed,
        IdRequestDone,
        IdRequestFailed,
        FileSaveDone,
        FileSaveFailed,
        FileProcessDone,
        FileProcessFailed,
        IdProcessDone,
        IdProcessFailed,
        Connected,
        Suspended
    }

    public enum ErrorType
    {
        Success,
        SocketClosed,
        FileNotFound,
        ArgumentError,
        EmptyFile, //file Length was 0
        InvalidPacket, // Wrong packet has arrived
        NoMoreId, //No more ID to be assigned
        ConnectionCheckFailed, // Connection was terminated due to network error
        ManualShutdown, // Shutdown was called by main thread
        Unknown
    }
}


