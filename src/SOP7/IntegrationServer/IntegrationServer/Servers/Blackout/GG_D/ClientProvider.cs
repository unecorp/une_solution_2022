using dnsTcpLib2;
using IntegrationServer.Datas;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace IntegrationServer.Servers.Blackout.GG_D
{
    class ClientProvider : ClientServiceProvider
    {
        private static byte FC_ReadRegister = 0x04;

        private int m_nServerSeqNo = -1;

        private UInt16 m_nTransID = 0;

        private bool m_runThread = false;
        private bool m_bIsConnect = false;

        Thread m_ConnectionThread = null;
        Thread m_RequestThread = null;

        BlackoutGGDManager m_parentManager = null;

        public static int RegisterLength = 2;
        public static int FloatLeng = 4;
        public static UInt16 RequestLength = 6;

        private static bool IsReverse = true;   // 빅엔디안, 리틀엔디안 여부에 따라 

        public ClientProvider(BlackoutGGDManager mgr, int nServerSeqNo)
        {
            m_parentManager = mgr;
            m_nServerSeqNo = nServerSeqNo;

            this.Client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);
        }

        public override void OnDropConnection()
        {
            //throw new NotImplementedException();
        }

        public override void OnReceiveData()
        {
            string strErrorMessage = "";

            try
            {
                byte[] data = this.ReceivedData;

                if (data == null || data.Length < 10)
                    return;

                m_parentManager.WriteBinaryLog(data, 0, data.Length, "[Received]");

                //data[0] = 0x00;           // Transaction ID
                //data[1] = 0x00;           // Transaction ID
                //data[2] = 0x00;           // TCP/IP 고정
                //data[3] = 0x00;           // TCP/IP 고정
                //data[4] = 0x00;           // 길이
                //data[5] = 0x04;           // 길이 >> SlaveID 부터 [SlaveID, FC, Byte Count, Data] 길이
                //data[6] = 0x01;           // Slave ID
                byte FC = data[7];          // Function Code
                int nDataLeng = data[8];    // Byte Count >> Data를 구성하는 Byte Count

                byte[] arrData = new byte[nDataLeng];

                Array.Copy(data, 9, arrData, 0, nDataLeng);

                if (FC == FC_ReadRegister && nDataLeng == RequestLength * RegisterLength)
                {   // 신호 체크
                    m_parentManager.CheckAlarm(arrData, IsReverse);
                }
            }
            catch (Exception e)
            {
                m_parentManager.WriteLog("ContactProvider OnReceiveData() Exception : " + e.Message, LogTypes.Error);
            }
        }

        public void Start()
        {
            if (m_runThread)
                return;

            m_runThread = true;

            m_ConnectionThread = new Thread(new ThreadStart(ConnectionThread));
            m_ConnectionThread.Start();

            m_RequestThread = new Thread(new ThreadStart(RequestThread));
            m_RequestThread.Start();
        }

        public void Stop()
        {
            m_runThread = false;
            this.Close();
        }

        private void ConnectionThread()
        {
            byte[] pingBytes = new byte[] { 0x00 };

            while (m_runThread)
            {
                try
                {
                    if (!this.IsConnected)
                    {
                        lock (this)
                        {
                            if (m_parentManager != null && m_parentManager.Port > 0
                                && m_parentManager.ServerIP != null && m_parentManager.ServerIP != "")
                            {
                                bool bResult = this.Connect(m_parentManager.ServerIP, m_parentManager.Port);

                                if (m_bIsConnect == false && bResult == true)
                                {   // 연결 성공
                                    m_bIsConnect = true;
                                    m_parentManager.WriteLog("ConnectionThread() : " + m_parentManager.ServerIP + ":" + m_parentManager.Port.ToString() + " / " + this.IsConnected, LogTypes.Info);

                                    if (!m_runThread)
                                        return;

                                    string strError;
                                    if (UpdateServerStatus(m_bIsConnect, out strError) == false)
                                    {
                                        m_parentManager.WriteLog($"ConnectionThread() : (UpdateServerStatus Error {strError})", LogTypes.Error);
                                    }

                                }
                                else if (m_bIsConnect == true && bResult == false)
                                {   // 연결 실패
                                    m_bIsConnect = false;
                                    m_parentManager.WriteLog("ConnectionThread() : " + m_parentManager.ServerIP + ":" + m_parentManager.Port.ToString() + " / " + this.IsConnected, LogTypes.Info);

                                    if (!m_runThread)
                                        return;

                                    string strError;
                                    if (UpdateServerStatus(m_bIsConnect, out strError) == false)
                                    {
                                        m_parentManager.WriteLog($"ConnectionThread() : (UpdateServerStatus Error {strError})", LogTypes.Error);
                                    }

                                }
                            }
                        }
                    }

                    Thread.Sleep(500);
                }
                catch (Exception e)
                {
                    m_parentManager.WriteLog("ConnectionThread() Exception: " + e.Message, LogTypes.Error);
                }
            }
        }

        private void RequestThread()
        {
            while (m_runThread)
            {
                try
                {
                    if (this.IsConnected)
                    {
                        m_nTransID++;

                        UInt16 nStartAddr = m_parentManager.StartAddress;  // 입력포트 주소 (setting.json Modbus_StartAddress, 기본 10 = 30011)
                        UInt16 nLength = RequestLength; // 데이터 읽을 갯수
                        int nSlaveID = 1;   // SlaveID

                        byte[] arrData = MakeRequestMsg(FC_ReadRegister, nSlaveID, m_nTransID, nStartAddr, nLength, IsReverse);
                        SendBytes(arrData);

                        if (m_nTransID > 10000)
                            m_nTransID = 0;
                    }

                    Thread.Sleep(1000);
                }
                catch (Exception e)
                {
                    m_parentManager.WriteLog("RequestThread() : " + e.Message, LogTypes.Error);
                }
            }
        }

        private bool UpdateServerStatus(bool bStatus, out string strError)
        {
            ViewModels.Sdms.SensorServerInfo serverInfo = new ViewModels.Sdms.SensorServerInfo()
            {
                ID = m_nServerSeqNo,
                ServerType = (int)m_parentManager.ServerType,
                Place = m_parentManager.ServerAlias,
                IP = m_parentManager.ServerIP,
                Port = m_parentManager.Port,
                Status = bStatus,
                SOPWebServerURL = m_parentManager.SOPWebServerURL,
                bUse = m_parentManager.Use,
                SiteID = m_parentManager.SiteID
            };

            bool bResult = m_parentManager.DataManager.GetUpdate().Update<ViewModels.Sdms.SensorServerInfo>(serverInfo, "ID=" + serverInfo.ID, out strError);

            return bResult;
        }

        public static byte[] MakeRequestMsg(byte code, int nSlaveID, UInt16 nTransID, UInt16 nStartAddr, UInt16 nLength, bool bIsReverse = true)
        {
            UInt16 nHeadLength = 6;

            byte[] arrSlaveID = BitConverter.GetBytes(nSlaveID);
            byte slaveID = arrSlaveID[0];

            byte[] arrTransID = BitConverter.GetBytes(nTransID);
            byte[] arrStartAddr = BitConverter.GetBytes(nStartAddr);
            byte[] arrLength = BitConverter.GetBytes(nLength);
            byte[] arrHeadLength = BitConverter.GetBytes(nHeadLength);

            if (bIsReverse)
            {
                Array.Reverse(arrTransID);
                Array.Reverse(arrStartAddr);
                Array.Reverse(arrLength);
                Array.Reverse(arrHeadLength);
            }

            byte[] data = new byte[12];

            //data[0] = 0x00;       // Transaction ID
            //data[1] = 0x00;       // Transaction ID
            Array.Copy(arrTransID, 0, data, 0, arrTransID.Length);

            data[2] = 0x00;         // TCP/IP 고정
            data[3] = 0x00;         // TCP/IP 고정

            //data[4] = 0x00;         // 길이
            //data[5] = 0x06;         // 길이
            Array.Copy(arrHeadLength, 0, data, 4, arrHeadLength.Length);

            data[6] = slaveID;      // Server ID

            data[7] = code;         // Function code

            //data[8] = 0x00;         // 읽어올 주소
            //data[9] = 0x00;         // 읽어올 주소 
            Array.Copy(arrStartAddr, 0, data, 8, arrStartAddr.Length);

            //data[10] = 0x00;      // 읽어올 갯수
            //data[11] = 0x06;      // 읽어올 갯수
            Array.Copy(arrLength, 0, data, 10, arrLength.Length);

            return data;
        }

        public void SendBytes(byte[] CmdBuff)
        {
            try
            {
                if (!this.IsClientDisposed && this.IsConnected)
                {
                    this.LengthAdd = false;
                    int nResult = this.Send(CmdBuff, 0, CmdBuff.Length);

                    if (nResult < 0)
                    {
                        lock (this)
                        {
                            if (this.IsConnected)
                                this.Close();

                            if (this.Client.Client != null)
                            {
                                if (this.Client.Connected)
                                    this.Client.Close();
                            }
                        }
                    }
                    else
                        m_parentManager.WriteBinaryLog(CmdBuff, 0, CmdBuff.Length, "[Send]");
                }
            }
            catch (Exception e)
            {
                m_parentManager.WriteLog("SendBytes() Exception : " + e.Message, LogTypes.Error);
            }
        }
    }
}
