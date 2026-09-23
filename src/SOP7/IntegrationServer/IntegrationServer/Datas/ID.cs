using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntegrationServer.Datas
{
    /// <summary>
    /// 서버 속성
    /// </summary>
    public enum ServerProperty
    {
        /// <summary>
        /// 화재-동방
        /// </summary>
        MuxType = 0,
        /// <summary>
        /// 화재-지멘스
        /// </summary>
        ServerMode = 1,
        /// <summary>
        /// S1-SVMS, ShinilTech-Camera
        /// </summary>
        SvmsIP,
        SvmsPort,
        SvmsID,
        SvmsPW,
        RtspServerName,
        RunRtspServer,
        CctvConfig,
        ContactType,
        ContactSensorType,
        ContactSensorID,
        ContactAlarmDepth,
        /// <summary>
        /// Sensor Table에서 사용하는 고유키 앞자리
        /// </summary>
        UniqueKeyTag,
        ServiceKey,
        Weather_KoreaData,
        DB_Host,
        DB_ID,
        DB_PW,
        Oracle_SID,
        BaseUrl,
        /// <summary>
        /// 코너스 MQTT 통신을 위하여 필요한 데이터
        /// </summary>
        SiteID,
        MpcID,
        /// <summary>
        /// Suprema api를 이용하여 출입통제 시스템과 통신하기 위한 데이터
        /// </summary>
        Biostar_id,
        Biostar_pw,
        /// <summary>
        /// Modbus 계측기 : 읽기 시작 주소(0부터 시작하는 PDU 주소, 예: 30029 -> 28)와 FLOAT32 워드 순서(ABCD, CDAB, BADC, DCBA)
        /// </summary>
        Modbus_StartAddress,
        Modbus_WordOrder,
        /// <summary>
        /// Modbus 계측기 : 읽은 값에 곱할 배율(기본 1). 예를 들어 0.1kV 단위로 보내면 100
        /// </summary>
        Modbus_ValueScale
    }

    public enum DbTypes
    {
        sqlserver = 0,
        mysql = 1,
        oracle = 2
    }

    public enum LogTypes
    {
        Info = 0,
        Error = 1
    }

    public enum EventTypes
    {
        Unknown,
        DetectSensor,
        ClearSensor,
        ClearAll,
        DetectError,
        ClearError
    }

    public enum MuxTypes 
    { 
        None = 0, 
        Mux1 = 1, 
        Mux2 = 2 
    }

    public enum ServerModes
    {
        Client = 0,
        Server = 1            
    }

    public enum ContactTypes
    {
        First_Dry = 0,
        Second_Wet = 1,
        Both = 2
    }

    public enum AlarmDepths
    {
        None = 0,
        Interest = 1,
        Caution,
        Alert,
        Serious
    }
}
