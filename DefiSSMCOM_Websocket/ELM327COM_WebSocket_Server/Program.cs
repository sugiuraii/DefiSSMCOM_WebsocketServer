﻿using System.Threading;
using DefiSSMCOM.WebSocket;
using log4net;
using System.Xml;
using System.IO;

namespace DefiSSMCOM.Application.ELM327COM
{
    public class MainClass
    {
        public static void Main(string[] args)
        {
            Application appli = new Application();
            appli.start();
        }
    }

    public class Application : ApplicationCommon
    {
        private readonly DefiSSMCOM.WebSocket.ELM327COM_Websocket elm327comserver1 = new ELM327COM_Websocket();
        private static readonly ILog logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public void start()
        {
            webSocketServerStart("elm327server_settings.xml", elm327comserver1);
        }
    }
}
