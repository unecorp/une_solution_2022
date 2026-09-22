using dnsCommunicateSopServer;
using dnsDapperDBUtil.DataAccessLayer.DAL;
using IntegrationServer.Datas;
using IntegrationServer.Managers;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using static dnsSopID.ID;

namespace IntegrationServer.Servers
{
    /// <summary>
    /// 서버들과 통신 관리
    /// 1. ServerManager가 사용하는 각 서버의 Manager(ex:FireJohnsonManager)를 실행
    /// 2. 각 서버의 Provider가 신호 데이터를 수신 OnReceiveData
    /// 3. 전달 받은 데이터를 SopServerManager가 SOPWebServer로 송신
    /// </summary>
    public class ServerManager
    {
        private DataManager m_dataManager = null;
        private ServerSetting m_serverSetting = null;
        private SopServerManager m_sopServerManager = null; // SOPWebServer 통신
        private SensorManager m_sensorManager = null;
        private AlarmManager m_alarmManager = null;

        // Key : 서버 고유번호(SeqNo), 같은 종류의 서버가 여러개 필요할 경우 구분하기 위해 필요하다
        private Dictionary<int, IServer> m_dicServerDatas = null;

        public ServerManager(ServerSetting serverSetting)
        {
            // .net core 환경에서 ssl 접속시 예외처리
            System.Net.ServicePointManager.ServerCertificateValidationCallback += delegate (object sender,
                System.Security.Cryptography.X509Certificates.X509Certificate certificate,
                System.Security.Cryptography.X509Certificates.X509Chain chain,
                System.Net.Security.SslPolicyErrors sslPolicyErrors)
            {
                return true;
            };

            m_sopServerManager = new SopServerManager();
            m_dataManager = new DataManager(serverSetting.DbType, serverSetting.DbIP, serverSetting.DbName, serverSetting.DbID, serverSetting.DbPW);
            m_serverSetting = serverSetting;

            SetServer();
        }

        private void SetServer()
        {
            if (m_dicServerDatas == null)
                m_dicServerDatas = new Dictionary<int, IServer>();
            else
                m_dicServerDatas.Clear();

            if (m_serverSetting == null || m_serverSetting.ServerDatas == null)
            {
                m_dicServerDatas = null;
                return;
            }

            foreach (var item in m_serverSetting.ServerDatas)
            {
                if (!item.Use)
                    continue;

                switch (item.ServerType)
                {
                    case (int)ServerTypes.Fire_Johnson:
                        if (!item.ServerProperties.ContainsKey(ServerProperty.MuxType))
                        {
                            Logger.Instance.Write(LogTypes.Error, ServerTypes.Fire_Johnson, item.SeqNo, "MuxType 정의 안됨");
                            continue;
                        }

                        MuxTypes muxType = (MuxTypes)Convert.ToInt32(item.ServerProperties[ServerProperty.MuxType]);
                        m_dicServerDatas[item.SeqNo] = new Fire.Johnson.JohnsonManager(this, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, muxType, item.ServerAlias);
                        break;
                    case (int)ServerTypes.Fire_Siemens:
                        if (!item.ServerProperties.ContainsKey(ServerProperty.ServerMode))
                        {
                            Logger.Instance.Write(LogTypes.Error, ServerTypes.Fire_Siemens, item.SeqNo, "ServerMode 정의 안됨");
                            continue;
                        }

                        ServerModes serverMode = (ServerModes)Convert.ToInt32(item.ServerProperties[ServerProperty.ServerMode]);

                        m_dicServerDatas[item.SeqNo] = new Fire.Siemens.SiemensManager(this, item.SOPWebServerURL, item.SeqNo, item.IP, item.Port, serverMode, item.ServerAlias);
                        break;
                    case (int)ServerTypes.Fire_EmergencyBroadcast:
                        m_dicServerDatas[item.SeqNo] = new Fire.EmergencyBroadcast.EmrBrdManager(this, item.SOPWebServerURL, item.SeqNo, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.CCTV_S1_SVMS:
                        m_dicServerDatas[item.SeqNo] = new CCTV.S1.SVMS.SvmsManager(this, m_dataManager, item.SOPWebServerURL, item.SiteID, item.SeqNo, item.ServerProperties, item.ServerAlias);
                        break;
                    case (int)ServerTypes.CCTV_ShinilTech:
                        m_dicServerDatas[item.SeqNo] = new CCTV.ShinilTech.ShinilTechManager(this, m_dataManager, item.SOPWebServerURL, item.SiteID, item.SeqNo, item.ServerAlias);
                        break;
                    case (int)ServerTypes.EmergencyBell_MPia:
                        object value;

                        if (item.ServerProperties.TryGetValue(ServerProperty.UniqueKeyTag, out value))
                        {
                            if (value is string)
                            {
                                string strUniqueKeyTag = (string)value;
                                m_dicServerDatas[item.SeqNo] = new EmergencyBell.MPia.MPiaManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.IP, item.Port, strUniqueKeyTag, item.ServerAlias);
                            }
                        }
                        break;
                    case (int)ServerTypes.ContactSignal:
                        m_dicServerDatas[item.SeqNo] = new ContactSignal.Sollae.ContactManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerProperties, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Weather:
                        object value1, value2;
                        string strServiceKey;

                        if (item.ServerProperties.TryGetValue(ServerProperty.ServiceKey, out value1) && item.ServerProperties.TryGetValue(ServerProperty.Weather_KoreaData, out value2))
                        {
                            if (value1 is string && value2 is JArray)
                            {
                                strServiceKey = (string)value1;
                                List<string> weatherDatas = JArrayToList((JArray)value2);
                                m_dicServerDatas[item.SeqNo] = new Weather.Korea.WeatherManager(this, m_dataManager, item.SeqNo, strServiceKey, weatherDatas, item.ServerAlias);
                            }
                        }
                        break;
                    case (int)ServerTypes.PSM_Senko:
                        m_dicServerDatas[item.SeqNo] = new AirPollution.Senko.AirPollutionManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.MES_Hansol:
                        string strDbHost, strDbId, strDbPw, strSid;

                        if (GetMesHansolParameters(item, out strDbHost, out strDbId, out strDbPw, out strSid))
                        {
                            m_dicServerDatas[item.SeqNo] = new MES.Hansol.HansolManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, strDbHost, strDbId, strDbPw, strSid, item.ServerAlias);
                        }
                        break;
                    case (int)ServerTypes.Worker_SWayM:
                        object objSWayM;

                        if (item.ServerProperties.TryGetValue(ServerProperty.BaseUrl, out objSWayM))
                        {
                            if (objSWayM is string)
                                m_dicServerDatas[item.SeqNo] = new Worker.SWayM.SWaymManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, (string)objSWayM, item.ServerAlias);
                        }
                        break;
                    case (int)ServerTypes.Fire_Safesystem:
                        m_dicServerDatas[item.SeqNo] = new Fire.Safesystem.SafesystemManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.EmergencyBell_ITSeng:
                        m_dicServerDatas[item.SeqNo] = new EmergencyBell.ITSeng.ITSengManager(this, m_dataManager, item.SOPWebServerURL, item.SiteID, item.SeqNo, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.Fire_Taesan:
                        m_dicServerDatas[item.SeqNo] = new Fire.Taesan.TaesanManager(this, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, m_dataManager);
                        break;
                    case (int)ServerTypes.Elevator_HD:
                        m_dicServerDatas[item.SeqNo] = new Elevator.Hyundai.HDManager(this, m_dataManager, item.SiteID, item.SeqNo, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.Fms_SumpPit_Lozi:
                        m_dicServerDatas[item.SeqNo] = new FMS.Lozi.SumpPitManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Earthquake_GG:
                        m_dicServerDatas[item.SeqNo] = new Earthquake.GG.EarthquakeManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.UPS_GG:
                        m_dicServerDatas[item.SeqNo] = new UPS.GG.UpsGGManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Door_DDS:
                        m_dicServerDatas[item.SeqNo] = new Door.DDS.DoorManager(this, m_dataManager, item.SeqNo, item.SiteID, item.ServerAlias);
                        break;
                    case (int)ServerTypes.ParkingGate_rs:
                        m_dicServerDatas[item.SeqNo] = new ParkingGate.RS.ParkingGateManager(this, m_dataManager, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.Mqtt_Corners:
                        m_dicServerDatas[item.SeqNo] = new MQTT.Corners.MqttManager(this, m_dataManager, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.ServerProperties);
                        break;
                    case (int)ServerTypes.Blackout_GG_F:
                        m_dicServerDatas[item.SeqNo] = new Blackout.GG_F.BlackoutGGFManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Fms_SumpPit_GG_F:
                        m_dicServerDatas[item.SeqNo] = new FMS.GG_F.SumpPitGGFManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Fire_AllLite:
                        m_dicServerDatas[item.SeqNo] = new Fire.AllLite.AllLiteManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Fire_JTECH:
                        m_dicServerDatas[item.SeqNo] = new Fire.JTECH.JTECHManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Blackout_GG_D:
                        m_dicServerDatas[item.SeqNo] = new Blackout.GG_D.BlackoutGGDManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use, item.ServerProperties);
                        break;
                    case (int)ServerTypes.Fms_SumpPit_GG_D:
                        m_dicServerDatas[item.SeqNo] = new FMS.GG_D.SumpPitGGDManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.EmergencyBell_Eraeseeds:
                        m_dicServerDatas[item.SeqNo] = new EmergencyBell.Eraeseeds.EraeseedsManager(this, m_dataManager, item.SOPWebServerURL, item.SiteID, item.SeqNo, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Door_Biostar:
                        m_dicServerDatas[item.SeqNo] = new Door.Biostar.BiostarDoorManager(this, m_dataManager, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.ServerProperties);
                        break;
                    case (int)ServerTypes.Elevator_OTIS:
                        m_dicServerDatas[item.SeqNo] = new Elevator.Otis.OtisManager(this, m_dataManager, item.SiteID, item.SeqNo, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.Elevator_IBMS:
                        m_dicServerDatas[item.SeqNo] = new Elevator.IBMS.IBMSManager(this, m_dataManager, item.SiteID, item.SeqNo, item.IP, item.Port, item.ServerAlias);
                        break;
                    case (int)ServerTypes.EmergencyBell_GGEducation:
                        m_dicServerDatas[item.SeqNo] = new EmergencyBell.GGEdu.EduManager(this, m_dataManager, item.SOPWebServerURL, item.SiteID, item.SeqNo, item.IP, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Blackout_GG_G:
                        m_dicServerDatas[item.SeqNo] = new Blackout.GG_G.BlackoutGG_GManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.Fire_Singwang:
                        m_dicServerDatas[item.SeqNo] = new Fire.Singwang.SingwangManager(this, m_dataManager, item.SOPWebServerURL, item.SeqNo, item.SiteID, item.IP, item.Port, item.ServerAlias, item.Use);
                        break;
                    case (int)ServerTypes.EmergencyBell_Nextronics:
                        m_dicServerDatas[item.SeqNo] = new EmergencyBell.Nextronics.NextronicsManager(this, m_dataManager, item.SOPWebServerURL, item.SiteID, item.SeqNo, item.Port, item.ServerAlias, item.Use);
                        break;
                }
            }
        }

        private bool GetMesHansolParameters(ServerData item, out string strDbHost, out string strDbId, out string strDbPw, out string strSid)
        {
            strDbHost = strDbId = strDbPw = strSid = null;
            object value;

            if (item.ServerProperties.TryGetValue(ServerProperty.DB_Host, out value))
            {
                if (value is string)
                    strDbHost = (string)value;
            }

            if (item.ServerProperties.TryGetValue(ServerProperty.DB_ID, out value))
            {
                if (value is string)
                    strDbId = (string)value;
            }

            if (item.ServerProperties.TryGetValue(ServerProperty.DB_PW, out value))
            {
                if (value is string)
                    strDbPw = (string)value;
            }

            if (item.ServerProperties.TryGetValue(ServerProperty.Oracle_SID, out value))
            {
                if (value is string)
                    strSid = (string)value;
            }

            if (strDbHost == null || strDbId == null || strDbPw == null || strSid == null)
                return false;

            return true;
        }

        private List<string> JArrayToList(JArray arr)
        {
            List<string> arrDatas = new List<string>();

            foreach (var item in arr.Children())
            {
                string strValue = item.Value<string>().ToString();
                arrDatas.Add(strValue);
            }

            return arrDatas;
        }

        public bool BeginServer()
        {
            if (m_dicServerDatas == null)
                return false;

            m_sensorManager = new SensorManager(m_dataManager);
            m_alarmManager = new AlarmManager(this, m_dataManager, m_serverSetting.SOPWebServerFrontURL);

            //List<int> sensorServerIDs = m_serverSetting.ServerDatas.Where(p => p.Use).Select(p => p.SeqNo).ToList();
            m_sensorManager.LoadData(m_serverSetting.ServerDatas);

            foreach (KeyValuePair<int, IServer> pair in m_dicServerDatas)
            {
                if (pair.Value.IsConnected == false)
                {
                    //string serverTxt = dnsSopID.ID.GetServerText(pair.Value.ServerType);
                    string serverTxt = pair.Value.ServerAlias;
                    if (serverTxt == null || serverTxt == "")
                        serverTxt = dnsSopID.ID.GetServerText(pair.Value.ServerType);
                    else
                        serverTxt = dnsSopID.ID.GetServerText(pair.Value.ServerType) + "_" + serverTxt;

                    pair.Value.Logger = Logger.Instance.Clone(m_serverSetting.LogPath, serverTxt);
                    pair.Value.Start();
                    pair.Value.Logger.Write(LogTypes.Info, pair.Value.ServerType, pair.Value.ServerSeqNo, "Start");
                }
            }

            return true;
        }

        public void StopServer()
        {
            if (m_dicServerDatas == null)
                return;

            m_sensorManager.Stop();
            m_alarmManager.Stop();

            foreach (KeyValuePair<int, IServer> pair in m_dicServerDatas)
            {
                pair.Value.Stop();
                pair.Value.Logger.Close();
            }
        }

        /// <summary>
        /// 서버 연결 상태 DB 저장
        /// </summary>
        /// <param name="nServerSeqNo"></param>
        /// <param name="ServerType"></param>
        /// <param name="bState"></param>
        /// <returns></returns>
        public bool UpdateConnectState(int nServerSeqNo, ServerTypes ServerType, bool bState)
        {
            string strError;
            
            dynamic nCount = m_dataManager.GetSelect().SelectFirst($"select count(*) cnt from OptionSDMS where PropertyName like '{ServerType}%'", out strError);
            if (nCount == null)
            {
                Logger.Instance.Write(LogTypes.Error, ServerType, nServerSeqNo, strError);
                return false;
            }

            bool bResult = true;
            if (nCount.cnt == 0)
            {
                dynamic site = m_dataManager.GetSelect().SelectFirst("select id from site", out strError);
                if (site == null)
                {
                    Logger.Instance.Write(LogTypes.Error, ServerType, nServerSeqNo, strError);
                    return false;
                }

                int nSiteID = site.id;
                string strSQL = $@"insert into OptionSDMS (propertyName, propertyValue, SiteID, Description) 
                                   values ()";
                               
            }

            return bResult;
        }

        public bool SendSensorData(SopQueryManager sopQueryManager, int nSensorType, int nTagID, int nSensorZoneID, bool bIsAlarm)
        {
            return m_sopServerManager.SendSensorData(sopQueryManager, nSensorType, nTagID, nSensorZoneID, bIsAlarm);
        }

        public bool SendSensorData(SopQueryManager sopQueryManager, int nSensorType, int nTagID, int nSensorZoneID, bool bIsAlarm, int nAlarmLevel)
        {
            return m_sopServerManager.SendSensorData(sopQueryManager, nSensorType, nTagID, nSensorZoneID, bIsAlarm, nAlarmLevel);
        }

        public void SendSensorDataAsync(SopQueryManager sopQueryManager, int nSensorType, int nTagID, int nSensorZoneID, bool bIsAlarm)
        {
            m_sopServerManager.SendSensorDataAsync(sopQueryManager, nSensorType, nTagID, nSensorZoneID, bIsAlarm);
        }

        public bool SendClearAlarm(SopQueryManager sopQueryManager, int nSensorType, int nTagID, int nSensorZoneID, int nClearType)
        {
            return m_sopServerManager.SendClearAlarm(sopQueryManager, nSensorType, nTagID, nSensorZoneID, nClearType);
        }

        public void SendClearAlarmAsync(SopQueryManager sopQueryManager, int nSensorType, int nTagID, int nSensorZoneID, int nClearType)
        {
            m_sopServerManager.SendClearAlarmAsync(sopQueryManager, nSensorType, nTagID, nSensorZoneID, nClearType);
        }

        public bool SendClearPsmAlarm(SopQueryManager sopQueryManager, int nSensorZoneID)
        {
            return m_sopServerManager.SendClearPsmAlarm(sopQueryManager, nSensorZoneID);
        }

        public bool SendAllClear(SopQueryManager sopQueryManager, int? nSiteID = null)
        {
            return m_sopServerManager.SendAllClear(sopQueryManager, nSiteID);
        }
        public void SendAllClearAsync(SopQueryManager sopQueryManager)
        {
            m_sopServerManager.SendAllClearAsync(sopQueryManager);
        }
    }
}
