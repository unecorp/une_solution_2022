using dnsCommunicateSopServer;
using dnsDapperDBUtil.DataAccessLayer.DAL;
using dnsData.Sensor;
using dnsSopID;
using IntegrationServer.Datas;
using Nipa.Model.Sdms.Sensor;
using Nipa.Model.Sdms.Spatial;
using System;
using System.Collections.Generic;
using System.Text;
using static dnsSopID.ID;

namespace IntegrationServer.Servers.Blackout.GG_D
{
    class BlackoutGGDManager : SyswillProcessManager, IServer
    {
        private int m_nServerSeqNo = -1;
        public int ServerSeqNo { get { return m_nServerSeqNo; } }

        public ID.ServerTypes ServerType { get { return ServerTypes.Blackout_GG_D; } }

        private string m_strServerAlias = "";
        public string ServerAlias { get { return m_strServerAlias; } }

        private bool m_isStarted = false;
        public bool IsConnected { get { return m_isStarted; } }

        public Logger Logger { get; set; }

        private ServerManager m_serverManager = null;
        public ServerManager GetServerManager() { return m_serverManager; }

        private ClientProvider m_provider = null;

        private bool m_bIsAlarm = false;

        private DataManager m_dataManager = null;
        public DataManager DataManager { get { return m_dataManager; } }

        private SopQueryManager m_sopQueryManager = null;


        public string SOPWebServerURL { get; set; }
        public string ServerIP { get; set; }

        // 모드버스 기본 Port
        private int m_nPort = 502;
        public int Port
        {
            get { return m_nPort; }
            set { m_nPort = value; }
        }

        private int m_nSiteID = -1;
        public int SiteID { get { return m_nSiteID; } }

        private bool m_use = false;
        public bool Use { get { return m_use; } }

        private SensorZone m_sensorZone = null;
        private TagInfo m_tag = null;

        private static string UniqueKey_BLACKOUT = "GGD_BLACKOUT";
        private int? m_nBlackoutID = null;

        // 시스윌로 마지막에 전달한 상전압과 정전 판정 결과
        private bool m_bSyswillUpdated = false;
        private int m_nSyswillVolA = 0, m_nSyswillVolB = 0, m_nSyswillVolC = 0;
        private bool m_bSyswillIsAlarm = false;

        // setting.json ServerProperties : 상전압 읽기 시작 주소와 FLOAT32 워드 순서
        // 워드 순서를 지정하지 않으면 예전처럼 4바이트를 UInt32로 해석한다.
        private UInt16 m_nStartAddress = 10;
        public UInt16 StartAddress { get { return m_nStartAddress; } }

        private string m_strWordOrder = null;

        public BlackoutGGDManager(ServerManager serverManager, DataManager dataManager, string strSOPWebServerURL, int nServerSeqNo, int nSiteID, string strServerIP, int nPort, string strServerAlias, bool use, Dictionary<ServerProperty, object> properties)
            : base(dataManager, nSiteID)
        {
            m_serverManager = serverManager;
            m_dataManager = (DataManager)dataManager.Clone();
            m_sopQueryManager = new SopQueryManager(strSOPWebServerURL + "/api/BlackOutSensor");

            m_nServerSeqNo = nServerSeqNo;
            this.ServerIP = strServerIP;
            m_nPort = nPort;
            m_strServerAlias = strServerAlias;

            this.SOPWebServerURL = strSOPWebServerURL;
            m_nSiteID = nSiteID;
            m_use = use;

            LoadProperties(properties);

            Init();

            m_provider = new ClientProvider(this, m_nServerSeqNo);
            m_provider.LengthAdd = false;
        }

        private void Init()
        {
            string strErrorMessage;
            string strCondition = string.Format("{0} = {1} and {2} IS NULL and {3} < 1000000 and {3} in (Select {4} from {5} where {6} = {7})",
                SensorZone.Fields.SensorType,
                (int)Facility.FacilityType.BLACKOUT,
                SensorZone.Fields.OrgSensorID,
                SensorZone.Fields.EquipZoneID,
                EquipmentZone.Fields.ID,
                EquipmentZone.TableName,
                EquipmentZone.Fields.SiteID,
                m_nSiteID);


            SensorZone sensorZone = m_dataManager.GetSelect().SelectFirst<SensorZone>(strCondition, out strErrorMessage);
            if (sensorZone == null)
            {
                WriteLog($"Init() SensorZone 실패: " + strErrorMessage, LogTypes.Error);
                return;
            }

            strCondition = string.Format("{0} = {1}", TagInfo.Fields.SensorZoneID, sensorZone.ID);

            TagInfo tag = m_dataManager.GetSelect().SelectFirst<TagInfo>(strCondition, out strErrorMessage);
            if (tag == null)
            {
                WriteLog($"Init() TagInfo 실패: " + strErrorMessage, LogTypes.Error);
                return;
            }

            m_sensorZone = sensorZone;
            m_tag = tag;

            string strSQL = $"Select {ETC.Fields.ID}, {ETC.Fields.UniqueKey} from {ETC.TableName} where {ETC.Fields.UniqueKey} = '{UniqueKey_BLACKOUT}'";
            IEnumerable<dynamic> datas = m_dataManager.GetSelect().Select(strSQL, out strErrorMessage);
            if (datas == null)
                return;

            foreach (var data in datas)
            {
                if (data.ID != null && data.ID is int &&
                    data.UniqueKey != null && data.UniqueKey is string)
                {
                    string strUniqueKey = (string)data.UniqueKey;

                    if (strUniqueKey == UniqueKey_BLACKOUT)
                    {
                        m_nBlackoutID = data.ID;
                        break;
                    }
                }
            }
        }

        public void Start()
        {
            WriteLog($"상전압 시작 주소 : {m_nStartAddress}, 워드 순서 : {m_strWordOrder ?? "UInt32"}", LogTypes.Info);
            m_provider.Start();
            m_isStarted = true;
        }

        public void Stop()
        {
            m_provider.Stop();
            m_isStarted = false;
        }

        private void LoadProperties(Dictionary<ServerProperty, object> properties)
        {
            object value;

            if (properties != null && properties.TryGetValue(ServerProperty.Modbus_StartAddress, out value) && value != null)
            {
                try
                {
                    m_nStartAddress = Convert.ToUInt16(value);
                }
                catch (Exception e)
                {
                    WriteLog($"Modbus_StartAddress 설정값 오류 ({value}) : {e.Message}. 기본값 {m_nStartAddress} 사용", LogTypes.Error);
                }
            }

            if (properties != null && properties.TryGetValue(ServerProperty.Modbus_WordOrder, out value) && value != null)
            {
                string strWordOrder = value.ToString().Trim().ToUpper();

                if (strWordOrder == "ABCD" || strWordOrder == "CDAB" || strWordOrder == "BADC" || strWordOrder == "DCBA")
                    m_strWordOrder = strWordOrder;
                else
                    WriteLog($"Modbus_WordOrder 설정값 오류 ({value}). UInt32로 해석", LogTypes.Error);
            }
        }

        // 수신한 4바이트(레지스터 2개, 수신 순서 그대로)를 설정한 워드 순서에 따라 값으로 변환한다.
        private double ToVoltage(byte[] arrData, int nOffset, bool bIsReverse)
        {
            byte[] arr = new byte[ClientProvider.FloatLeng];

            if (m_strWordOrder == null)
            {
                Array.Copy(arrData, nOffset, arr, 0, ClientProvider.FloatLeng);

                if (bIsReverse)
                    Array.Reverse(arr);

                return BitConverter.ToUInt32(arr, 0);
            }

            // 빅엔디안 ABCD 순서로 맞춘다.
            byte r0 = arrData[nOffset], r1 = arrData[nOffset + 1], r2 = arrData[nOffset + 2], r3 = arrData[nOffset + 3];
            switch (m_strWordOrder)
            {
                case "CDAB": arr[0] = r2; arr[1] = r3; arr[2] = r0; arr[3] = r1; break;
                case "BADC": arr[0] = r1; arr[1] = r0; arr[2] = r3; arr[3] = r2; break;
                case "DCBA": arr[0] = r3; arr[1] = r2; arr[2] = r1; arr[3] = r0; break;
                default:     arr[0] = r0; arr[1] = r1; arr[2] = r2; arr[3] = r3; break;
            }

            if (BitConverter.IsLittleEndian)
                Array.Reverse(arr);

            return BitConverter.ToSingle(arr, 0);
        }

        // 시스윌 tb_blackout_info 전압 컬럼(int)에 기록할 값
        private static int ToSyswillVoltage(double value)
        {
            if (double.IsNaN(value))
                return 0;

            if (value >= int.MaxValue)
                return int.MaxValue;

            if (value <= int.MinValue)
                return int.MinValue;

            return (int)Math.Round(value);
        }

        public void CheckAlarm(byte[] arrData, bool bIsReverse = true)
        {
            if (arrData == null || arrData.Length == 0 || arrData.Length != ClientProvider.RequestLength * ClientProvider.RegisterLength)
                return;

            double fVolA = ToVoltage(arrData, 0, bIsReverse);
            double fVolB = ToVoltage(arrData, (2 * ClientProvider.RegisterLength), bIsReverse);
            double fVolC = ToVoltage(arrData, (4 * ClientProvider.RegisterLength), bIsReverse);

            WriteLog($"CheckAlarm 데이터 상전압 A: {fVolA}, 상전압 B: {fVolB}, 상전압 C: {fVolC}", LogTypes.Info);

            bool isBlackout = fVolA <= 5000 || fVolB <= 5000 || fVolC <= 5000;

            // 시스윌 연동 : SOP 알람 상태 전이와 무관하게 상전압이나 정전 판정 결과가 바뀌면 전달한다.
            int nVolA = ToSyswillVoltage(fVolA), nVolB = ToSyswillVoltage(fVolB), nVolC = ToSyswillVoltage(fVolC);
            if (m_bSyswillUpdated == false || m_nSyswillVolA != nVolA || m_nSyswillVolB != nVolB || m_nSyswillVolC != nVolC || m_bSyswillIsAlarm != isBlackout)
            {
                if (UpdateBlackout(nVolA, nVolB, nVolC, isBlackout, m_dataManager, this.Logger, ServerType, m_nServerSeqNo))
                {
                    m_nSyswillVolA = nVolA;
                    m_nSyswillVolB = nVolB;
                    m_nSyswillVolC = nVolC;
                    m_bSyswillIsAlarm = isBlackout;
                    m_bSyswillUpdated = true;
                }
            }

            //if (fVolA <= 20 || fVolB <= 20 || fVolC <= 20)
            if (isBlackout)
            {
                // .TODO: UPS 정보를 이용해서 알람 단계 로직 필요함

                if (m_bIsAlarm == false)
                {   // 알람 발생
                    if (m_serverManager.SendSensorData(m_sopQueryManager, (int)Facility.FacilityType.BLACKOUT, m_tag.ID, m_tag.SensorZoneID.Value, true))
                    {
                        WriteLog($"정전 알람 발생 (SendSensorData 성공, Tag ID: {m_tag.ID}, SensorZoneID: {m_tag.SensorZoneID.Value})", LogTypes.Info);
                        m_bIsAlarm = true;

                        // 센서 상태값 업데이트
                        int nMaxDepth = 1;

                        if (m_nBlackoutID.HasValue)
                        {
                            Dictionary<ETC.Fields, object> dicSets = new Dictionary<ETC.Fields, object>();
                            dicSets[ETC.Fields.Status] = nMaxDepth;

                            string strCondition = string.Format("{0} = {1}", ETC.Fields.ID, m_nBlackoutID.Value);

                            if (m_dataManager.GetUpdate().Update<ETC, ETC.Fields>(dicSets, strCondition, out string strErrorMessage) == false)
                                WriteLog($"ETC Update Error (ID: {m_nBlackoutID}, Status: {nMaxDepth})", LogTypes.Error);
                        }
                    }
                    else
                    {
                        WriteLog($"SendSensorData 알람 발생 실패, Tag ID: {m_tag.ID}, SensorZoneID: {m_tag.SensorZoneID.Value})", LogTypes.Info);
                    }
                }
            }
            else if (m_bIsAlarm == true)
            {   // 알람 해제
                if (m_serverManager.SendSensorData(m_sopQueryManager, (int)Facility.FacilityType.BLACKOUT, m_tag.ID, m_tag.SensorZoneID.Value, false))
                {
                    WriteLog($"정전 알람 해제 (SendSensorData 성공, Tag ID: {m_tag.ID}, SensorZoneID: {m_tag.SensorZoneID.Value})", LogTypes.Info);
                    m_bIsAlarm = false;

                    // 센서 상태값 업데이트
                    int nMaxDepth = 0;

                    if (m_nBlackoutID.HasValue)
                    {
                        Dictionary<ETC.Fields, object> dicSets = new Dictionary<ETC.Fields, object>();
                        dicSets[ETC.Fields.Status] = nMaxDepth;

                        string strCondition = string.Format("{0} = {1}", ETC.Fields.ID, m_nBlackoutID.Value);

                        if (m_dataManager.GetUpdate().Update<ETC, ETC.Fields>(dicSets, strCondition, out string strErrorMessage) == false)
                            WriteLog($"ETC Update Error (ID: {m_nBlackoutID}, Status: {nMaxDepth})", LogTypes.Error);
                    }
                }
                else
                {
                    WriteLog($"SendSensorData 알람 해제 실패, Tag ID: {m_tag.ID}, SensorZoneID: {m_tag.SensorZoneID.Value})", LogTypes.Info);
                }
            }
        }


        public void WriteLog(string strLog, LogTypes type = LogTypes.Info)
        {
            if (this.Logger != null)
                this.Logger.Write(type, ServerType, m_nServerSeqNo, strLog);
            else
                Logger.Instance.Write(type, ServerType, m_nServerSeqNo, strLog);
        }

        public string WriteBinaryLog(byte[] bytes, int nIndex, int len, string strTag)
        {
            string strBytesLog = Logger.GetByteString(bytes, nIndex, len);
            WriteLog(strTag + " : " + strBytesLog, LogTypes.Info);
            return strTag + " : " + strBytesLog;
        }
    }
}
