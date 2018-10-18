using System.Net;
using System.Threading;
using SurpliseClient;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace TestClient
{
    class Program
    {
        private static String _ipAddress = "192.168.1.53";
        private static int _portNum = 2018;
        private static bool testActivated = true;

        static void Main(string[] args)
        {
            Console.WriteLine("Spawning New Thread");
            try
            {
                Thread thread = new Thread(new ThreadStart(Start));
                thread.Start();
                string str = Console.ReadLine();
                if (str == "q")
                    testActivated = false;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        private static void Start()
        {
            var processManager = new ProcessManager();
            processManager.ConnectServer(_ipAddress, _portNum, 0);
            EventCapsule capsule;
            byte fileSavepid;
            string logFileName = @"Log/testLog.txt ";// + DateTime.Now.ToString(new CultureInfo("en-US")) + ".txt";
            FileStream file = File.Open(logFileName, FileMode.OpenOrCreate);
            
            for (int i = 0; i < 2; i++)
            {
                capsule = processManager.DequeueEventQueue();
                string timelog =" || "+ DateTime.Now.ToString(new CultureInfo("en-US"));
                WriteToFile(
                    "NetworkEvent: " + capsule.NetworkEvent + "ErrorType: " + capsule.ErrorType + "ProcessId" +
                    capsule.ProcessId + "\n", file);
                WriteToFile(timelog, file); // Timelog
                Console.WriteLine(capsule.NetworkEvent + timelog);

                if (capsule.NetworkEvent == NetworkEvent.Connected)
                {
                    Console.WriteLine("Connected");
                }
                if (capsule.NetworkEvent == NetworkEvent.IdRequestDone)
                {
                    Console.WriteLine("Id:" + capsule.UserId);
                    processManager.RequestFile(@"scg/target1.jpg", FileType.Dlc);
                    processManager.RequestFile(@"scg/target2.png", FileType.Dlc);
                    processManager.RequestFile(@"scg/target3.png", FileType.Dlc);
                    processManager.RequestFile(@"scg/target1.jpg", FileType.Dlc);
                    processManager.RequestFile(@"scg/target2.png", FileType.Dlc);
                    processManager.RequestFile(@"scg/target3.png", FileType.Dlc);
                    processManager.RequestFile(@"scg/target1.jpg", FileType.Dlc);
                    processManager.RequestFile(@"scg/target2.png", FileType.Dlc);
                    processManager.RequestFile(@"scg/target3.png", FileType.Dlc);
                    byte[] fileData = File.ReadAllBytes(@"umaru_source.jpg");
                    fileSavepid = processManager.SaveUserFile("Umaru.jpg", fileData);
                    break;
                }
                else
                {
                    Console.WriteLine(capsule.NetworkEvent);
                    Console.WriteLine(capsule.ErrorType);
                }
            }

            while (testActivated)
            {
                while (processManager.CountProcessList() > 0 || processManager.CountEventQueue() > 0)
                {
                    capsule = processManager.DequeueEventQueue();

                    string timelog = DateTime.Now.ToString(new CultureInfo("en-US"));
                    WriteToFile(
                        "NetworkEvent: " + capsule.NetworkEvent + "ErrorType: " + capsule.ErrorType + "ProcessId" +
                        capsule.ProcessId + "\n", file);
                    WriteToFile(timelog, file); // Timelog
                    Console.WriteLine(capsule.NetworkEvent + timelog);

                    if (capsule.NetworkEvent == NetworkEvent.FileSaveDone)
                        processManager.RequestFile("Umaru.jpg", FileType.User);
                    if (capsule.NetworkEvent == NetworkEvent.FileRequestDone)
                    {
                        File.WriteAllBytes(((FileCapsule) capsule).FileName,
                            ((FileCapsule) capsule).Data);
                    }
                }

                processManager.RequestFile(@"scg/target1.jpg", FileType.Dlc);
                processManager.RequestFile(@"scg/target2.png", FileType.Dlc);
                processManager.RequestFile(@"scg/target3.png", FileType.Dlc);
                processManager.RequestFile(@"scg/target1.jpg", FileType.Dlc);
                processManager.RequestFile(@"scg/target2.png", FileType.Dlc);
                processManager.RequestFile(@"scg/target3.png", FileType.Dlc);
                processManager.RequestFile(@"scg/target1.jpg", FileType.Dlc);
                processManager.RequestFile(@"scg/target2.png", FileType.Dlc);
                processManager.RequestFile(@"scg/target3.png", FileType.Dlc);
                byte[] fileData = File.ReadAllBytes(@"umaru_source.jpg");
                fileSavepid = processManager.SaveUserFile("Umaru.jpg", fileData);
            }

            WriteToFile("Shutting down", file);
            file.Close();

            processManager.SaveUserFile("log.txt", File.ReadAllBytes(logFileName)); //send the log file to server
            processManager.DequeueEventQueue();//Dequeue saveDone capsule

            processManager.Shutdown();
             Console.WriteLine("shutting down");
        }

        private static void WriteToFile(string str, FileStream file)
        {
            byte[] bytesToWrite = new UTF8Encoding(true).GetBytes(str+" | ");
            file.Write(bytesToWrite, 0, bytesToWrite.Length);
        }
    }
}
