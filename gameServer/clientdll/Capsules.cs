using System;


namespace SurpliseClient
{
    public abstract class EventCapsule
    {
        public NetworkEvent NetworkEvent { get; internal set; }

        public ErrorType ErrorType { get; internal set; }


        public uint UserId { get; internal set; }


        public byte ProcessId { get; internal set; }
    }
    /*
     * Every member accesible from unity
     */
    public class FileCapsule : EventCapsule
    {
        public FileCapsule(uint userId, byte processId)
        {
            UserId = userId;
            ProcessId = processId;
        }
        public byte[] Data;
        public uint FileSize { get; internal set; }
        public String FileName { get; internal set; }
        public FileType FileType { get; internal set; }
    }
    /*
     * Only networkEvent and errorType is accesible from unity
     */
    public class ConnectedEvent : EventCapsule
    {
        internal ConnectedEvent(uint userId)
        {
            NetworkEvent = NetworkEvent.Connected;
            ErrorType = ErrorType.Success;
            UserId = userId;
            ProcessId = 0;
        }
    }
    /*
     * UserId accesible from unity
     */
    public class IdCapsule : EventCapsule
    {
        internal IdCapsule(uint userId, byte processId)
        {
            UserId = userId;
            ProcessId = processId;
        }
    }

    public class ServerEventCapsule : EventCapsule
    {
        internal ServerEventCapsule(NetworkEvent networkEvent, ErrorType errorType, uint userId, byte processId)
        {
            NetworkEvent = networkEvent;
            ErrorType = errorType;
            UserId = userId;
            ProcessId = processId;
        }
    }

    public class ShutDownCapsule : EventCapsule
    {
        public ShutDownCapsule(ErrorType errorType, uint userId)
        {
            ProcessId = 0;
            NetworkEvent = NetworkEvent.Suspended;
            ErrorType = errorType;
            UserId = userId;
        }
    }

    public class FileSaveCapsule:EventCapsule
    {
        internal FileSaveCapsule(uint userId, byte processId, NetworkEvent networkEvent, ErrorType errorType)
        {
            UserId = userId;
            ProcessId = processId;
            NetworkEvent = networkEvent;
            ErrorType = errorType;
        }
    }
}
